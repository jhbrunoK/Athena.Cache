using Athena.Cache.Core.Models;

namespace Athena.Cache.Core.Configuration;

/// <summary>
/// 개별 캐시 정책
/// </summary>
public class CachePolicy
{
    public string Controller { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public TimeSpan Expiration { get; set; }
    public bool Enabled { get; set; } = true;
    public List<TableInvalidationRule> InvalidationRules { get; set; } = new();
}