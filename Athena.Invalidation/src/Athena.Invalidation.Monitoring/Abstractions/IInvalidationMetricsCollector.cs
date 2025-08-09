using Athena.Invalidation.Core.Abstractions;

namespace Athena.Invalidation.Monitoring.Abstractions;

/// <summary>
/// 무효화 메트릭 수집기 인터페이스
/// </summary>
public interface IInvalidationMetricsCollector
{
    /// <summary>메트릭 수집 시작</summary>
    Task StartAsync(CancellationToken cancellationToken = default);
    
    /// <summary>메트릭 수집 중지</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
    
    /// <summary>무효화 이벤트 기록</summary>
    void RecordInvalidationEvent(InvalidationType type, string target, TimeSpan duration, bool success = true);
    
    /// <summary>배치 무효화 이벤트 기록</summary>
    void RecordBatchInvalidationEvent(int count, TimeSpan duration, bool success = true);
    
    /// <summary>캐시 히트/미스 기록</summary>
    void RecordCacheAccess(string cacheName, bool hit);
    
    /// <summary>분산 이벤트 기록</summary>
    void RecordDistributedEvent(string eventType, string sourceNode, TimeSpan processingTime, bool success = true);
    
    /// <summary>현재 메트릭 스냅샷 반환</summary>
    Task<MetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>메트릭 리셋</summary>
    Task ResetMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 메트릭 스냅샷
/// </summary>
public class MetricsSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan Uptime { get; init; }
    
    // 무효화 메트릭
    public long TotalInvalidations { get; init; }
    public long SuccessfulInvalidations { get; init; }
    public long FailedInvalidations { get; init; }
    public double InvalidationSuccessRate { get; init; }
    public TimeSpan AverageInvalidationTime { get; init; }
    public Dictionary<InvalidationType, long> InvalidationsByType { get; init; } = new();
    
    // 캐시 메트릭
    public long TotalCacheAccesses { get; init; }
    public long CacheHits { get; init; }
    public long CacheMisses { get; init; }
    public double CacheHitRatio { get; init; }
    
    // 분산 메트릭
    public long TotalDistributedEvents { get; init; }
    public long SuccessfulDistributedEvents { get; init; }
    public long FailedDistributedEvents { get; init; }
    public TimeSpan AverageDistributedEventProcessingTime { get; init; }
    public Dictionary<string, long> DistributedEventsByType { get; init; } = new();
    public Dictionary<string, long> DistributedEventsByNode { get; init; } = new();
    
    // 시스템 메트릭
    public Dictionary<string, object> SystemMetrics { get; init; } = new();
}