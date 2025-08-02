using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Cache.Core.Controllers;

/// <summary>
/// 기본 캐시 모니터링 API - 핵심 기능만 제공
/// 고급 대시보드 기능은 Athena.Cache.Monitoring 라이브러리 사용
/// </summary>
[ApiController]
[Route("api/athena-cache/monitoring")]
[Authorize(Policy = "AthenaCacheMonitoring")] // 보안을 위한 인증 필요
public class CacheMonitoringController(
    CacheHealthMonitor healthMonitor,
    ILogger<CacheMonitoringController> logger)
    : ControllerBase
{
    private readonly CacheHealthMonitor _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
    private readonly ILogger<CacheMonitoringController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// 전체 캐시 헬스 상태 조회 (기본 기능)
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
    /// 현재 성능 메트릭 조회 (기본 기능)
    /// </summary>
    [HttpGet("metrics/current")]
    public ActionResult<ICacheMetrics> GetCurrentMetrics()
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
    /// 기본 상태 확인 - 간단한 Alive 체크
    /// </summary>
    [HttpGet("status")]
    public ActionResult<object> GetStatus()
    {
        try
        {
            var currentMetrics = _healthMonitor.GetCurrentSnapshot();
            
            return Ok(new
            {
                timestamp = DateTime.UtcNow,
                status = "healthy",
                hitRatio = currentMetrics.HitRatio,
                memoryUsageMB = Math.Round(currentMetrics.MemoryUsageBytes / (1024.0 * 1024.0), 2),
                itemCount = currentMetrics.ItemCount,
                message = "Athena Cache is running"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cache status");
            return StatusCode(500, new { 
                timestamp = DateTime.UtcNow,
                status = "error", 
                message = "Cache status check failed",
                error = ex.Message 
            });
        }
    }
}