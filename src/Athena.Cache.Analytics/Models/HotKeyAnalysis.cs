namespace Athena.Cache.Analytics.Models;

/// <summary>
/// 핫키 분석 결과
/// </summary>
public class HotKeyAnalysis
{
    public string CacheKey { get; set; } = string.Empty;
    public string EndpointPath { get; set; } = string.Empty;
    public long HitCount { get; set; }
    public long MissCount { get; set; }
    public double HitRatio { get; set; }
    public DateTime FirstAccess { get; set; }
    public DateTime LastAccess { get; set; }
    public double AverageResponseTime { get; set; }
    public int? AverageResponseSize { get; set; }
}