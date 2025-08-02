using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Cache.Sample.Controllers;

/// <summary>
/// Convention 기반 추론을 비활성화한 컨트롤러 예제
/// 캐싱은 사용하지만, 무효화는 수동으로 제어
/// </summary>
[ApiController]
[Route("api/[controller]")]
[NoConventionInvalidation]  // Convention 기반 추론 비활성화
public class ReportsController(ICacheInvalidator cacheInvalidator, ILogger<ReportsController> logger) : ControllerBase
{
    /// <summary>
    /// 보고서 데이터 조회 (캐싱만, 자동 무효화 없음)
    /// </summary>
    [HttpGet("monthly")]
    [AthenaCache(ExpirationMinutes = 120)]
    public async Task<ActionResult<object>> GetMonthlyReport([FromQuery] int year = 2024, [FromQuery] int month = 1)
    {
        logger.LogInformation("GetMonthlyReport called with year: {Year}, month: {Month}", year, month);

        // 실제로는 데이터베이스에서 복잡한 집계 쿼리 실행
        await Task.Delay(100); // 시뮬레이션
        
        var report = new
        {
            Year = year,
            Month = month,
            TotalUsers = 1500,
            TotalOrders = 3200,
            Revenue = 125000.50m,
            GeneratedAt = DateTime.UtcNow
        };

        return Ok(report);
    }

    /// <summary>
    /// 연간 보고서 조회 (명시적으로 특정 테이블만 무효화)
    /// </summary>
    [HttpGet("yearly")]
    [AthenaCache(ExpirationMinutes = 240)]
    [CacheInvalidateOn("UserStatistics")]  // Convention 없이 명시적으로만
    [CacheInvalidateOn("OrderStatistics")]
    public async Task<ActionResult<object>> GetYearlyReport([FromQuery] int year = 2024)
    {
        logger.LogInformation("GetYearlyReport called with year: {Year}", year);

        await Task.Delay(200); // 시뮬레이션
        
        var report = new
        {
            Year = year,
            TotalUsers = 18000,
            TotalOrders = 45000,
            Revenue = 1850000.75m,
            GeneratedAt = DateTime.UtcNow
        };

        return Ok(report);
    }

    /// <summary>
    /// 보고서 데이터 새로 고침 (수동 캐시 무효화)
    /// </summary>
    [HttpPost("refresh")]
    public async Task<ActionResult> RefreshReportData([FromQuery] string? reportType = null)
    {
        logger.LogInformation("RefreshReportData called with reportType: {ReportType}", reportType);

        // 수동으로 캐시 무효화
        switch (reportType?.ToLower())
        {
            case "monthly":
                await cacheInvalidator.InvalidateByPatternAsync("*GetMonthlyReport*");
                logger.LogInformation("Monthly report cache invalidated");
                break;
                
            case "yearly":
                await cacheInvalidator.InvalidateByPatternAsync("*GetYearlyReport*");
                logger.LogInformation("Yearly report cache invalidated");
                break;
                
            case null:
            case "all":
                await cacheInvalidator.InvalidateByPatternAsync("*Report*");
                logger.LogInformation("All report caches invalidated");
                break;
                
            default:
                return BadRequest($"Unknown report type: {reportType}");
        }

        return Ok(new { Message = "Report cache refreshed successfully", ReportType = reportType ?? "all" });
    }

    /// <summary>
    /// 캐시 없이 실시간 데이터 조회
    /// </summary>
    [HttpGet("realtime")]
    [NoCache]
    public async Task<ActionResult<object>> GetRealtimeData()
    {
        logger.LogInformation("GetRealtimeData called - 캐시 없이 실시간 데이터");

        await Task.Delay(50); // 시뮬레이션
        
        var data = new
        {
            CurrentActiveUsers = Random.Shared.Next(100, 500),
            PendingOrders = Random.Shared.Next(10, 50),
            ServerLoad = Random.Shared.NextDouble() * 100,
            Timestamp = DateTime.UtcNow
        };

        return Ok(data);
    }
}