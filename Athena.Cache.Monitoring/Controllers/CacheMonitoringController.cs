using Athena.Cache.Monitoring.Enums;
using Athena.Cache.Monitoring.Interfaces;
using Athena.Cache.Monitoring.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Athena.Cache.Monitoring.Controllers;

/// <summary>
/// 캐시 모니터링 대시보드 API 컨트롤러
/// </summary>
[ApiController]
[Route("api/cache/monitoring")]
public class CacheMonitoringController : ControllerBase
{
    private readonly ICacheMetricsCollector _metricsCollector;
    private readonly ICacheHealthChecker _healthChecker;
    private readonly ICacheAlertService _alertService;
    private readonly ILogger<CacheMonitoringController> _logger;

    public CacheMonitoringController(
        ICacheMetricsCollector metricsCollector,
        ICacheHealthChecker healthChecker,
        ICacheAlertService alertService,
        ILogger<CacheMonitoringController> logger)
    {
        _metricsCollector = metricsCollector;
        _healthChecker = healthChecker;
        _alertService = alertService;
        _logger = logger;
    }

    /// <summary>
    /// 현재 캐시 메트릭 조회
    /// </summary>
    [HttpGet("metrics/current")]
    public async Task<ActionResult<CacheMetrics>> GetCurrentMetrics()
    {
        try
        {
            var metrics = await _metricsCollector.CollectMetricsAsync();
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current metrics");
            return StatusCode(500, new { error = "Failed to retrieve metrics" });
        }
    }

    /// <summary>
    /// 시계열 메트릭 데이터 조회
    /// </summary>
    [HttpGet("metrics/history")]
    public async Task<ActionResult<List<CacheMetrics>>> GetMetricsHistory(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] int? intervalMinutes = null)
    {
        try
        {
            var start = startTime ?? DateTime.UtcNow.AddHours(-1);
            var end = endTime ?? DateTime.UtcNow;

            var history = await _metricsCollector.GetMetricsHistoryAsync(start, end);

            // 간격이 지정된 경우 데이터 샘플링
            if (intervalMinutes.HasValue && intervalMinutes.Value > 0)
            {
                var interval = TimeSpan.FromMinutes(intervalMinutes.Value);
                history = SampleMetrics(history, interval);
            }

            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metrics history");
            return StatusCode(500, new { error = "Failed to retrieve metrics history" });
        }
    }

    /// <summary>
    /// 캐시 상태 확인
    /// </summary>
    [HttpGet("health")]
    public async Task<ActionResult<CacheHealthStatus>> GetHealthStatus()
    {
        try
        {
            var health = await _healthChecker.CheckHealthAsync();
            return Ok(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get health status");
            return StatusCode(500, new { error = "Failed to retrieve health status" });
        }
    }

    /// <summary>
    /// 개별 컴포넌트 상태 확인
    /// </summary>
    [HttpGet("health/{component}")]
    public async Task<ActionResult<HealthCheckResult>> GetComponentHealth(string component)
    {
        try
        {
            var result = await _healthChecker.CheckComponentHealthAsync(component);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get component health for {Component}", component);
            return StatusCode(500, new { error = $"Failed to check {component} health" });
        }
    }

    /// <summary>
    /// 대시보드 요약 정보
    /// </summary>
    [HttpGet("dashboard/summary")]
    public async Task<ActionResult<object>> GetDashboardSummary()
    {
        try
        {
            var metrics = await _metricsCollector.CollectMetricsAsync();
            var health = await _healthChecker.CheckHealthAsync();

            var summary = new
            {
                timestamp = DateTime.UtcNow,
                status = health.Status.ToString().ToLower(),
                metrics = new
                {
                    hitRatio = metrics.HitRatio,
                    totalRequests = metrics.TotalRequests,
                    averageResponseTime = metrics.AverageResponseTimeMs,
                    memoryUsage = metrics.MemoryUsageMB,
                    connectionCount = metrics.ConnectionCount,
                    errorRate = metrics.ErrorRate
                },
                components = health.ComponentsHealth.ToDictionary(
                    kvp => kvp.Key.ToLower(),
                    kvp => new
                    {
                        status = kvp.Value.Status.ToString().ToLower(),
                        message = kvp.Value.Message,
                        responseTime = kvp.Value.ResponseTime.TotalMilliseconds
                    })
            };

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get dashboard summary");
            return StatusCode(500, new { error = "Failed to retrieve dashboard summary" });
        }
    }

    /// <summary>
    /// 테스트 알림 발송
    /// </summary>
    [HttpPost("alerts/test")]
    public async Task<ActionResult> SendTestAlert([FromBody] TestAlertRequest? request = null)
    {
        try
        {
            var alert = new CacheAlert
            {
                Level = request?.Level ?? AlertLevel.Info,
                Title = request?.Title ?? "Test Alert",
                Message = request?.Message ?? "This is a test alert from the monitoring system",
                Component = request?.Component ?? "Monitoring"
            };

            await _alertService.SendAlertAsync(alert);

            return Ok(new
            {
                message = "Test alert sent successfully",
                alertId = alert.Id,
                timestamp = alert.Timestamp
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send test alert");
            return StatusCode(500, new { error = "Failed to send test alert" });
        }
    }

    /// <summary>
    /// 테스트 알림 요청 모델
    /// </summary>
    public class TestAlertRequest
    {
        public AlertLevel Level { get; set; } = AlertLevel.Info;
        public string Title { get; set; } = "Test Alert";
        public string Message { get; set; } = "Test message";
        public string Component { get; set; } = "Monitoring";
    }

    /// <summary>
    /// 메트릭 데이터 샘플링 (대용량 데이터 처리용)
    /// </summary>
    private static List<CacheMetrics> SampleMetrics(List<CacheMetrics> metrics, TimeSpan interval)
    {
        if (!metrics.Any()) return metrics;

        var sampled = new List<CacheMetrics>();
        var currentBucket = metrics.First().Timestamp;
        var bucketMetrics = new List<CacheMetrics>();

        foreach (var metric in metrics)
        {
            if (metric.Timestamp - currentBucket >= interval)
            {
                // 현재 버킷의 평균 계산
                if (bucketMetrics.Any())
                {
                    sampled.Add(new CacheMetrics
                    {
                        Timestamp = currentBucket,
                        HitRatio = bucketMetrics.Average(m => m.HitRatio),
                        TotalRequests = bucketMetrics.Sum(m => m.TotalRequests),
                        HitCount = bucketMetrics.Sum(m => m.HitCount),
                        MissCount = bucketMetrics.Sum(m => m.MissCount),
                        AverageResponseTimeMs = bucketMetrics.Average(m => m.AverageResponseTimeMs),
                        MemoryUsageMB = (long)bucketMetrics.Average(m => m.MemoryUsageMB),
                        ConnectionCount = (int)bucketMetrics.Average(m => m.ConnectionCount),
                        ErrorRate = bucketMetrics.Average(m => m.ErrorRate)
                    });
                }

                // 새 버킷 시작
                currentBucket = metric.Timestamp;
                bucketMetrics.Clear();
            }

            bucketMetrics.Add(metric);
        }

        // 마지막 버킷 처리
        if (bucketMetrics.Any())
        {
            sampled.Add(new CacheMetrics
            {
                Timestamp = currentBucket,
                HitRatio = bucketMetrics.Average(m => m.HitRatio),
                TotalRequests = bucketMetrics.Sum(m => m.TotalRequests),
                HitCount = bucketMetrics.Sum(m => m.HitCount),
                MissCount = bucketMetrics.Sum(m => m.MissCount),
                AverageResponseTimeMs = bucketMetrics.Average(m => m.AverageResponseTimeMs),
                MemoryUsageMB = (long)bucketMetrics.Average(m => m.MemoryUsageMB),
                ConnectionCount = (int)bucketMetrics.Average(m => m.ConnectionCount),
                ErrorRate = bucketMetrics.Average(m => m.ErrorRate)
            });
        }

        return sampled;
    }
}