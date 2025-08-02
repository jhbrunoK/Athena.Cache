namespace Athena.Cache.Core.Abstractions;

/// <summary>
/// 캐시 무효화 관리자 인터페이스
/// </summary>
public interface ICacheInvalidator
{
    /// <summary>
    /// 테이블 변경 시 연관된 캐시 무효화
    /// </summary>
    Task InvalidateAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 패턴에 맞는 캐시들 무효화
    /// </summary>
    Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 키를 테이블과 연결하여 추적
    /// </summary>
    Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 테이블과 연결하여 추적
    /// </summary>
    Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 테이블과 연결된 모든 캐시 키 조회
    /// </summary>
    Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 관련 테이블들과 함께 연쇄 무효화
    /// </summary>
    Task InvalidateWithRelatedAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 테이블을 배치로 무효화 (성능 최적화)
    /// </summary>
    Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 패턴을 배치로 무효화 (성능 최적화)
    /// </summary>
    Task InvalidateByPatternBatchAsync(IEnumerable<string> patterns, CancellationToken cancellationToken = default);
}