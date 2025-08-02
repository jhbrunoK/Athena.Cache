using MessagePack;

namespace Athena.Cache.Core.Middleware;

/// <summary>
/// 캐시된 HTTP 응답 (Object Pooling 지원)
/// </summary>
[MessagePackObject]
public class CachedResponse
{
    [Key(0)]
    public int StatusCode { get; set; }
    
    [Key(1)]
    public string ContentType { get; set; } = string.Empty;
    
    [Key(2)]
    public string Content { get; set; } = string.Empty;
    
    [Key(3)]
    public Dictionary<string, string> Headers { get; set; } = new();
    
    [Key(4)]
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
    
    [Key(5)]
    public DateTime ExpiresAt { get; set; }
    
    /// <summary>
    /// 객체 풀링을 위한 초기화 메서드
    /// </summary>
    public void Reset()
    {
        StatusCode = 0;
        ContentType = string.Empty;
        Content = string.Empty;
        Headers?.Clear();
        CachedAt = default;
        ExpiresAt = default;
    }
    
    /// <summary>
    /// 객체 풀링을 위한 설정 메서드
    /// </summary>
    public void Initialize(int statusCode, string contentType, string content, 
                          Dictionary<string, string> headers, DateTime expiresAt)
    {
        StatusCode = statusCode;
        ContentType = contentType;
        Content = content;
        Headers = headers ?? new();
        CachedAt = DateTime.UtcNow;
        ExpiresAt = expiresAt;
    }
}