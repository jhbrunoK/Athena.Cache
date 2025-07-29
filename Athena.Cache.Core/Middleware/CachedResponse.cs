using MessagePack;

namespace Athena.Cache.Core.Middleware;

/// <summary>
/// 캐시된 HTTP 응답
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
}