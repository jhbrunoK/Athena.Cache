namespace Athena.Cache.Analytics.Models;

/// <summary>
/// 캐시 통계 집계
/// </summary>
public class CacheStatistics
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public long TotalRequests { get; set; }
    public long TotalHits { get; set; }
    public long TotalMisses { get; set; }
    public double HitRatio { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public long TotalCacheSize { get; set; }
    public int ActiveKeys { get; set; }
    public Dictionary<string, long> EndpointHits { get; set; } = new();
    public Dictionary<string, long> TableInvalidations { get; set; } = new();
}