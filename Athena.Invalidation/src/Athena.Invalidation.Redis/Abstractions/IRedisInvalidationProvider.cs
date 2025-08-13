namespace Athena.Invalidation.Redis.Abstractions;

/// <summary>
/// Redis 캐시 무효화 제공자 추상화
/// </summary>
public interface IRedisInvalidationProvider
{
    /// <summary>Redis 연결 상태</summary>
    bool IsConnected { get; }
    
    /// <summary>Redis 데이터베이스 번호</summary>
    int Database { get; }
    
    /// <summary>캐시 제공자 이름</summary>
    string Name { get; }
    
    /// <summary>캐시 제공자 타입</summary>
    CacheProviderType Type { get; }
    
    /// <summary>특정 키를 무효화</summary>
    Task InvalidateAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>패턴과 일치하는 키들을 무효화</summary>
    Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default);
    
    /// <summary>여러 키를 한 번에 무효화</summary>
    Task InvalidateBatchAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
    
    /// <summary>Redis 키를 스캔하여 패턴과 일치하는 키들 조회</summary>
    Task<IEnumerable<string>> ScanKeysAsync(string pattern, int pageSize = 1000, CancellationToken cancellationToken = default);
    
    /// <summary>Redis에서 키 존재 여부 확인</summary>
    Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>키에 TTL 설정</summary>
    Task<bool> SetExpiryAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default);
    
    /// <summary>캐시 제공자 상태 확인</summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
    
    /// <summary>Redis 통계 조회</summary>
    Task<RedisInvalidationStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}



/// <summary>
/// Redis 무효화 통계
/// </summary>
public class RedisInvalidationStatistics
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
