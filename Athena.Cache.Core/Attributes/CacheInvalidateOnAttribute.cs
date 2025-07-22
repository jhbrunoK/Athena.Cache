using Athena.Cache.Core.Enums;

namespace Athena.Cache.Core.Attributes;

/// <summary>
/// 테이블 변경 시 캐시 무효화 설정 Attribute
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class CacheInvalidateOnAttribute(string tableName, InvalidationType invalidationType = InvalidationType.All)
    : Attribute
{
    public string TableName { get; } = tableName;
    public InvalidationType InvalidationType { get; } = invalidationType;
    public string? Pattern { get; }
    public string[] RelatedTables { get; } = [];
    public int MaxDepth { get; set; } = -1; // -1은 글로벌 설정 사용

    public CacheInvalidateOnAttribute(string tableName, InvalidationType invalidationType, string pattern)
        : this(tableName, invalidationType)
    {
        Pattern = pattern;
    }

    public CacheInvalidateOnAttribute(string tableName, InvalidationType invalidationType, params string[] relatedTables)
        : this(tableName, invalidationType)
    {
        RelatedTables = relatedTables;
    }
}