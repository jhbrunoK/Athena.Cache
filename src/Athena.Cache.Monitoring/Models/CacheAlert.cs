using Athena.Cache.Monitoring.Enums;

namespace Athena.Cache.Monitoring.Models;

/// <summary>
/// 알림 이벤트
/// </summary>
public class CacheAlert
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public AlertLevel Level { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Component { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
}