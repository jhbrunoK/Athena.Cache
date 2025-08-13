using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Monitoring.Abstractions;

namespace Athena.Invalidation.Monitoring.HealthChecks;

/// <summary>
/// 무효화 엔진 헬스체크 구현
/// </summary>
public class InvalidationEngineHealthCheck(
    IInvalidationEngine invalidationEngine,
    IInvalidationMetricsCollector metricsCollector,
    ILogger<InvalidationEngineHealthCheck> logger,
    IOptions<HealthCheckOptions> options)
    : IInvalidationHealthChecker, IHealthCheck
{
    private readonly IInvalidationEngine _invalidationEngine = invalidationEngine ?? throw new ArgumentNullException(nameof(invalidationEngine));
    private readonly IInvalidationMetricsCollector _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
    private readonly ILogger<InvalidationEngineHealthCheck> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly HealthCheckOptions _options = options.Value ?? new HealthCheckOptions();

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var data = new Dictionary<string, object>();

            // 무효화 엔진 상태 확인
            var engineResult = await CheckInvalidationEngineAsync(cancellationToken);
            data.Add("invalidation_engine", engineResult.Data ?? new Dictionary<string, object>());

            // 메트릭 수집기 상태 확인
            var metricsResult = await CheckMetricsCollectorAsync(cancellationToken);
            data.Add("metrics_collector", metricsResult.Data ?? new Dictionary<string, object>());

            // 전체 상태 결정
            var overallStatus = DetermineOverallHealth(engineResult.Status, metricsResult.Status);
            var description = $"Invalidation system health: {overallStatus}";

            if (overallStatus == HealthStatus.Healthy)
            {
                return HealthCheckExtensions.Healthy(description, data);
            }
            else if (overallStatus == HealthStatus.Degraded)
            {
                return HealthCheckExtensions.Degraded(description, data: data);
            }
            else
            {
                return HealthCheckExtensions.Unhealthy(description, data: data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed with exception");
            return HealthCheckExtensions.Unhealthy("Health check failed", ex);
        }
    }

    public async Task<HealthCheckResult> CheckInvalidationEngineAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _invalidationEngine.GetStatusAsync(cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["is_healthy"] = status.IsHealthy,
                ["uptime"] = status.Uptime.TotalSeconds,
                ["tracked_keys"] = status.TrackedKeysCount,
                ["registered_rules"] = status.RegisteredRulesCount,
                ["last_activity"] = status.LastActivity.ToString("O")
            };

            // 메트릭 정보 추가
            foreach (var metric in status.Metrics)
            {
                data[metric.Key] = metric.Value;
            }

            if (status.IsHealthy)
            {
                return HealthCheckExtensions.Healthy("Invalidation engine is healthy", data);
            }
            else
            {
                var issues = status.Metrics.ContainsKey("Issues") ? status.Metrics["Issues"].ToString() : "Unknown issues";
                return HealthCheckExtensions.Unhealthy($"Invalidation engine is unhealthy: {issues}", data: data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check invalidation engine health");
            return HealthCheckExtensions.Unhealthy("Failed to check invalidation engine", ex);
        }
    }

    public Task<HealthCheckResult> CheckEventBusAsync(CancellationToken cancellationToken = default)
    {
        // 분산 이벤트 버스가 있는 경우 확인
        // 현재는 기본 구현으로 Healthy 반환
        var data = new Dictionary<string, object>
        {
            ["event_bus_available"] = false,
            ["message"] = "No distributed event bus configured"
        };

        return Task.FromResult(HealthCheckExtensions.Healthy("Event bus check skipped (not configured)", data));
    }

    public Task<HealthCheckResult> CheckCacheProvidersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 캐시 제공자 상태는 별도 구현 필요
            // 현재는 기본적으로 건강함으로 가정
            var data = new Dictionary<string, object>
            {
                ["providers_checked"] = 0,
                ["message"] = "Cache provider health checks not implemented"
            };

            return Task.FromResult(HealthCheckExtensions.Healthy("Cache providers assumed healthy", data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check cache providers");
            return Task.FromResult(HealthCheckExtensions.Unhealthy("Failed to check cache providers", ex));
        }
    }

    public Task<HealthCheckResult> CheckDatabaseConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 데이터베이스 연결 확인은 구체적인 구현에 따라 달라짐
            // 현재는 기본적으로 건강함으로 가정
            var data = new Dictionary<string, object>
            {
                ["database_available"] = true,
                ["message"] = "Database connection check not implemented"
            };

            return Task.FromResult(HealthCheckExtensions.Healthy("Database connection assumed healthy", data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check database connection");
            return Task.FromResult(HealthCheckExtensions.Unhealthy("Failed to check database connection", ex));
        }
    }

    private async Task<HealthCheckResult> CheckMetricsCollectorAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _metricsCollector.GetMetricsAsync(cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["total_invalidations"] = metrics.TotalInvalidations,
                ["success_rate"] = metrics.InvalidationSuccessRate,
                ["uptime"] = metrics.Uptime.TotalSeconds,
                ["cache_hit_ratio"] = metrics.CacheHitRatio
            };

            // 성공률이 임계값보다 낮으면 Degraded
            if (metrics.InvalidationSuccessRate < _options.MinSuccessRate)
            {
                return HealthCheckExtensions.Degraded(
                    $"Low invalidation success rate: {metrics.InvalidationSuccessRate:P2}", data: data);
            }

            return HealthCheckExtensions.Healthy("Metrics collector is healthy", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check metrics collector");
            return HealthCheckExtensions.Unhealthy("Failed to check metrics collector", ex);
        }
    }

    private static HealthStatus DetermineOverallHealth(params HealthStatus[] statuses)
    {
        if (statuses.Any(s => s == HealthStatus.Unhealthy))
            return HealthStatus.Unhealthy;
        if (statuses.Any(s => s == HealthStatus.Degraded))
            return HealthStatus.Degraded;
        return HealthStatus.Healthy;
    }
}

/// <summary>
/// 헬스체크 구성 옵션
/// </summary>
public class HealthCheckOptions
{
    /// <summary>최소 성공률 (0.0 ~ 1.0)</summary>
    public double MinSuccessRate { get; set; } = 0.95;
    
    /// <summary>최대 응답 시간 (밀리초)</summary>
    public int MaxResponseTimeMs { get; set; } = 5000;
    
    /// <summary>최대 오류 비율 (0.0 ~ 1.0)</summary>
    public double MaxErrorRate { get; set; } = 0.05;
    
    /// <summary>최소 캐시 히트 비율 (0.0 ~ 1.0)</summary>
    public double MinCacheHitRatio { get; set; } = 0.70;
}
