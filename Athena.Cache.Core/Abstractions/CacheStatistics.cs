namespace Athena.Cache.Core.Abstractions;

/// <summary>
/// 캐시 통계 정보
/// </summary>
public class CacheStatistics
{
    public long TotalKeys { get; set; }
    public long HitCount { get; set; }
    public long MissCount { get; set; }
    public double HitRatio => TotalRequests > 0 ? (double)HitCount / TotalRequests : 0;
    public long TotalRequests => HitCount + MissCount;
    public TimeSpan Uptime { get; set; }
    public long MemoryUsage { get; set; } // bytes
    public long ItemCount { get; set; }
    public DateTime LastCleanup { get; set; }
}