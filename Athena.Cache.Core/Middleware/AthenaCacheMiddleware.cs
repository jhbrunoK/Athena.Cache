using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Diagnostics;
using Athena.Cache.Core.Interfaces;
using Athena.Cache.Core.Models;
using Athena.Cache.Core.ObjectPools;
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

            // 캐시에서 응답 조회 (성능 모니터링 포함)
            using var cacheGetMeasurement = performanceMonitor.StartMeasurement("cache_get");
            var cachedResponse = await cache.GetAsync<CachedResponse>(cacheKey).ConfigureAwait(false);
            cacheGetMeasurement.Dispose();
            
            if (cachedResponse != null)
            {
                // 캐시 히트 - 바로 응답 반환
                performanceMonitor.RecordCacheHit();
                
                // 지능형 캐시 관리자에 접근 기록
                if (intelligentCacheManager != null)
                {
                    await intelligentCacheManager.RecordCacheAccessAsync(cacheKey, CacheAccessType.Hit).ConfigureAwait(false);
                }
                
                await WriteCachedResponseAsync(context, cachedResponse);
                return;
            }
            
            // 캐시 미스 기록
            performanceMonitor.RecordCacheMiss();
            
            // 지능형 캐시 관리자에 미스 기록
            if (intelligentCacheManager != null)
            {
                await intelligentCacheManager.RecordCacheAccessAsync(cacheKey, CacheAccessType.Miss).ConfigureAwait(false);
            }

            // 캐시 미스 - 응답 캐싱
            await CacheResponseAsync(context, cacheKey, cacheConfig);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in AthenaCacheMiddleware");

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
        return string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase);
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
        if (!controllerName.EndsWith("Controller"))
            controllerName += "Controller";

        // 주입된 레지스트리에서 설정 조회
        return configRegistry.GetConfiguration(controllerName, actionName);
    }

    private Task<string> GenerateCacheKeyAsync(HttpContext context, CacheConfiguration config)
    {
        try
        {
            // 쿼리 파라미터 수집
            var parameters = new Dictionary<string, object?>();

            foreach (var param in context.Request.Query)
            {
                // 제외 파라미터 체크
                if (config.ExcludeParameters.Contains(param.Key, StringComparer.OrdinalIgnoreCase))
                    continue;

                parameters[param.Key] = param.Value.ToString();
            }

            // 추가 파라미터 포함
            foreach (var additionalParam in config.AdditionalKeyParameters)
            {
                if (context.Items.TryGetValue(additionalParam, out var value))
                {
                    parameters[additionalParam] = value;
                }
            }

            // 캐시 키 생성
            var controllerName = config.CustomKeyPrefix ?? config.Controller;
            var cacheKey = keyGenerator.GenerateKey(controllerName, config.Action, parameters);

            if (options.Logging.LogKeyGeneration)
            {
                logger.LogDebug("Generated cache key: {CacheKey} for {Controller}.{Action}",
                    cacheKey, config.Controller, config.Action);
            }

            return Task.FromResult(cacheKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate cache key for {Controller}.{Action}",
                config.Controller, config.Action);
            return Task.FromResult(string.Empty);
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
        context.Response.Headers["X-Athena-Cache"] = "HIT";

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
                    cachedResponse.Initialize(
                        context.Response.StatusCode,
                        context.Response.ContentType ?? "application/json",
                        responseContent,
                        context.Response.Headers
                            .Where(h => IsCacheableHeader(h.Key))
                            .ToDictionary(h => h.Key, h => h.Value.ToString()),
                        expiresAt
                    );

                    // 캐시에 저장 (성능 모니터링 포함)
                    using var cacheSetMeasurement = performanceMonitor.StartMeasurement("cache_set");
                    await cache.SetAsync(cacheKey, cachedResponse, expiration).ConfigureAwait(false);
                    cacheSetMeasurement.Dispose();
                    
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

                // 테이블 추적 설정
                if (config.InvalidationRules.Any())
                {
                    var tablesToTrack = config.InvalidationRules
                        .Select(rule => rule.TableName)
                        .Distinct()
                        .ToArray();
                    
                    await invalidator.TrackCacheKeyAsync(tablesToTrack, cacheKey).ConfigureAwait(false);
                }

                if (options.Logging.LogCacheHitMiss)
                {
                    logger.LogInformation("Cached response for {Method} {Path} with key {CacheKey}",
                        context.Request.Method, context.Request.Path, cacheKey);
                }
            }

            // 캐시 미스 헤더 추가
            context.Response.Headers["X-Athena-Cache"] = "MISS";

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
    /// 메모리 효율적인 스트림 읽기 (ArrayPool 사용)
    /// </summary>
    private static async Task<string> ReadStreamEfficientlyAsync(Stream stream)
    {
        if (stream.Length == 0)
            return string.Empty;

        // ArrayPool을 사용해서 메모리 할당 최소화
        var bufferSize = (int)Math.Min(stream.Length, 8192); // 최대 8KB 버퍼
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        
        try
        {
            using var memoryStream = new MemoryStream((int)stream.Length);
            int bytesRead;
            
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bufferSize)).ConfigureAwait(false)) > 0)
            {
                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
            }
            
            // Span을 사용해서 효율적인 문자열 변환
            var bytes = memoryStream.ToArray();
            return Encoding.UTF8.GetString(bytes.AsSpan());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}