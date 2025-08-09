using System.ComponentModel.DataAnnotations;

namespace Athena.Cache.Analytics.Data;

/// <summary>
/// 캐시 이벤트 엔티티 (데이터베이스용)
/// </summary>
public class CacheEventEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int EventType { get; set; } // CacheEventType enum을 int로 저장
    public string CacheKey { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string? TableName { get; set; }
    public string EndpointPath { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public int? ResponseSize { get; set; }
    public double ProcessingTimeMs { get; set; }
    public string? UserId { get; set; }
    public string? SessionId { get; set; }
    public string? MetadataJson { get; set; } // JSON으로 직렬화된 메타데이터
}