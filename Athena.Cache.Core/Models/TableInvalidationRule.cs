using Athena.Cache.Core.Enums;

namespace Athena.Cache.Core.Models;

/// <summary>
/// 테이블 무효화 규칙
/// </summary>
public class TableInvalidationRule
{
    public string TableName { get; set; } = string.Empty;
    public InvalidationType InvalidationType { get; set; }
    public string? Pattern { get; set; }
    public string[] RelatedTables { get; set; } = [];
    public int MaxDepth { get; set; } = -1;
}