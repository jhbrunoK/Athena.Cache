namespace Athena.Cache.Core.Abstractions;

/// <summary>
/// 지능형 캐시 관리 기능을 제공하는 인터페이스
/// Hot Key Detection, Adaptive TTL, Cache Warming 등 고급 캐싱 패턴 지원
/// </summary>
public interface IIntelligentCacheManager
{
    /// <summary>
    /// Hot Key 감지 시작
    /// </summary>
    Task StartHotKeyDetectionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Hot Key 감지 중지
    /// </summary>
    Task StopHotKeyDetectionAsync();
    
    /// <summary>
    /// 현재 Hot Key 목록 조회
    /// </summary>
    Task<IEnumerable<HotKeyInfo>> GetHotKeysAsync(int topCount = 10, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 키의 사용 패턴을 기반으로 최적 TTL 계산
    /// </summary>
    Task<TimeSpan> CalculateAdaptiveTtlAsync(string cacheKey, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 캐시 워밍 실행 (미리 데이터 로드)
    /// </summary>
    Task WarmCacheAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 캐시 사용 통계 기록
    /// </summary>
    Task RecordCacheAccessAsync(string cacheKey, CacheAccessType accessType, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// LRU/LFU 정책에 따른 캐시 정리
    /// </summary>
    Task EvictCacheByPolicyAsync(CacheEvictionPolicy policy, int maxItems, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 키의 우선순위 계산 (사용 빈도, 최근 사용 등 고려)
    /// </summary>
    Task<double> CalculateKeyPriorityAsync(string cacheKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Hot Key 정보
/// </summary>
public class HotKeyInfo
{
    public required string Key { get; init; }
    public long AccessCount { get; init; }
    public double AccessRate { get; init; } // 접근/시간 비율
    public DateTime FirstAccess { get; init; }
    public DateTime LastAccess { get; init; }
    public TimeSpan AverageInterval { get; init; }
    public double Priority { get; init; }
}

/// <summary>
/// 캐시 접근 타입
/// </summary>
public enum CacheAccessType
{
    /// <summary>캐시 히트</summary>
    Hit,
    
    /// <summary>캐시 미스</summary>
    Miss,
    
    /// <summary>캐시 설정</summary>
    Set,
    
    /// <summary>캐시 삭제</summary>
    Delete,
    
    /// <summary>캐시 만료</summary>
    Expire
}

/// <summary>
/// 캐시 교체 정책
/// </summary>
public enum CacheEvictionPolicy
{
    /// <summary>Least Recently Used - 가장 오래 사용되지 않은 항목 제거</summary>
    LRU,
    
    /// <summary>Least Frequently Used - 가장 적게 사용된 항목 제거</summary>
    LFU,
    
    /// <summary>Time To Live - 만료 시간 기준 제거</summary>
    TTL,
    
    /// <summary>Random - 무작위 제거</summary>
    Random,
    
    /// <summary>FIFO - First In First Out</summary>
    FIFO
}