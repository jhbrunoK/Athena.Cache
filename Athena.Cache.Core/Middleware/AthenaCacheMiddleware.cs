using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
    AthenaCacheOptions options,
    ILogger<AthenaCacheMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // GET 요청만 캐싱 (POST, PUT, DELETE 등은 제외)
        if (!IsGetRequest(context))
        {
            await next(context);
            return;
        }

        // 캐시 비활성화 체크
        if (IsCacheDisabled(context))
        {
            await next(context);
            return;
        }

        try
        {
            // 캐시 설정 가져오기
            var cacheConfig = GetCacheConfiguration(context);
            if (cacheConfig == null || !cacheConfig.Enabled)
            {
                await next(context);
                return;
            }

            // 캐시 키 생성
            var cacheKey = await GenerateCacheKeyAsync(context, cacheConfig);
            if (string.IsNullOrEmpty(cacheKey))
            {
                await next(context);
                return;
            }

            // HttpContext에 생성된 키 저장 (Action Filter에서 사용)
            context.Items["AthenaCache.GeneratedKey"] = cacheKey;

            // 캐시에서 응답 조회
            var cachedResponse = await cache.GetAsync<CachedResponse>(cacheKey);
            if (cachedResponse != null)
            {
                // 캐시 히트 - 바로 응답 반환
                await WriteCachedResponseAsync(context, cachedResponse);
                return;
            }

            // 캐시 미스 - 응답 캐싱
            await CacheResponseAsync(context, cacheKey, cacheConfig);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in AthenaCacheMiddleware");

            if (options.ErrorHandling.SilentFallback)
            {
                await next(context);

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

    private static CacheConfiguration? GetCacheConfiguration(HttpContext context)
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

        // Source Generator가 생성한 레지스트리에서 설정 조회
        return Athena.Cache.Core.Generated.CacheConfigurationRegistry.GetConfiguration(controllerName, actionName);
    }

    private async Task<string> GenerateCacheKeyAsync(HttpContext context, CacheConfiguration config)
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

            return cacheKey;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate cache key for {Controller}.{Action}",
                config.Controller, config.Action);
            return string.Empty;
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
            await next(context);

            // 성공 응답만 캐싱 (200-299)
            if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
            {
                // 응답 내용 읽기
                responseBodyStream.Seek(0, SeekOrigin.Begin);
                var responseContent = await new StreamReader(responseBodyStream).ReadToEndAsync();

                // 캐시에 저장할 응답 객체 생성
                var cachedResponse = new CachedResponse
                {
                    StatusCode = context.Response.StatusCode,
                    ContentType = context.Response.ContentType ?? "application/json",
                    Content = responseContent,
                    Headers = context.Response.Headers
                        .Where(h => IsCacheableHeader(h.Key))
                        .ToDictionary(h => h.Key, h => h.Value.ToString())
                };

                // 만료 시간 결정
                var expiration = DetermineExpiration(config);

                // 캐시에 저장
                await cache.SetAsync(cacheKey, cachedResponse, expiration);

                // 테이블 추적 설정
                if (config.InvalidationRules.Any())
                {
                    var tablesToTrack = config.InvalidationRules
                        .Select(rule => rule.TableName)
                        .Distinct()
                        .ToArray();
                    
                    await invalidator.TrackCacheKeyAsync(tablesToTrack, cacheKey);
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
            await responseBodyStream.CopyToAsync(originalBodyStream);
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

    private static bool IsCacheableHeader(string headerName)
    {
        // 캐시하면 안 되는 헤더들 제외
        var excludeHeaders = new[]
        {
            "set-cookie", "authorization", "www-authenticate",
            "proxy-authenticate", "connection", "upgrade"
        };

        return !excludeHeaders.Contains(headerName.ToLower());
    }
}