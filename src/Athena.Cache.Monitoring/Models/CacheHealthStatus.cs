using Athena.Cache.Monitoring.Enums;

namespace Athena.Cache.Monitoring.Models;

/// <summary>
/// 캐시 상태 정보
/// </summary>
public class CacheHealthStatus
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public HealthStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, HealthCheckResult> ComponentsHealth { get; set; } = new();
    public CacheMetrics Metrics { get; set; } = new();
}