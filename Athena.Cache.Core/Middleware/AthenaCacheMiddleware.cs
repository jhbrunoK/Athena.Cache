using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Diagnostics;
using Athena.Cache.Core.Interfaces;
using Athena.Cache.Core.Models;
using Athena.Cache.Core.ObjectPools;
using Athena.Cache.Core.Observability;
using Athena.Cache.Core.Resilience;
using Athena.Cache.Core.Memory;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Buffers;
using System.Text;

namespace Athena.Cache.Core.Middleware;

/// <summary>
/// Athena 캐시 미들웨어
/// HTTP 요청을 가로채서 캐시 확인 및 응답 캐싱 처리
/// </summary>
public class AthenaCacheMiddleware(
    RequestDelegate next,
    IAthenaCache cache,
    ICacheKeyGenerator keyGenerator,
    ICacheInvalidator invalidator,
    ICacheConfigurationRegistry configRegistry,
    AthenaCacheOptions options,
    CachedResponsePool responsePool,
    CachePerformanceMonitor performanceMonitor,
    AthenaCacheMetrics metrics,
    CacheHealthMonitor healthMonitor,
    CacheCircuitBreaker circuitBreaker,
    ILogger<AthenaCacheMiddleware> logger,
    IIntelligentCacheManager? intelligentCacheManager = null)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // GET 요청만 캐싱 (POST, PUT, DELETE 등은 제외)
        if (!IsGetRequest(context))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // 캐시 비활성화 체크
        if (IsCacheDisabled(context))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        using var activity = AthenaCacheMetrics.StartActivity("athena_cache.middleware.invoke");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // 캐시 설정 가져오기
            var cacheConfig = GetCacheConfiguration(context);
            if (cacheConfig == null || !cacheConfig.Enabled)
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            // 캐시 키 생성
            var cacheKey = await GenerateCacheKeyAsync(context, cacheConfig).ConfigureAwait(false);
            if (string.IsNullOrEmpty(cacheKey))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            // HttpContext에 생성된 키 저장 (Action Filter에서 사용)
            context.Items["AthenaCache.GeneratedKey"] = cacheKey;

            // Circuit Breaker를 통해 캐시 조회 실행
            var cachedResponse = await circuitBreaker.ExecuteAsync(
                "cache_get",
                async () =>
                {
                    using var cacheGetMeasurement = performanceMonitor.StartMeasurement("cache_get");
                    var result = await cache.GetAsync<CachedResponse>(cacheKey).ConfigureAwait(false);
                    
                    // OpenTelemetry 메트릭 기록
                    metrics.RecordOperationDuration(cacheGetMeasurement.Elapsed, "get", "middleware");
                    if (result != null && !string.IsNullOrEmpty(result.Content))
                    {
                        metrics.RecordValueSize(Encoding.UTF8.GetByteCount(result.Content), "cached_response");
                    }
                    
                    return result;
                },
                fallback: async () => 
                {
                    logger.LogWarning("Cache circuit breaker open, bypassing cache for key: {CacheKey}", cacheKey);
                    return null;
                }).ConfigureAwait(false);
            
            if (cachedResponse != null)
            {
                // 캐시 히트 기록
                performanceMonitor.RecordCacheHit();
                healthMonitor.RecordCacheHit();
                metrics.RecordCacheHit("middleware", ExtractKeyPattern(cacheKey));
                
                // 지능형 캐시 관리자에 접근 기록
                if (intelligentCacheManager != null)
                {
                    await intelligentCacheManager.RecordCacheAccessAsync(cacheKey, CacheAccessType.Hit).ConfigureAwait(false);
                }
                
                stopwatch.Stop();
                metrics.RecordOperationDuration(stopwatch.Elapsed, "cache_hit", "middleware");
                activity?.SetTag("cache.result", "hit");
                
                await WriteCachedResponseAsync(context, cachedResponse);
                return;
            }
            
            // 캐시 미스 기록
            performanceMonitor.RecordCacheMiss();
            healthMonitor.RecordCacheMiss();
            metrics.RecordCacheMiss("middleware", ExtractKeyPattern(cacheKey));
            
            // 지능형 캐시 관리자에 미스 기록
            if (intelligentCacheManager != null)
            {
                await intelligentCacheManager.RecordCacheAccessAsync(cacheKey, CacheAccessType.Miss).ConfigureAwait(false);
            }

            activity?.SetTag("cache.result", "miss");
            
            // 캐시 미스 - 응답 캐싱
            await CacheResponseAsync(context, cacheKey, cacheConfig);
            
            stopwatch.Stop();
            metrics.RecordOperationDuration(stopwatch.Elapsed, "cache_miss", "middleware");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            // 에러 메트릭 기록
            healthMonitor.RecordError("middleware", ex);
            metrics.RecordCacheError("middleware", ex.GetType().Name, ex.Message);
            AthenaCacheMetrics.RecordException(activity, ex);
            
            logger.LogError(ex, "Error in AthenaCacheMiddleware for path: {Path}", context.Request.Path);

            if (options.ErrorHandling.SilentFallback)
            {
                await next(context).ConfigureAwait(false);

                if (options.ErrorHandling.CustomErrorHandler != null)
                {
                    await options.ErrorHandling.CustomErrorHandler(ex);
                }
            }
            else
            {
                throw;
            }
        }
    }

    private static bool IsGetRequest(HttpContext context)
    {
        return string.Equals(context.Request.Method, CachedConstants.HttpGet, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCacheDisabled(HttpContext context)
    {
        return context.Items.ContainsKey("AthenaCache.Disabled") &&
               context.Items["AthenaCache.Disabled"] is true;
    }

    private CacheConfiguration? GetCacheConfiguration(HttpContext context)
    {
        // 라우팅 정보에서 컨트롤러와 액션 이름 가져오기
        var routeData = context.GetRouteData();
        if (routeData?.Values == null)
            return null;

        var controllerName = routeData.Values["controller"]?.ToString();
        var actionName = routeData.Values["action"]?.ToString();

        if (string.IsNullOrEmpty(controllerName) || string.IsNullOrEmpty(actionName))
            return null;

        // Controller 접미사 추가 (필요한 경우)
        if (!controllerName.EndsWith(CachedConstants.ControllerSuffix))
            controllerName += CachedConstants.ControllerSuffix;

        // 주입된 레지스트리에서 설정 조회
        return configRegistry.GetConfiguration(controllerName, actionName);
    }

    private Task<string> GenerateCacheKeyAsync(HttpContext context, CacheConfiguration config)
    {
        // 컬렉션 풀에서 Dictionary 대여
        var parameters = CollectionPools.RentStringObjectDictionary();
        
        try
        {
            // 1. 라우트 파라미터 수집 (최우선)
            var routeData = context.GetRouteData();
            if (routeData?.Values != null)
            {
                foreach (var routeValue in routeData.Values)
                {
                    var key = routeValue.Key;
                    
                    // 시스템 파라미터 제외 (controller, action은 이미 캐시 키에 포함됨)
                    if (IsSystemParameter(key))
                        continue;
                        
                    // 제외 파라미터 체크
                    if (config.ExcludeParameters.Contains(key, StringComparer.OrdinalIgnoreCase))
                        continue;

                    parameters[key] = routeValue.Value;
                }
            }

            // 2. 쿼리 파라미터 수집 (라우트 파라미터가 없는 경우만)
            foreach (var param in context.Request.Query)
            {
                var key = param.Key;
                
                // 이미 라우트 파라미터로 추가된 경우 스킵 (라우트 파라미터 우선)
                if (parameters.ContainsKey(key))
                    continue;
                    
                // 제외 파라미터 체크
                if (config.ExcludeParameters.Contains(key, StringComparer.OrdinalIgnoreCase))
                    continue;

                parameters[key] = param.Value.ToString();
            }

            // 3. 추가 파라미터 포함 (가장 낮은 우선순위)
            foreach (var additionalParam in config.AdditionalKeyParameters)
            {
                // 이미 추가된 경우 스킵
                if (parameters.ContainsKey(additionalParam))
                    continue;
                    
                if (context.Items.TryGetValue(additionalParam, out var value))
                {
                    parameters[additionalParam] = value;
                }
            }

            // 캐시 키 생성
            var controllerName = config.CustomKeyPrefix ?? config.Controller;
            var cacheKey = keyGenerator.GenerateKey(controllerName, config.Action, (IDictionary<string, object?>)parameters);

            if (options.Logging.LogKeyGeneration)
            {
                // LINQ 없이 파라미터 문자열 생성 (zero allocation 최적화)
                var parameterString = BuildParameterString(parameters);
                logger.LogDebug("Generated cache key: {CacheKey} for {Controller}.{Action} with parameters: {Parameters}",
                    cacheKey, config.Controller, config.Action, parameterString);
            }

            return Task.FromResult(cacheKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate cache key for {Controller}.{Action}",
                config.Controller, config.Action);
            return Task.FromResult(string.Empty);
        }
        finally
        {
            // 풀에 Dictionary 반환
            CollectionPools.Return(parameters);
        }
    }

    /// <summary>
    /// 시스템 파라미터 여부 확인 (캐시 키에서 제외)
    /// </summary>
    private static bool IsSystemParameter(string parameterName)
    {
        return string.Equals(parameterName, "controller", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(parameterName, "action", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// LINQ 없이 파라미터 문자열 생성 (zero allocation 최적화)
    /// </summary>
    private static string BuildParameterString(IDictionary<string, object?> parameters)
    {
        if (parameters.Count == 0)
            return string.Empty;

        var sb = HighPerformanceStringPool.RentStringBuilder(parameters.Count * 20);
        try
        {
            var isFirst = true;
            foreach (var kvp in parameters)
            {
                if (!isFirst)
                {
                    sb.Append(", ");
                }
                
                sb.Append(kvp.Key);
                sb.Append('=');
                sb.Append(kvp.Value?.ToString() ?? "null");
                
                isFirst = false;
            }
            
            return sb.ToString();
        }
        finally
        {
            HighPerformanceStringPool.ReturnStringBuilder(sb, parameters.Count * 20);
        }
    }

    private async Task WriteCachedResponseAsync(HttpContext context, CachedResponse cachedResponse)
    {
        // 응답 헤더 설정
        context.Response.StatusCode = cachedResponse.StatusCode;
        context.Response.ContentType = cachedResponse.ContentType;

        foreach (var header in cachedResponse.Headers)
        {
            context.Response.Headers[header.Key] = header.Value;
        }

        // 캐시 히트 헤더 추가
        context.Response.Headers["X-Athena-Cache"] = CachedConstants.CacheHit;

        // 응답 본문 작성
        if (!string.IsNullOrEmpty(cachedResponse.Content))
        {
            await context.Response.WriteAsync(cachedResponse.Content);
        }

        if (options.Logging.LogCacheHitMiss)
        {
            logger.LogInformation("Cache HIT for {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }
    }

    private async Task CacheResponseAsync(HttpContext context, string cacheKey, CacheConfiguration config)
    {
        // 응답 스트림 캡처를 위한 래퍼
        var originalBodyStream = context.Response.Body;
        using var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        try
        {
            // 다음 미들웨어 실행
            await next(context).ConfigureAwait(false);

            // 성공 응답만 캐싱 (200-299)
            if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
            {
                // 메모리 효율적인 응답 내용 읽기
                responseBodyStream.Seek(0, SeekOrigin.Begin);
                var responseContent = await ReadStreamEfficientlyAsync(responseBodyStream).ConfigureAwait(false);

                // Object Pool에서 캐시 응답 객체 가져오기
                var cachedResponse = responsePool.Get();
                try
                {
                    // 만료 시간 결정 (지능형 관리자 우선)
                    var expiration = intelligentCacheManager != null
                        ? await intelligentCacheManager.CalculateAdaptiveTtlAsync(cacheKey).ConfigureAwait(false)
                        : DetermineExpiration(config);
                    var expiresAt = DateTime.UtcNow.Add(expiration);
                    
                    // 객체 초기화
                    // 캐시 가능한 헤더만 필터링 (LINQ 없이)
                    var cacheableHeaders = new Dictionary<string, string>();
                    foreach (var header in context.Response.Headers)
                    {
                        if (IsCacheableHeader(header.Key))
                        {
                            cacheableHeaders[header.Key] = header.Value.ToString();
                        }
                    }
                    
                    cachedResponse.Initialize(
                        context.Response.StatusCode,
                        context.Response.ContentType ?? CachedConstants.ContentTypeJson,
                        responseContent,
                        cacheableHeaders,
                        expiresAt
                    );

                    // Circuit Breaker를 통해 캐시 저장 실행
                    await circuitBreaker.ExecuteAsync(
                        "cache_set",
                        async () =>
                        {
                            using var cacheSetMeasurement = performanceMonitor.StartMeasurement("cache_set");
                            await cache.SetAsync(cacheKey, cachedResponse, expiration).ConfigureAwait(false);
                            
                            // OpenTelemetry 메트릭 기록
                            metrics.RecordOperationDuration(cacheSetMeasurement.Elapsed, "set", "middleware");
                            metrics.RecordKeySize(Encoding.UTF8.GetByteCount(cacheKey), ExtractKeyPattern(cacheKey));
                            metrics.RecordValueSize(Encoding.UTF8.GetByteCount(responseContent), "cached_response");
                            
                            return Task.CompletedTask;
                        },
                        fallback: async () =>
                        {
                            logger.LogWarning("Cache circuit breaker open, unable to cache response for key: {CacheKey}", cacheKey);
                            return Task.CompletedTask;
                        }).ConfigureAwait(false);
                    
                    // 지능형 캐시 관리자에 설정 기록
                    if (intelligentCacheManager != null)
                    {
                        await intelligentCacheManager.RecordCacheAccessAsync(cacheKey, CacheAccessType.Set).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // 예외 발생 시 객체를 풀에 반환
                    responsePool.Return(cachedResponse);
                    throw;
                }
                // 정상적으로 캐시에 저장된 경우 객체는 풀에 반환하지 않음 (캐시에서 사용 중)

                // 테이블 추적 설정 (LINQ 없이)
                if (config.InvalidationRules.Count > 0)
                {
                    var tableSet = new HashSet<string>();
                    for (int i = 0; i < config.InvalidationRules.Count; i++)
                    {
                        tableSet.Add(config.InvalidationRules[i].TableName);
                    }
                    
                    var tablesToTrack = new string[tableSet.Count];
                    tableSet.CopyTo(tablesToTrack);
                    
                    await invalidator.TrackCacheKeyAsync(tablesToTrack, cacheKey).ConfigureAwait(false);
                }

                if (options.Logging.LogCacheHitMiss)
                {
                    logger.LogInformation("Cached response for {Method} {Path} with key {CacheKey}",
                        context.Request.Method, context.Request.Path, cacheKey);
                }
            }

            // 캐시 미스 헤더 추가
            context.Response.Headers["X-Athena-Cache"] = CachedConstants.CacheMiss;

            // 원본 스트림으로 응답 복사
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            await responseBodyStream.CopyToAsync(originalBodyStream).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private TimeSpan DetermineExpiration(CacheConfiguration config)
    {
        if (config.ExpirationMinutes > 0)
        {
            return TimeSpan.FromMinutes(config.ExpirationMinutes);
        }

        return TimeSpan.FromMinutes(options.DefaultExpirationMinutes);
    }

    // 컴파일타임 최적화: 캐시하면 안 되는 헤더들을 HashSet으로 미리 정의
    private static readonly HashSet<string> ExcludeHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "set-cookie", "authorization", "www-authenticate",
        "proxy-authenticate", "connection", "upgrade",
        "transfer-encoding", "content-encoding"
    };

    private static bool IsCacheableHeader(string headerName)
    {
        return !ExcludeHeaders.Contains(headerName);
    }

    /// <summary>
    /// Memory/Span 기반 제로 allocation 스트림 읽기
    /// </summary>
    private static async Task<string> ReadStreamEfficientlyAsync(Stream stream)
    {
        if (stream.Length == 0)
            return string.Empty;

        // 작은 스트림은 stackalloc, 큰 스트림은 ArrayPool 사용
        var streamLength = (int)stream.Length;
        
        if (streamLength <= 4096) // 4KB 이하는 스택 할당
        {
            var stackBuffer = new byte[streamLength];
            var totalRead = 0;
            
            while (totalRead < streamLength)
            {
                var bytesRead = await stream.ReadAsync(stackBuffer.AsMemory(totalRead)).ConfigureAwait(false);
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }
            
            return Encoding.UTF8.GetString(stackBuffer.AsSpan(0, totalRead));
        }
        else // 큰 데이터는 ArrayPool 사용
        {
            var buffer = ArrayPool<byte>.Shared.Rent(8192);
            var resultBuffer = ArrayPool<byte>.Shared.Rent(streamLength);
            
            try
            {
                var totalRead = 0;
                int bytesRead;
                
                while ((bytesRead = await stream.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
                {
                    buffer.AsSpan(0, bytesRead).CopyTo(resultBuffer.AsSpan(totalRead));
                    totalRead += bytesRead;
                }
                
                return Encoding.UTF8.GetString(resultBuffer.AsSpan(0, totalRead));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                ArrayPool<byte>.Shared.Return(resultBuffer);
            }
        }
    }

    /// <summary>
    /// 캐시 키에서 패턴 추출 (Span 기반 zero allocation)
    /// </summary>
    private static string ExtractKeyPattern(string cacheKey)
    {
        if (string.IsNullOrEmpty(cacheKey))
            return CachedConstants.Unknown;

        // ReadOnlySpan으로 문자열 분할 (allocation 없이)
        var span = cacheKey.AsSpan();
        var firstColon = span.IndexOf(':');
        
        if (firstColon == -1)
            return CachedConstants.Unknown;
            
        var secondColon = span.Slice(firstColon + 1).IndexOf(':');
        
        if (secondColon == -1)
            return CachedConstants.Unknown;
            
        // "Controller:Action" 부분만 추출
        var patternLength = firstColon + 1 + secondColon;
        return new string(span.Slice(0, patternLength));
    }
}