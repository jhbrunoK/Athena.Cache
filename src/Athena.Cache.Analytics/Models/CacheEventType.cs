namespace Athena.Cache.Analytics.Models;

/// <summary>
/// 캐시 분석 이벤트 타입
/// </summary>
public enum CacheEventType
{
    Hit,        // 캐시 히트
    Miss,       // 캐시 미스
    Set,        // 캐시 저장
    Delete,     // 캐시 삭제
    Expire,     // 캐시 만료
    Invalidate  // 캐시 무효화
}