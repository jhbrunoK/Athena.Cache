using Athena.Cache.Core.Abstractions;

namespace Athena.Cache.Analytics.Models;

/// <summary>
/// 캐시 통계 집계 - ICacheMetrics 구현 (분석용)
/// </summary>
public class CacheStatistics : ICacheMetrics
{
    // 분석 기간
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    
    // ICacheMetrics 인터페이스 구현
    public DateTime Timestamp => PeriodEnd;
    public long TotalRequests { get; set; }
    public long TotalHits { get; set; }
    public long TotalMisses { get; set; }
    public double HitRatio { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public long MemoryUsageBytes => TotalCacheSize;
    public long ItemCount => ActiveKeys;
    public long TotalErrors { get; set; }
    public long HotKeysCount { get; set; }
    public long TotalInvalidations { get; set; }
    
    // Analytics 고유 속성
    public long TotalCacheSize { get; set; }
    public int ActiveKeys { get; set; }
    public Dictionary<string, long> EndpointHits { get; set; } = new();
    public Dictionary<string, long> TableInvalidations { get; set; } = new();
    
    // 분석 기간 정보
    public TimeSpan AnalysisPeriod => PeriodEnd - PeriodStart;
}