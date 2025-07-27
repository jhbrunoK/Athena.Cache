namespace Athena.Cache.Core.Models;

/// <summary>
/// 캐시 설정 정보
/// </summary>
public class CacheConfiguration
{
    public string Controller { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int ExpirationMinutes { get; set; } = -1;
    public int MaxRelatedDepth { get; set; } = -1;
    public string[] AdditionalKeyParameters { get; set; } = [];
    public string[] ExcludeParameters { get; set; } = [];
    public string? CustomKeyPrefix { get; set; }
    public List<TableInvalidationRule> InvalidationRules { get; set; } = [];
}