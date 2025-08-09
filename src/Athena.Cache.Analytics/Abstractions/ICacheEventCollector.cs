using Athena.Cache.Analytics.Models;

namespace Athena.Cache.Analytics.Abstractions;

/// <summary>
/// 캐시 이벤트 수집기
/// </summary>
public interface ICacheEventCollector
{
    /// <summary>
    /// 캐시 이벤트 기록
    /// </summary>
    Task RecordEventAsync(CacheEvent cacheEvent);

    /// <summary>
    /// 배치로 이벤트 기록
    /// </summary>
    Task RecordEventsAsync(IEnumerable<CacheEvent> events);

    /// <summary>
    /// 이벤트 플러시 (버퍼링된 이벤트 즉시 저장)
    /// </summary>
    Task FlushAsync();
}