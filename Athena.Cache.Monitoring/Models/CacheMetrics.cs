using Athena.Cache.Core.Abstractions;

namespace Athena.Cache.Monitoring.Models;

/// <summary>
/// 실시간 캐시 메트릭 - IExtendedCacheMetrics 구현
/// </summary>
public class CacheMetrics : IExtendedCacheMetrics
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double HitRatio { get; set; }
    public long TotalRequests { get; set; }
    public long HitCount { get; set; }
    public long MissCount { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public long MemoryUsageMB { get; set; }
    public int ConnectionCount { get; set; }
    public double ErrorRate { get; set; }
    private readonly Dictionary<string, object> _customMetrics = new();
    
    // ICacheMetrics 인터페이스 구현
    public long TotalHits => HitCount;
    public long TotalMisses => MissCount;
    public long MemoryUsageBytes => MemoryUsageMB * 1024 * 1024;
    public long ItemCount { get; set; }
    public long TotalErrors { get; set; }
    public long HotKeysCount { get; set; }
    public long TotalInvalidations { get; set; }
    
    // IExtendedCacheMetrics 인터페이스 구현
    public IReadOnlyDictionary<string, object> CustomMetrics => _customMetrics.AsReadOnly();
    
    // 하위 호환성을 위한 메서드
    public Dictionary<string, object> GetMutableCustomMetrics() => _customMetrics;
    public long DistributedInvalidationMessages { get; set; }
    public string CircuitBreakerState { get; set; } = "Closed";
}