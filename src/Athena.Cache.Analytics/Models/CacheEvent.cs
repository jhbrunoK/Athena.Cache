namespace Athena.Cache.Analytics.Models;

/// <summary>
/// 개별 캐시 이벤트
/// </summary>
public class CacheEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public CacheEventType EventType { get; set; }
    public string CacheKey { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string? TableName { get; set; }
    public string EndpointPath { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public int? ResponseSize { get; set; }
    public double ProcessingTimeMs { get; set; }
    public string? UserId { get; set; }
    public string? SessionId { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}