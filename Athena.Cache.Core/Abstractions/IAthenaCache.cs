namespace Athena.Cache.Core.Abstractions;

/// <summary>
/// 캐시 제공자 추상화 인터페이스
/// </summary>
public interface IAthenaCache
{
    /// <summary>
    /// 캐시에서 값 조회
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시에 값 저장
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시에서 특정 키 삭제
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 패턴에 맞는 캐시 키들 삭제 (와일드카드 지원)
    /// </summary>
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 키 존재 여부 확인
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 키를 배치로 조회
    /// </summary>
    Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 키-값 쌍을 배치로 저장
    /// </summary>
    Task SetManyAsync<T>(Dictionary<string, T> keyValuePairs, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 키를 배치로 삭제
    /// </summary>
    Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 통계 정보 조회
    /// </summary>
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}