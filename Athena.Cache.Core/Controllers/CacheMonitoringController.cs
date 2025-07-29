using Athena.Cache.Core.Analytics;
using Athena.Cache.Core.Observability;
using Athena.Cache.Core.Resilience;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Cache.Core.Controllers;

/// <summary>
/// 실시간 캐시 상태 모니터링 API
/// </summary>
[ApiController]
[Route("api/athena-cache/monitoring")]
[Authorize(Policy = "AthenaCacheMonitoring")] // 보안을 위한 인증 필요
public class CacheMonitoringController : ControllerBase
{
    private readonly CacheHealthMonitor _healthMonitor;
    private readonly CacheOptimizationAnalyzer _optimizationAnalyzer;
    private readonly CacheCircuitBreaker _circuitBreaker;
    private readonly ILogger<CacheMonitoringController> _logger;

    public CacheMonitoringController(
        CacheHealthMonitor healthMonitor,
        CacheOptimizationAnalyzer optimizationAnalyzer,
        CacheCircuitBreaker circuitBreaker,
        ILogger<CacheMonitoringController> logger)
    {
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _optimizationAnalyzer = optimizationAnalyzer ?? throw new ArgumentNullException(nameof(optimizationAnalyzer));
        _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 전체 캐시 헬스 상태 조회
    /// </summary>
    [HttpGet("health")]
    public async Task<ActionResult<OverallHealthStatus>> GetHealthStatus()
    {
        try
        {
            var healthStatus = await _healthMonitor.GetOverallHealthAsync();
            return Ok(healthStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cache health status");
            return StatusCode(500, new { error = "Failed to retrieve health status", details = ex.Message });
        }
    }

    /// <summary>
    /// 실시간 성능 메트릭 조회
    /// </summary>
    [HttpGet("metrics/current")]
    public ActionResult<CachePerformanceSnapshot> GetCurrentMetrics()
    {
        try
        {
            var snapshot = _healthMonitor.GetCurrentSnapshot();
            return Ok(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current cache metrics");
            return StatusCode(500, new { error = "Failed to retrieve current metrics", details = ex.Message });
        }
    }

    /// <summary>
    /// 성능 히스토리 조회
    /// </summary>
    [HttpGet("metrics/history")]
    public ActionResult<IEnumerable<CachePerformanceSnapshot>> GetMetricsHistory(
        [FromQuery] int maxItems = 60)
    {
        try
        {
            if (maxItems <= 0 || maxItems > 1440) // 최대 24시간
            {
                return BadRequest(new { error = "maxItems must be between 1 and 1440" });
            }

            var history = _healthMonitor.GetPerformanceHistory(maxItems);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cache metrics history");
            return StatusCode(500, new { error = "Failed to retrieve metrics history", details = ex.Message });
        }
    }

    /// <summary>
    /// 최적화 보고서 생성
    /// </summary>
    [HttpGet("optimization-report")]
    public async Task<ActionResult<OptimizationReport>> GetOptimizationReport()
    {
        try
        {
            var report = await _optimizationAnalyzer.GenerateOptimizationReportAsync();
            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate optimization report");
            return StatusCode(500, new { error = "Failed to generate optimization report", details = ex.Message });
        }
    }

    /// <summary>
    /// Circuit Breaker 상태 조회
    /// </summary>
    [HttpGet("circuit-breaker/status")]
    public ActionResult<CircuitBreakerStatistics> GetCircuitBreakerStatus()
    {
        try
        {
            var statistics = _circuitBreaker.GetStatistics();
            return Ok(statistics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get circuit breaker status");
            return StatusCode(500, new { error = "Failed to retrieve circuit breaker status", details = ex.Message });
        }
    }

    /// <summary>
    /// 실시간 대시보드 데이터 (WebSocket용)
    /// </summary>
    [HttpGet("dashboard/realtime")]
    public async Task<ActionResult<DashboardData>> GetRealtimeDashboardData()
    {
        try
        {
            var healthStatus = await _healthMonitor.GetOverallHealthAsync();
            var currentMetrics = _healthMonitor.GetCurrentSnapshot();
            var circuitBreakerStats = _circuitBreaker.GetStatistics();

            var dashboardData = new DashboardData
            {
                Timestamp = DateTime.UtcNow,
                HealthStatus = healthStatus,
                CurrentMetrics = currentMetrics,
                CircuitBreakerStatus = circuitBreakerStats,
                RecentHistory = _healthMonitor.GetPerformanceHistory(10).ToArray()
            };

            return Ok(dashboardData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get realtime dashboard data");
            return StatusCode(500, new { error = "Failed to retrieve dashboard data", details = ex.Message });
        }
    }

    /// <summary>
    /// 캐시 통계 요약
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<CacheSummary>> GetCacheSummary()
    {
        try
        {
            var healthStatus = await _healthMonitor.GetOverallHealthAsync();
            var currentMetrics = _healthMonitor.GetCurrentSnapshot();
            var history = _healthMonitor.GetPerformanceHistory(60).ToList();

            var summary = new CacheSummary
            {
                GeneratedAt = DateTime.UtcNow,
                OverallStatus = healthStatus.Status,
                CurrentHitRatio = currentMetrics.HitRatio,
                TotalHits = currentMetrics.TotalHits,
                TotalMisses = currentMetrics.TotalMisses,
                MemoryUsageMB = currentMetrics.MemoryUsageBytes / (1024.0 * 1024.0),
                ItemCount = currentMetrics.ItemCount,
                HotKeysCount = currentMetrics.HotKeysCount,
                TotalErrors = currentMetrics.TotalErrors,
                AverageHitRatioLast60Min = history.Any() ? history.Average(h => h.HitRatio) : 0,
                CircuitBreakerState = _circuitBreaker.State
            };

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cache summary");
            return StatusCode(500, new { error = "Failed to retrieve cache summary", details = ex.Message });
        }
    }
}

#region Response Models

/// <summary>
/// 실시간 대시보드 데이터
/// </summary>
public class DashboardData
{
    public DateTime Timestamp { get; init; }
    public OverallHealthStatus HealthStatus { get; init; } = new();
    public CachePerformanceSnapshot CurrentMetrics { get; init; } = new();
    public CircuitBreakerStatistics CircuitBreakerStatus { get; init; } = new();
    public CachePerformanceSnapshot[] RecentHistory { get; init; } = Array.Empty<CachePerformanceSnapshot>();
}

/// <summary>
/// 캐시 통계 요약
/// </summary>
public class CacheSummary
{
    public DateTime GeneratedAt { get; init; }
    public HealthStatus OverallStatus { get; init; }
    public double CurrentHitRatio { get; init; }
    public long TotalHits { get; init; }
    public long TotalMisses { get; init; }
    public double MemoryUsageMB { get; init; }
    public long ItemCount { get; init; }
    public long HotKeysCount { get; init; }
    public long TotalErrors { get; init; }
    public double AverageHitRatioLast60Min { get; init; }
    public CircuitBreakerState CircuitBreakerState { get; init; }
}

#endregion