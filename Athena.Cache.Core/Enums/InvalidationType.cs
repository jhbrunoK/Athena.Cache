namespace Athena.Cache.Core.Enums;

/// <summary>
/// 캐시 무효화 타입
/// </summary>
public enum InvalidationType
{
    /// <summary>연관된 모든 캐시 삭제</summary>
    All,
    /// <summary>패턴에 맞는 캐시만 삭제</summary>
    Pattern,
    /// <summary>관련 테이블들의 캐시도 함께 삭제</summary>
    Related
}