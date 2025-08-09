namespace Athena.Cache.Analytics.Models;

/// <summary>
/// 시계열 캐시 메트릭
/// </summary>
public class CacheTimeSeriesData
{
    public DateTime Timestamp { get; set; }
    public double HitRatio { get; set; }
    public long RequestCount { get; set; }
    public double AverageResponseTime { get; set; }
    public long CacheSize { get; set; }
}