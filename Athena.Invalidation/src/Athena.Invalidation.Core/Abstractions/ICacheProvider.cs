namespace Athena.Invalidation.Core.Abstractions;

/// <summary>
/// 다양한 캐시 구현체를 추상화하는 인터페이스
/// Redis, MemoryCache, FusionCache 등 모든 캐시 라이브러리와 호환 가능
/// </summary>
public interface ICacheProvider
{
    /// <summary>
    /// 캐시 프로바이더 이름 (예: "Redis", "Memory", "FusionCache")
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 캐시 키가 존재하는지 확인
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 특정 캐시 키 제거
    /// </summary>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 캐시 키를 한번에 제거
    /// </summary>
    Task<int> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// 패턴에 맞는 캐시 키들 제거 (예: "user_*", "product_123_*")
    /// </summary>
    Task<int> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// 값을 캐시에 저장 (무효화 추적용)
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시에서 값 조회 (무효화 추적용)
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 통계 정보 조회
    /// </summary>
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 연결 상태 확인
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 캐시 통계 정보
/// </summary>
public class CacheStatistics
{
    public long HitCount { get; set; }
    public long MissCount { get; set; }
    public long TotalKeys { get; set; }
    public TimeSpan Uptime { get; set; }
    public double HitRatio => HitCount + MissCount > 0 ? (double)HitCount / (HitCount + MissCount) : 0;
}