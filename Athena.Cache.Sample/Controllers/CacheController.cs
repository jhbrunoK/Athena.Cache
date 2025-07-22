using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Cache.Sample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CacheController(
    IAthenaCache cache,
    ICacheInvalidator invalidator,
    ILogger<CacheController> logger)
    : ControllerBase
{
    /// <summary>
    /// 캐시 통계 조회
    /// </summary>
    [HttpGet("statistics")]
    public async Task<ActionResult<CacheStatistics>> GetStatistics()
    {
        var stats = await cache.GetStatisticsAsync();
        return Ok(stats);
    }

    /// <summary>
    /// 특정 테이블의 캐시 수동 무효화
    /// </summary>
    [HttpDelete("invalidate/{tableName}")]
    public async Task<ActionResult> InvalidateTable(string tableName)
    {
        await invalidator.InvalidateAsync(tableName);
        logger.LogInformation("Manually invalidated cache for table: {TableName}", tableName);

        return Ok(new { message = $"Cache invalidated for table: {tableName}" });
    }

    /// <summary>
    /// 패턴으로 캐시 삭제
    /// </summary>
    [HttpDelete("invalidate-pattern")]
    public async Task<ActionResult> InvalidateByPattern([FromQuery] string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return BadRequest("Pattern is required");
        }

        await invalidator.InvalidateByPatternAsync(pattern);
        logger.LogInformation("Manually invalidated cache with pattern: {Pattern}", pattern);

        return Ok(new { message = $"Cache invalidated with pattern: {pattern}" });
    }
}