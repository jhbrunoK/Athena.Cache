using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Invalidus.Core.Abstractions;

/// <summary>
/// 통합 캐시 프로바이더 - 캐싱과 무효화를 모두 처리하는 범용 인터페이스
/// Universal cache provider interface that handles both caching and invalidation operations
/// </summary>
public interface ICacheProvider
{
    #region Provider Information
    
    /// <summary>
    /// 캐시 프로바이더 이름 (예: "Redis", "Memory", "FusionCache")
    /// Cache provider name
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 캐시 프로바이더 타입
    /// Cache provider type
    /// </summary>
    CacheProviderType ProviderType { get; }

    /// <summary>
    /// 프로바이더가 현재 사용 가능한지 여부
    /// Whether the provider is currently available
    /// </summary>
    bool IsAvailable { get; }

    #endregion

    #region Cache Operations (Get/Set)

    /// <summary>
    /// 캐시에서 값 조회
    /// Get value from cache
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시에 값 저장
    /// Set value in cache
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 키가 존재하는지 확인
    /// Check if cache key exists
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 키를 배치로 조회
    /// Get multiple keys in batch
    /// </summary>
    Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 키-값 쌍을 배치로 저장
    /// Set multiple key-value pairs in batch
    /// </summary>
    Task SetManyAsync<T>(Dictionary<string, T> keyValuePairs, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    #endregion

    #region Invalidation Operations (Remove)

    /// <summary>
    /// 특정 캐시 키 제거
    /// Remove specific cache key
    /// </summary>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 캐시 키를 한번에 제거
    /// Remove multiple cache keys at once
    /// </summary>
    Task<int> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// 패턴에 맞는 캐시 키들 제거 (예: "user_*", "product_123_*")
    /// Remove cache keys matching a pattern
    /// </summary>
    Task<int> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// 패턴에 맞는 키들을 스캔하여 조회 (제거 전 확인용)
    /// Scan and retrieve keys matching a pattern
    /// </summary>
    Task<IEnumerable<string>> ScanKeysAsync(string pattern, int? limit = null, CancellationToken cancellationToken = default);

    #endregion

    #region Advanced Operations

    /// <summary>
    /// 키의 TTL(Time To Live) 설정
    /// Set TTL (Time To Live) for a key
    /// </summary>
    Task<bool> SetExpiryAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default);

    /// <summary>
    /// 키의 남은 TTL 조회
    /// Get remaining TTL for a key
    /// </summary>
    Task<TimeSpan?> GetTtlAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 원자적 연산: 값이 없으면 설정, 있으면 기존 값 반환
    /// Atomic operation: set if not exists, return existing value if exists
    /// </summary>
    Task<T?> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    #endregion

    #region Statistics and Health

    /// <summary>
    /// 캐시 통계 정보 조회
    /// Get cache statistics
    /// </summary>
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 연결 상태 확인
    /// Check cache connection health
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 상세 헬스체크 정보
    /// Detailed health check information
    /// </summary>
    Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> GetHealthCheckAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Events

    /// <summary>
    /// 캐시 키가 제거될 때 발생하는 이벤트
    /// Event raised when a cache key is removed
    /// </summary>
    event EventHandler<CacheKeyRemovedEventArgs>? KeyRemoved;

    /// <summary>
    /// 캐시 키가 만료될 때 발생하는 이벤트
    /// Event raised when a cache key expires
    /// </summary>
    event EventHandler<CacheKeyExpiredEventArgs>? KeyExpired;

    #endregion
}

/// <summary>
/// 캐시 프로바이더 타입
/// Cache provider types
/// </summary>
public enum CacheProviderType
{
    /// <summary>메모리 캐시 (L1 Cache)</summary>
    Memory,
    
    /// <summary>분산 캐시 (L2 Cache) - Redis, etc.</summary>
    Distributed,
    
    /// <summary>하이브리드 캐시 (L1 + L2)</summary>
    Hybrid,
    
    /// <summary>커스텀 캐시 구현</summary>
    Custom
}

/// <summary>
/// 캐시 통계 정보
/// Cache statistics information
/// </summary>
public class CacheStatistics
{
    public string ProviderName { get; init; } = string.Empty;
    public CacheProviderType Type { get; init; }
    public long TotalKeys { get; init; }
    public long DatabaseSize { get; init; }
    public long ExpiredKeys { get; init; }
    public long EvictedKeys { get; init; }
    public double HitRatio { get; init; }
    public TimeSpan Uptime { get; init; }
    public DateTimeOffset LastAccess { get; init; }
    public Dictionary<string, object> AdditionalMetrics { get; init; } = new();
}


/// <summary>
/// 캐시 키 제거 이벤트 인자
/// Cache key removed event arguments
/// </summary>
public class CacheKeyRemovedEventArgs : EventArgs
{
    public string Key { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 캐시 키 만료 이벤트 인자
/// Cache key expired event arguments
/// </summary>
public class CacheKeyExpiredEventArgs : EventArgs
{
    public string Key { get; init; } = string.Empty;
    public DateTimeOffset ExpiredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}