namespace Athena.Invalidation.Tracking.Abstractions;

/// <summary>
/// 캐시 키 추적 시스템 인터페이스
/// </summary>
public interface ICacheKeyTracker
{
    /// <summary>추적된 키 총 개수</summary>
    int TrackedKeysCount { get; }
    
    /// <summary>추적된 테이블 개수</summary>
    int TrackedTablesCount { get; }
    
    /// <summary>캐시 키 추적 시작</summary>
    Task TrackKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default);
    
    /// <summary>여러 테이블에 대해 캐시 키 추적</summary>
    Task TrackKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default);
    
    /// <summary>캐시 키 추적 해제</summary>
    Task UntrackKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default);
    
    /// <summary>테이블의 모든 키 추적 해제</summary>
    Task UntrackAllKeysAsync(string tableName, CancellationToken cancellationToken = default);
    
    /// <summary>특정 테이블에 대한 추적된 키들 조회</summary>
    Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default);
    
    /// <summary>패턴과 일치하는 키들 조회</summary>
    Task<IEnumerable<string>> GetKeysByPatternAsync(string pattern, CancellationToken cancellationToken = default);
    
    /// <summary>키가 추적되고 있는 테이블들 조회</summary>
    Task<IEnumerable<string>> GetTablesForKeyAsync(string cacheKey, CancellationToken cancellationToken = default);
    
    /// <summary>추적 통계 조회</summary>
    Task<CacheKeyTrackingStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>만료된 키들 정리</summary>
    Task CleanupExpiredKeysAsync(CancellationToken cancellationToken = default);
    
    /// <summary>모든 추적 정보 삭제</summary>
    Task ClearAllTrackingAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 고급 캐시 키 추적 인터페이스
/// </summary>
public interface IAdvancedCacheKeyTracker : ICacheKeyTracker
{
    /// <summary>키 패턴 등록</summary>
    Task RegisterPatternAsync(string patternName, string pattern, CancellationToken cancellationToken = default);
    
    /// <summary>키 패턴 해제</summary>
    Task UnregisterPatternAsync(string patternName, CancellationToken cancellationToken = default);
    
    /// <summary>등록된 패턴들 조회</summary>
    Task<IEnumerable<CacheKeyPattern>> GetRegisteredPatternsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>키가 특정 패턴과 일치하는지 확인</summary>
    Task<bool> MatchesPatternAsync(string cacheKey, string patternName, CancellationToken cancellationToken = default);
    
    /// <summary>키에 태그 추가</summary>
    Task AddTagToKeyAsync(string cacheKey, string tag, CancellationToken cancellationToken = default);
    
    /// <summary>키에서 태그 제거</summary>
    Task RemoveTagFromKeyAsync(string cacheKey, string tag, CancellationToken cancellationToken = default);
    
    /// <summary>태그가 달린 키들 조회</summary>
    Task<IEnumerable<string>> GetKeysByTagAsync(string tag, CancellationToken cancellationToken = default);
    
    /// <summary>키의 태그들 조회</summary>
    Task<IEnumerable<string>> GetTagsForKeyAsync(string cacheKey, CancellationToken cancellationToken = default);
    
    /// <summary>키 히스토리 조회</summary>
    Task<IEnumerable<CacheKeyHistoryEntry>> GetKeyHistoryAsync(string cacheKey, CancellationToken cancellationToken = default);
    
    /// <summary>테이블 종속성 추가</summary>
    Task AddTableDependencyAsync(string tableName, string dependentTable, CancellationToken cancellationToken = default);
    
    /// <summary>테이블 종속성 제거</summary>
    Task RemoveTableDependencyAsync(string tableName, string dependentTable, CancellationToken cancellationToken = default);
    
    /// <summary>테이블 종속성 그래프 조회</summary>
    Task<TableDependencyGraph> GetTableDependencyGraphAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 캐시 키 패턴
/// </summary>
public class CacheKeyPattern
{
    public string Name { get; init; } = string.Empty;
    public string Pattern { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastUsedAt { get; set; }
    public long MatchCount { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 캐시 키 히스토리 항목
/// </summary>
public class CacheKeyHistoryEntry
{
    public string CacheKey { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty; // Track, Untrack, Access
    public string? TableName { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// 테이블 종속성 그래프
/// </summary>
public class TableDependencyGraph
{
    public Dictionary<string, HashSet<string>> Dependencies { get; init; } = new();
    public Dictionary<string, HashSet<string>> ReverseDependencies { get; init; } = new();
    
    public IEnumerable<string> GetDependentTables(string tableName)
    {
        return Dependencies.GetValueOrDefault(tableName, new HashSet<string>());
    }
    
    public IEnumerable<string> GetParentTables(string tableName)
    {
        return ReverseDependencies.GetValueOrDefault(tableName, new HashSet<string>());
    }
}

/// <summary>
/// 캐시 키 추적 통계
/// </summary>
public class CacheKeyTrackingStatistics
{
    public int TotalTrackedKeys { get; init; }
    public int TotalTrackedTables { get; init; }
    public int RegisteredPatterns { get; init; }
    public int ActivePatterns { get; init; }
    public long TotalTrackingOperations { get; init; }
    public long TotalUntrackingOperations { get; init; }
    public long TotalLookupOperations { get; init; }
    public double AverageKeysPerTable { get; init; }
    public double AverageTablesPerKey { get; init; }
    public DateTimeOffset LastCleanupTime { get; init; }
    public TimeSpan Uptime { get; init; }
    public Dictionary<string, object> AdditionalMetrics { get; init; } = new();
}