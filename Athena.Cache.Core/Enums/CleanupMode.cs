namespace Athena.Cache.Core.Enums;

/// <summary>
/// 서비스 시작 시 캐시 정리 방식
/// </summary>
public enum CleanupMode
{
    /// <summary>정리하지 않음</summary>
    None,
    /// <summary>만료시간 단축으로 자연 삭제</summary>
    ExpireShorten,
    /// <summary>패턴별 선택 삭제</summary>
    SelectiveDelete,
    /// <summary>전체 삭제</summary>
    FullDelete
}