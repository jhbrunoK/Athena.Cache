using Athena.Cache.Analytics.Abstractions;
using Athena.Cache.Analytics.Models;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Athena.Cache.Analytics.Middleware;

/// <summary>
/// 캐시 분석 미들웨어 - 자동으로 캐시 이벤트 수집
/// </summary>
public class CacheAnalyticsMiddleware(RequestDelegate next, ICacheEventCollector eventCollector)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        // 응답 크기 측정을 위한 래퍼
        var originalBody = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await next(context);

        stopwatch.Stop();

        // 응답을 원래 스트림에 복사
        responseBody.Seek(0, SeekOrigin.Begin);
        await responseBody.CopyToAsync(originalBody);

        // 캐시 관련 헤더가 있는 경우에만 이벤트 수집
        if (context.Response.Headers.ContainsKey("X-Cache-Status"))
        {
            var cacheStatus = context.Response.Headers["X-Cache-Status"].ToString();
            var cacheKey = context.Response.Headers["X-Cache-Key"].ToString();

            var cacheEvent = new CacheEvent
            {
                EventType = cacheStatus.Equals("HIT", StringComparison.OrdinalIgnoreCase)
                    ? CacheEventType.Hit
                    : CacheEventType.Miss,
                CacheKey = cacheKey,
                EndpointPath = context.Request.Path,
                HttpMethod = context.Request.Method,
                ResponseSize = (int)responseBody.Length,
                ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                UserId = context.User?.Identity?.Name,
                SessionId = context.Session?.Id,
                Metadata = new Dictionary<string, object>
                {
                    ["StatusCode"] = context.Response.StatusCode,
                    ["UserAgent"] = context.Request.Headers["User-Agent"].ToString()
                }
            };

            await eventCollector.RecordEventAsync(cacheEvent);
        }
    }
}