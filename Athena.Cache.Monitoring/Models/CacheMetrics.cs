namespace Athena.Cache.Monitoring.Models;

/// <summary>
/// 실시간 캐시 메트릭
/// </summary>
public class CacheMetrics
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
    public Dictionary<string, object> CustomMetrics { get; set; } = new();
}