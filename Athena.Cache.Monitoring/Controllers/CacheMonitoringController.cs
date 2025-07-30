using Athena.Cache.Core.Abstractions;
using Athena.Cache.Monitoring.Enums;
using Athena.Cache.Monitoring.Interfaces;
using Athena.Cache.Monitoring.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Athena.Cache.Monitoring.Controllers;

/// <summary>
/// 고급 캐시 모니터링 대시보드 API 컨트롤러
/// 실시간 대시보드, 알림, 분석 기능 제공
/// 기본 모니터링은 Athena.Cache.Core/Controllers/CacheMonitoringController 사용
/// </summary>
[ApiController]
[Route("api/athena-cache/monitoring/advanced")]
public class CacheMonitoringController(
    ICacheMetricsCollector metricsCollector,
    ICacheHealthChecker healthChecker,
    ICacheAlertService alertService,
    ILogger<CacheMonitoringController> logger)
    : ControllerBase
{
    /// <summary>
    /// 현재 확장 캐시 메트릭 조회 (고급 기능)
    /// </summary>
    [HttpGet("metrics/current")]
    public async Task<ActionResult<IExtendedCacheMetrics>> GetCurrentMetrics()
    {
        try
        {
            var metrics = await metricsCollector.CollectMetricsAsync();
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get current advanced metrics");
            return StatusCode(500, new { error = "Failed to retrieve advanced metrics", details = ex.Message });
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

            var history = await metricsCollector.GetMetricsHistoryAsync(start, end);

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
            logger.LogError(ex, "Failed to get metrics history");
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
            var health = await healthChecker.CheckHealthAsync();
            return Ok(health);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get health status");
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
            var result = await healthChecker.CheckComponentHealthAsync(component);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get component health for {Component}", component);
            return StatusCode(500, new { error = $"Failed to check {component} health" });
        }
    }

    /// <summary>
    /// 고급 대시보드 요약 정보 - 실시간 분석 포함
    /// </summary>
    [HttpGet("dashboard/summary")]
    public async Task<ActionResult<AdvancedDashboardSummary>> GetDashboardSummary()
    {
        try
        {
            var metrics = await metricsCollector.CollectMetricsAsync();
            var health = await healthChecker.CheckHealthAsync();
            var history = await metricsCollector.GetMetricsHistoryAsync(
                DateTime.UtcNow.AddMinutes(-60), DateTime.UtcNow);

            var summary = new AdvancedDashboardSummary
            {
                Timestamp = DateTime.UtcNow,
                Status = health.Status.ToString().ToLower(),
                CurrentMetrics = metrics,
                HealthDetails = health,
                PerformanceTrends = CalculatePerformanceTrends(history),
                Recommendations = GenerateRecommendations(metrics, history)
            };

            return Ok(summary);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get advanced dashboard summary");
            return StatusCode(500, new { error = "Failed to retrieve advanced dashboard summary", details = ex.Message });
        }
    }

    /// <summary>
    /// 실시간 메트릭 스트림 (WebSocket 대안)
    /// </summary>
    [HttpGet("metrics/stream")]
    public async Task<ActionResult<object>> GetMetricsStream(
        [FromQuery] int intervalSeconds = 5,
        [FromQuery] int durationMinutes = 1)
    {
        try
        {
            if (intervalSeconds < 1 || intervalSeconds > 60)
                return BadRequest(new { error = "intervalSeconds must be between 1 and 60" });
            
            if (durationMinutes < 1 || durationMinutes > 10)
                return BadRequest(new { error = "durationMinutes must be between 1 and 10" });

            var streamData = new List<IExtendedCacheMetrics>();
            var endTime = DateTime.UtcNow.AddMinutes(durationMinutes);
            
            while (DateTime.UtcNow < endTime)
            {
                var metrics = await metricsCollector.CollectMetricsAsync();
                streamData.Add(metrics);
                
                if (streamData.Count > 100) // 메모리 보호
                    streamData.RemoveAt(0);
                    
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
            }

            return Ok(new
            {
                collectionPeriod = new { intervalSeconds, durationMinutes },
                dataPoints = streamData.Count,
                metrics = streamData
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get metrics stream");
            return StatusCode(500, new { error = "Failed to retrieve metrics stream", details = ex.Message });
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

            await alertService.SendAlertAsync(alert);

            return Ok(new
            {
                message = "Test alert sent successfully",
                alertId = alert.Id,
                timestamp = alert.Timestamp
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send test alert");
            return StatusCode(500, new { error = "Failed to send test alert" });
        }
    }

    #region Advanced Analytics Helpers

    /// <summary>
    /// 성능 트렌드 계산
    /// </summary>
    private static PerformanceTrends CalculatePerformanceTrends(List<CacheMetrics> history)
    {
        if (!history.Any())
            return new PerformanceTrends();

        var recent = history.TakeLast(10).ToList();
        var older = history.Take(history.Count - 10).TakeLast(10).ToList();

        return new PerformanceTrends
        {
            HitRatioTrend = CalculateTrend(older.Average(m => m.HitRatio), recent.Average(m => m.HitRatio)),
            ResponseTimeTrend = CalculateTrend(older.Average(m => m.AverageResponseTimeMs), recent.Average(m => m.AverageResponseTimeMs)),
            MemoryUsageTrend = CalculateTrend(older.Average(m => m.MemoryUsageMB), recent.Average(m => m.MemoryUsageMB)),
            ErrorRateTrend = CalculateTrend(older.Average(m => m.ErrorRate), recent.Average(m => m.ErrorRate))
        };
    }

    /// <summary>
    /// 트렌드 방향 계산
    /// </summary>
    private static string CalculateTrend(double oldValue, double newValue)
    {
        if (Math.Abs(oldValue - newValue) < 0.01) return "stable";
        return newValue > oldValue ? "increasing" : "decreasing";
    }

    /// <summary>
    /// 성능 개선 권장사항 생성
    /// </summary>
    private static List<string> GenerateRecommendations(CacheMetrics current, List<CacheMetrics> history)
    {
        var recommendations = new List<string>();

        // 히트율 분석
        if (current.HitRatio < 0.7)
            recommendations.Add("캐시 히트율이 70% 미만입니다. 캐시 만료 시간을 늘리거나 키 전략을 검토하세요.");

        // 응답 시간 분석
        if (current.AverageResponseTimeMs > 100)
            recommendations.Add("평균 응답시간이 100ms를 초과합니다. 캐시 성능 최적화를 고려하세요.");

        // 메모리 사용량 분석
        if (current.MemoryUsageMB > 1024)
            recommendations.Add("메모리 사용량이 1GB를 초과합니다. 캐시 정리 정책을 검토하세요.");

        // 에러율 분석
        if (current.ErrorRate > 0.05)
            recommendations.Add("에러율이 5%를 초과합니다. 캐시 설정과 연결 상태를 확인하세요.");

        // 히스토리 기반 분석
        if (history.Count >= 10)
        {
            var recent = history.TakeLast(5).Average(m => m.HitRatio);
            var older = history.Take(5).Average(m => m.HitRatio);
            
            if (recent < older - 0.1)
                recommendations.Add("최근 캐시 히트율이 하락하고 있습니다. 캐시 무효화 패턴을 확인하세요.");
        }

        if (!recommendations.Any())
            recommendations.Add("현재 캐시 성능이 양호합니다.");

        return recommendations;
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
                    sampled.Add(CreateSampledMetric(currentBucket, bucketMetrics));
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
            sampled.Add(CreateSampledMetric(currentBucket, bucketMetrics));
        }

        return sampled;
    }

    /// <summary>
    /// 샘플링된 메트릭 생성
    /// </summary>
    private static CacheMetrics CreateSampledMetric(DateTime timestamp, List<CacheMetrics> bucketMetrics)
    {
        return new CacheMetrics
        {
            Timestamp = timestamp,
            HitRatio = bucketMetrics.Average(m => m.HitRatio),
            TotalRequests = bucketMetrics.Sum(m => m.TotalRequests),
            HitCount = bucketMetrics.Sum(m => m.HitCount),
            MissCount = bucketMetrics.Sum(m => m.MissCount),
            AverageResponseTimeMs = bucketMetrics.Average(m => m.AverageResponseTimeMs),
            MemoryUsageMB = (long)bucketMetrics.Average(m => m.MemoryUsageMB),
            ConnectionCount = (int)bucketMetrics.Average(m => m.ConnectionCount),
            ErrorRate = bucketMetrics.Average(m => m.ErrorRate),
            ItemCount = (long)bucketMetrics.Average(m => m.ItemCount),
            TotalErrors = bucketMetrics.Sum(m => m.TotalErrors),
            HotKeysCount = (long)bucketMetrics.Average(m => m.HotKeysCount),
            TotalInvalidations = bucketMetrics.Sum(m => m.TotalInvalidations)
        };
    }

    #endregion
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
/// 고급 대시보드 요약 정보
/// </summary>
public class AdvancedDashboardSummary
{
    public DateTime Timestamp { get; init; }
    public string Status { get; init; } = string.Empty;
    public IExtendedCacheMetrics CurrentMetrics { get; init; } = null!;
    public CacheHealthStatus HealthDetails { get; init; } = null!;
    public PerformanceTrends PerformanceTrends { get; init; } = new();
    public List<string> Recommendations { get; init; } = new();
}

/// <summary>
/// 성능 트렌드 정보
/// </summary>
public class PerformanceTrends
{
    public string HitRatioTrend { get; init; } = "stable";
    public string ResponseTimeTrend { get; init; } = "stable";
    public string MemoryUsageTrend { get; init; } = "stable";
    public string ErrorRateTrend { get; init; } = "stable";
}