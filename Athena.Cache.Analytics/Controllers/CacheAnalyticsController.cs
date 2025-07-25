using Athena.Cache.Analytics.Abstractions;
using Athena.Cache.Analytics.Models;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Cache.Analytics.Controllers;

/// <summary>
/// 캐시 분석 대시보드 API 컨트롤러
/// </summary>
[ApiController]
[Route("api/cache-analytics")]
[Produces("application/json")]
public class CacheAnalyticsController(
    ICacheAnalyticsService analyticsService,
    ICacheEventCollector eventCollector)
    : ControllerBase
{
    /// <summary>
    /// 캐시 통계 조회
    /// </summary>
    [HttpGet("statistics")]
    public async Task<ActionResult<CacheStatistics>> GetStatistics(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var start = startDate ?? DateTime.UtcNow.AddDays(-7);
        var end = endDate ?? DateTime.UtcNow;

        if (start >= end)
        {
            return BadRequest("시작 날짜는 종료 날짜보다 이전이어야 합니다.");
        }

        var stats = await analyticsService.GetStatisticsAsync(start, end);
        return Ok(stats);
    }

    /// <summary>
    /// 실시간 캐시 통계 (최근 1시간)
    /// </summary>
    [HttpGet("statistics/realtime")]
    public async Task<ActionResult<CacheStatistics>> GetRealtimeStatistics()
    {
        var end = DateTime.UtcNow;
        var start = end.AddHours(-1);

        var stats = await analyticsService.GetStatisticsAsync(start, end);
        return Ok(stats);
    }

    /// <summary>
    /// 시계열 데이터 조회
    /// </summary>
    [HttpGet("time-series")]
    public async Task<ActionResult<List<CacheTimeSeriesData>>> GetTimeSeries(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int intervalMinutes = 60)
    {
        var start = startDate ?? DateTime.UtcNow.AddDays(-1);
        var end = endDate ?? DateTime.UtcNow;
        var interval = TimeSpan.FromMinutes(Math.Max(1, intervalMinutes));

        var timeSeries = await analyticsService.GetTimeSeriesDataAsync(start, end, interval);
        return Ok(timeSeries);
    }

    /// <summary>
    /// 핫키 분석 조회
    /// </summary>
    [HttpGet("hot-keys")]
    public async Task<ActionResult<List<HotKeyAnalysis>>> GetHotKeys(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int count = 50)
    {
        var start = startDate ?? DateTime.UtcNow.AddDays(-7);
        var end = endDate ?? DateTime.UtcNow;
        var topCount = Math.Min(Math.Max(1, count), 1000); // 1-1000 범위로 제한

        var hotKeys = await analyticsService.GetHotKeysAsync(start, end, topCount);
        return Ok(hotKeys);
    }

    /// <summary>
    /// 콜드키 조회
    /// </summary>
    [HttpGet("cold-keys")]
    public async Task<ActionResult<List<string>>> GetColdKeys(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int inactiveHours = 24)
    {
        var start = startDate ?? DateTime.UtcNow.AddDays(-7);
        var end = endDate ?? DateTime.UtcNow;
        var inactiveThreshold = TimeSpan.FromHours(Math.Max(1, inactiveHours));

        var coldKeys = await analyticsService.GetColdKeysAsync(start, end, inactiveThreshold);
        return Ok(coldKeys);
    }

    /// <summary>
    /// 사용 패턴 분석
    /// </summary>
    [HttpGet("usage-patterns")]
    public async Task<ActionResult<UsagePatternAnalysis>> GetUsagePatterns(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var start = startDate ?? DateTime.UtcNow.AddDays(-7);
        var end = endDate ?? DateTime.UtcNow;

        var patterns = await analyticsService.AnalyzeUsagePatternsAsync(start, end);
        return Ok(patterns);
    }

    /// <summary>
    /// 캐시 효율성 분석
    /// </summary>
    [HttpGet("efficiency")]
    public async Task<ActionResult<Dictionary<string, double>>> GetCacheEfficiency(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var start = startDate ?? DateTime.UtcNow.AddDays(-7);
        var end = endDate ?? DateTime.UtcNow;

        var efficiency = await analyticsService.AnalyzeCacheEfficiencyAsync(start, end);
        return Ok(efficiency);
    }

    /// <summary>
    /// 이벤트 수동 플러시
    /// </summary>
    [HttpPost("flush")]
    public async Task<ActionResult> FlushEvents()
    {
        await eventCollector.FlushAsync();
        return Ok(new { message = "이벤트가 성공적으로 플러시되었습니다.", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// 대시보드 요약 정보
    /// </summary>
    [HttpGet("dashboard/summary")]
    public async Task<ActionResult<DashboardSummary>> GetDashboardSummary()
    {
        var end = DateTime.UtcNow;
        var start = end.AddDays(-1);

        var stats = await analyticsService.GetStatisticsAsync(start, end);
        var hotKeys = await analyticsService.GetHotKeysAsync(start, end, 5);
        var efficiency = await analyticsService.AnalyzeCacheEfficiencyAsync(start, end);

        var summary = new DashboardSummary
        {
            TotalRequests = stats.TotalRequests,
            HitRatio = stats.HitRatio,
            AverageResponseTime = stats.AverageResponseTimeMs,
            ActiveKeys = stats.ActiveKeys,
            TopHotKeys = hotKeys.Take(5).Select(h => new HotKeySummary
            {
                Key = h.CacheKey,
                HitCount = h.HitCount,
                HitRatio = h.HitRatio
            }).ToList(),
            OverallEfficiency = efficiency.GetValueOrDefault("Overall", 0)
        };

        return Ok(summary);
    }
}