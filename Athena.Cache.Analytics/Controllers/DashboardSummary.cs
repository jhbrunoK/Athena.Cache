namespace Athena.Cache.Analytics.Controllers;

/// <summary>
/// 대시보드 요약 정보
/// </summary>
public class DashboardSummary
{
    public long TotalRequests { get; set; }
    public double HitRatio { get; set; }
    public double AverageResponseTime { get; set; }
    public int ActiveKeys { get; set; }
    public List<HotKeySummary> TopHotKeys { get; set; } = new();
    public double OverallEfficiency { get; set; }
}

public class HotKeySummary
{
    public string Key { get; set; } = string.Empty;
    public long HitCount { get; set; }
    public double HitRatio { get; set; }
}