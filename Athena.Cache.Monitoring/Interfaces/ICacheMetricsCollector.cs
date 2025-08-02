using Athena.Cache.Monitoring.Models;

namespace Athena.Cache.Monitoring.Interfaces;

/// <summary>
/// 캐시 메트릭 수집기
/// </summary>
public interface ICacheMetricsCollector
{
    /// <summary>
    /// 현재 메트릭 수집
    /// </summary>
    Task<CacheMetrics> CollectMetricsAsync();

    /// <summary>
    /// 시계열 메트릭 조회
    /// </summary>
    Task<List<CacheMetrics>> GetMetricsHistoryAsync(DateTime startTime, DateTime endTime);

    /// <summary>
    /// 메트릭 기록
    /// </summary>
    Task RecordMetricsAsync(CacheMetrics metrics);
}