using Athena.Cache.Core.Abstractions;
using Athena.Cache.Monitoring.Enums;
using Athena.Cache.Monitoring.Interfaces;
using Athena.Cache.Monitoring.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Athena.Cache.Monitoring;

/// <summary>
/// 캐시 상태 확인 서비스
/// </summary>
public class CacheHealthChecker(
    IAthenaCache cache,
    ICacheMetricsCollector metricsCollector,
    ILogger<CacheHealthChecker> logger,
    IOptions<CacheMonitoringOptions> options)
    : ICacheHealthChecker
{
    private readonly CacheMonitoringOptions _options = options.Value;

    public async Task<CacheHealthStatus> CheckHealthAsync()
    {
        var healthStatus = new CacheHealthStatus();

        try
        {
            // 현재 메트릭 수집
            healthStatus.Metrics = await metricsCollector.CollectMetricsAsync();

            // 각 컴포넌트 상태 확인
            healthStatus.ComponentsHealth["Cache"] = await CheckCacheConnectionAsync();
            healthStatus.ComponentsHealth["Memory"] = await CheckMemoryUsageAsync();
            healthStatus.ComponentsHealth["Performance"] = await CheckPerformanceAsync();

            // 전체 상태 결정
            healthStatus.Status = DetermineOverallHealth(healthStatus);
            healthStatus.Message = GetHealthMessage(healthStatus.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed");
            healthStatus.Status = HealthStatus.Critical;
            healthStatus.Message = "Health check failed: " + ex.Message;
        }

        return healthStatus;
    }

    public async Task<HealthCheckResult> CheckComponentHealthAsync(string componentName)
    {
        return componentName.ToLower() switch
        {
            "cache" => await CheckCacheConnectionAsync(),
            "memory" => await CheckMemoryUsageAsync(),
            "performance" => await CheckPerformanceAsync(),
            _ => new HealthCheckResult
            {
                Status = HealthStatus.Warning,
                Message = "Unknown component"
            }
        };
    }

    private async Task<HealthCheckResult> CheckCacheConnectionAsync()
    {
        var startTime = DateTime.UtcNow;
        try
        {
            // 테스트 키로 연결 확인
            var testKey = $"health_check_{Guid.NewGuid()}";
            var testValue = "test";

            await cache.SetAsync(testKey, testValue, TimeSpan.FromSeconds(10));
            var retrieved = await cache.GetAsync<string>(testKey);
            await cache.RemoveAsync(testKey);

            var responseTime = DateTime.UtcNow - startTime;

            return new HealthCheckResult
            {
                Status = retrieved == testValue ? HealthStatus.Healthy : HealthStatus.Warning,
                Message = retrieved == testValue ? "Cache connection healthy" : "Cache data mismatch",
                ResponseTime = responseTime
            };
        }
        catch (Exception ex)
        {
            return new HealthCheckResult
            {
                Status = HealthStatus.Critical,
                Message = $"Cache connection failed: {ex.Message}",
                ResponseTime = DateTime.UtcNow - startTime
            };
        }
    }

    private async Task<HealthCheckResult> CheckMemoryUsageAsync()
    {
        var metrics = await metricsCollector.CollectMetricsAsync();
        var memoryUsage = metrics.MemoryUsageMB;
        var threshold = _options.Thresholds.MaxMemoryUsageMB;

        var status = memoryUsage switch
        {
            var usage when usage > threshold * 0.9 => HealthStatus.Critical,
            var usage when usage > threshold * 0.8 => HealthStatus.Warning,
            _ => HealthStatus.Healthy
        };

        return new HealthCheckResult
        {
            Status = status,
            Message = $"Memory usage: {memoryUsage}MB / {threshold}MB",
            Data = new Dictionary<string, object>
            {
                ["MemoryUsageMB"] = memoryUsage,
                ["ThresholdMB"] = threshold,
                ["UsagePercentage"] = (double)memoryUsage / threshold * 100
            }
        };
    }

    private async Task<HealthCheckResult> CheckPerformanceAsync()
    {
        var metrics = await metricsCollector.CollectMetricsAsync();
        var responseTime = metrics.AverageResponseTimeMs;
        var hitRatio = metrics.HitRatio;
        var threshold = _options.Thresholds.MaxResponseTimeMs;
        var minHitRatio = _options.Thresholds.MinHitRatio;

        var status = HealthStatus.Healthy;
        var messages = new List<string>();

        if (responseTime > threshold * 1.5)
        {
            status = HealthStatus.Critical;
            messages.Add($"Response time too high: {responseTime:F1}ms");
        }
        else if (responseTime > threshold)
        {
            status = HealthStatus.Warning;
            messages.Add($"Response time elevated: {responseTime:F1}ms");
        }

        if (hitRatio < minHitRatio * 0.7)
        {
            status = HealthStatus.Critical;
            messages.Add($"Hit ratio critically low: {hitRatio:P1}");
        }
        else if (hitRatio < minHitRatio)
        {
            if (status == HealthStatus.Healthy) status = HealthStatus.Warning;
            messages.Add($"Hit ratio below target: {hitRatio:P1}");
        }

        return new HealthCheckResult
        {
            Status = status,
            Message = messages.Any() ? string.Join(", ", messages) : "Performance healthy",
            Data = new Dictionary<string, object>
            {
                ["ResponseTimeMs"] = responseTime,
                ["HitRatio"] = hitRatio,
                ["ResponseTimeThreshold"] = threshold,
                ["HitRatioThreshold"] = minHitRatio
            }
        };
    }

    private static HealthStatus DetermineOverallHealth(CacheHealthStatus healthStatus)
    {
        var componentStatuses = healthStatus.ComponentsHealth.Values.Select(c => c.Status);

        if (componentStatuses.Any(s => s == HealthStatus.Critical))
            return HealthStatus.Critical;

        if (componentStatuses.Any(s => s == HealthStatus.Warning))
            return HealthStatus.Warning;

        return HealthStatus.Healthy;
    }

    private static string GetHealthMessage(HealthStatus status)
    {
        return status switch
        {
            HealthStatus.Healthy => "All systems operational",
            HealthStatus.Warning => "Some components need attention",
            HealthStatus.Critical => "Critical issues detected",
            HealthStatus.Offline => "System offline",
            _ => "Unknown status"
        };
    }
}