namespace Athena.Invalidation.MemoryCache.Abstractions;

/// <summary>
/// Memory Cache 무효화 제공자 추상화
/// </summary>
public interface IMemoryCacheInvalidationProvider
{
    /// <summary>캐시 제공자 이름</summary>
    string Name { get; }
    
    /// <summary>캐시 제공자 타입</summary>
    CacheProviderType Type { get; }
    
    /// <summary>현재 캐시된 키 개수</summary>
    int Count { get; }
    
    /// <summary>특정 키를 무효화</summary>
    Task InvalidateAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>패턴과 일치하는 키들을 무효화</summary>
    Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default);
    
    /// <summary>태그와 연관된 키들을 무효화</summary>
    Task InvalidateByTagAsync(string tag, CancellationToken cancellationToken = default);
    
    /// <summary>여러 키를 한 번에 무효화</summary>
    Task InvalidateBatchAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
    
    /// <summary>모든 캐시 무효화</summary>
    Task InvalidateAllAsync(CancellationToken cancellationToken = default);
    
    /// <summary>패턴과 일치하는 키들을 조회</summary>
    Task<IEnumerable<string>> GetKeysByPatternAsync(string pattern, CancellationToken cancellationToken = default);
    
    /// <summary>키 존재 여부 확인</summary>
    Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>키에 태그 연결</summary>
    Task TagKeyAsync(string key, string tag, CancellationToken cancellationToken = default);
    
    /// <summary>키에서 태그 제거</summary>
    Task UntagKeyAsync(string key, string tag, CancellationToken cancellationToken = default);
    
    /// <summary>캐시 제공자 상태 확인</summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
    
    /// <summary>Memory Cache 통계 조회</summary>
    Task<MemoryCacheInvalidationStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}



/// <summary>
/// Memory Cache 무효화 통계
/// </summary>
public class MemoryCacheInvalidationStatistics
{
    public string ProviderName { get; init; } = string.Empty;
    public CacheProviderType Type { get; init; }
    public long TotalKeys { get; init; }
    public long TotalTags { get; init; }
    public long HitCount { get; init; }
    public long MissCount { get; init; }
    public double HitRatio { get; init; }
    public long TotalEvictions { get; init; }
    public DateTimeOffset LastAccess { get; init; }
    public Dictionary<string, object> AdditionalMetrics { get; init; } = new();
}
