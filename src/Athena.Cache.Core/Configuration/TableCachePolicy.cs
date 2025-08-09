using Athena.Cache.Core.Enums;

namespace Athena.Cache.Core.Configuration;

/// <summary>
/// 테이블별 캐시 정책
/// </summary>
public class TableCachePolicy
{
    /// <summary>테이블명</summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>캐시 만료 시간 (분)</summary>
    public int ExpirationMinutes { get; set; } = 30;

    /// <summary>무효화 타입</summary>
    public InvalidationType InvalidationType { get; set; } = InvalidationType.All;

    /// <summary>패턴 (Pattern 타입일 때)</summary>
    public string? Pattern { get; set; }

    /// <summary>관련 테이블들 (Related 타입일 때)</summary>
    public string[] RelatedTables { get; set; } = [];

    /// <summary>연쇄 무효화 깊이</summary>
    public int MaxRelatedDepth { get; set; } = -1;
}