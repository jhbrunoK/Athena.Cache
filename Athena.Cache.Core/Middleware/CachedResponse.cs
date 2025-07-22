namespace Athena.Cache.Core.Middleware;

/// <summary>
/// 캐시된 HTTP 응답
/// </summary>
public class CachedResponse
{
    public int StatusCode { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
}