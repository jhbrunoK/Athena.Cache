#pragma warning disable CS1998 // 비동기 메서드에 await 연산자가 없으며 동기적으로 실행됨

using Athena.Invalidation.Tracking.Abstractions;

namespace Athena.Invalidation.Tracking.Services;

/// <summary>
/// 고급 캐시 키 추적 시스템 구현
/// </summary>
public class AdvancedCacheKeyTracker : IAdvancedCacheKeyTracker, IAsyncDisposable
{
    private readonly ILogger<AdvancedCacheKeyTracker> _logger;
    private readonly CacheKeyTrackingOptions _options;
    
    // 키-테이블 매핑
    private readonly ConcurrentDictionary<string, HashSet<string>> _keyToTables = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _tableToKeys = new();
    
    // 패턴 관리
    private readonly ConcurrentDictionary<string, CacheKeyPattern> _patterns = new();
    private readonly ConcurrentDictionary<string, Regex> _compiledPatterns = new();
    
    // 태그 관리
    private readonly ConcurrentDictionary<string, HashSet<string>> _keyToTags = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _tagToKeys = new();
    
    // 테이블 종속성
    private readonly ConcurrentDictionary<string, HashSet<string>> _tableDependencies = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _reverseTableDependencies = new();
    
    // 히스토리 및 통계
    private readonly ConcurrentQueue<CacheKeyHistoryEntry> _history = new();
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastCleanupTime = DateTimeOffset.UtcNow;
    
    // 통계 카운터
    private long _totalTrackingOperations = 0;
    private long _totalUntrackingOperations = 0;
    private long _totalLookupOperations = 0;
    
    // 동시성 제어
    private readonly ReaderWriterLockSlim _dataLock = new();
    private readonly Timer? _cleanupTimer;
    
    private volatile bool _disposed = false;

    public int TrackedKeysCount => _keyToTables.Count;
    public int TrackedTablesCount => _tableToKeys.Count;

    public AdvancedCacheKeyTracker(
        ILogger<AdvancedCacheKeyTracker> logger,
        IOptions<CacheKeyTrackingOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new CacheKeyTrackingOptions();
        
        // 정리 작업 타이머 설정
        if (_options.EnablePeriodicCleanup)
        {
            _cleanupTimer = new Timer(
                async _ => await CleanupExpiredKeysAsync(),
                null,
                _options.CleanupInterval,
                _options.CleanupInterval);
        }
        
        _logger.LogDebug("AdvancedCacheKeyTracker initialized with cleanup interval: {Interval}", 
            _options.CleanupInterval);
    }

    public async Task TrackKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        _dataLock.EnterWriteLock();
        try
        {
            // 키-테이블 매핑 추가
            _keyToTables.AddOrUpdate(cacheKey,
                [tableName],
                (_, existing) => 
                {
                    existing.Add(tableName);
                    return existing;
                });
            
            // 테이블-키 매핑 추가
            _tableToKeys.AddOrUpdate(tableName,
                [cacheKey],
                (_, existing) =>
                {
                    existing.Add(cacheKey);
                    return existing;
                });
            
            Interlocked.Increment(ref _totalTrackingOperations);
            
            // 히스토리 기록
            if (_options.EnableHistory)
            {
                AddHistoryEntry(cacheKey, "Track", tableName);
            }
            
            _logger.LogTrace("Tracked cache key {CacheKey} for table {TableName}", cacheKey, tableName);
        }
        finally
        {
            _dataLock.ExitWriteLock();
        }
    }

    public async Task TrackKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        _dataLock.EnterWriteLock();
        try
        {
            // 키-테이블 매핑 추가
            _keyToTables.AddOrUpdate(cacheKey,
                [..tableNames],
                (_, existing) =>
                {
                    foreach (var tableName in tableNames)
                    {
                        existing.Add(tableName);
                    }
                    return existing;
                });
            
            // 각 테이블에 키 매핑 추가
            foreach (var tableName in tableNames)
            {
                _tableToKeys.AddOrUpdate(tableName,
                    [cacheKey],
                    (_, existing) =>
                    {
                        existing.Add(cacheKey);
                        return existing;
                    });
            }
            
            Interlocked.Increment(ref _totalTrackingOperations);
            
            // 히스토리 기록
            if (_options.EnableHistory)
            {
                AddHistoryEntry(cacheKey, "TrackMultiple", null, 
                    new Dictionary<string, object> { ["tables"] = tableNames });
            }
            
            _logger.LogTrace("Tracked cache key {CacheKey} for {TableCount} tables", cacheKey, tableNames.Length);
        }
        finally
        {
            _dataLock.ExitWriteLock();
        }
    }

    public async Task UntrackKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        _dataLock.EnterWriteLock();
        try
        {
            // 키-테이블 매핑에서 제거
            if (_keyToTables.TryGetValue(cacheKey, out var tables))
            {
                tables.Remove(tableName);
                if (!tables.Any())
                {
                    _keyToTables.TryRemove(cacheKey, out _);
                }
            }
            
            // 테이블-키 매핑에서 제거
            if (_tableToKeys.TryGetValue(tableName, out var keys))
            {
                keys.Remove(cacheKey);
                if (!keys.Any())
                {
                    _tableToKeys.TryRemove(tableName, out _);
                }
            }
            
            Interlocked.Increment(ref _totalUntrackingOperations);
            
            // 히스토리 기록
            if (_options.EnableHistory)
            {
                AddHistoryEntry(cacheKey, "Untrack", tableName);
            }
            
            _logger.LogTrace("Untracked cache key {CacheKey} from table {TableName}", cacheKey, tableName);
        }
        finally
        {
            _dataLock.ExitWriteLock();
        }
    }

    public async Task UntrackAllKeysAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        _dataLock.EnterWriteLock();
        try
        {
            if (_tableToKeys.TryRemove(tableName, out var keys))
            {
                // 각 키에서 이 테이블 매핑 제거
                foreach (var cacheKey in keys)
                {
                    if (_keyToTables.TryGetValue(cacheKey, out var tables))
                    {
                        tables.Remove(tableName);
                        if (!tables.Any())
                        {
                            _keyToTables.TryRemove(cacheKey, out _);
                        }
                    }
                }
                
                Interlocked.Add(ref _totalUntrackingOperations, keys.Count);
                
                // 히스토리 기록
                if (_options.EnableHistory)
                {
                    AddHistoryEntry("*", "UntrackAll", tableName,
                        new Dictionary<string, object> { ["keys_count"] = keys.Count });
                }
                
                _logger.LogDebug("Untracked all {KeyCount} keys from table {TableName}", keys.Count, tableName);
            }
        }
        finally
        {
            _dataLock.ExitWriteLock();
        }
    }

    public async Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        Interlocked.Increment(ref _totalLookupOperations);
        
        _dataLock.EnterReadLock();
        try
        {
            return _tableToKeys.TryGetValue(tableName, out var keys) 
                ? keys.ToList() 
                : Enumerable.Empty<string>();
        }
        finally
        {
            _dataLock.ExitReadLock();
        }
    }

    public async Task<IEnumerable<string>> GetKeysByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        Interlocked.Increment(ref _totalLookupOperations);
        
        var regex = CreatePatternRegex(pattern);
        var matchingKeys = new List<string>();
        
        _dataLock.EnterReadLock();
        try
        {
            foreach (var cacheKey in _keyToTables.Keys)
            {
                if (regex.IsMatch(cacheKey))
                {
                    matchingKeys.Add(cacheKey);
                }
            }
        }
        finally
        {
            _dataLock.ExitReadLock();
        }
        
        return matchingKeys;
    }

    public async Task<IEnumerable<string>> GetTablesForKeyAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        Interlocked.Increment(ref _totalLookupOperations);
        
        _dataLock.EnterReadLock();
        try
        {
            return _keyToTables.TryGetValue(cacheKey, out var tables) 
                ? tables.ToList() 
                : Enumerable.Empty<string>();
        }
        finally
        {
            _dataLock.ExitReadLock();
        }
    }

    public async Task<CacheKeyTrackingStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        _dataLock.EnterReadLock();
        try
        {
            var totalKeys = _keyToTables.Count;
            var totalTables = _tableToKeys.Count;
            
            var avgKeysPerTable = totalTables > 0 
                ? _tableToKeys.Values.Average(keys => keys.Count) 
                : 0.0;
                
            var avgTablesPerKey = totalKeys > 0
                ? _keyToTables.Values.Average(tables => tables.Count)
                : 0.0;
            
            return new CacheKeyTrackingStatistics
            {
                TotalTrackedKeys = totalKeys,
                TotalTrackedTables = totalTables,
                RegisteredPatterns = _patterns.Count,
                ActivePatterns = _patterns.Values.Count(p => p.IsActive),
                TotalTrackingOperations = _totalTrackingOperations,
                TotalUntrackingOperations = _totalUntrackingOperations,
                TotalLookupOperations = _totalLookupOperations,
                AverageKeysPerTable = avgKeysPerTable,
                AverageTablesPerKey = avgTablesPerKey,
                LastCleanupTime = _lastCleanupTime,
                Uptime = DateTimeOffset.UtcNow - _startTime,
                AdditionalMetrics = new Dictionary<string, object>
                {
                    ["history_entries"] = _history.Count,
                    ["compiled_patterns"] = _compiledPatterns.Count,
                    ["tag_mappings"] = _keyToTags.Count,
                    ["table_dependencies"] = _tableDependencies.Count,
                    ["cleanup_enabled"] = _options.EnablePeriodicCleanup
                }
            };
        }
        finally
        {
            _dataLock.ExitReadLock();
        }
    }

    public async Task CleanupExpiredKeysAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        
        var cleanupStartTime = DateTimeOffset.UtcNow;
        var removedKeysCount = 0;
        var removedHistoryCount = 0;
        
        _dataLock.EnterWriteLock();
        try
        {
            // 히스토리 정리 (오래된 항목 제거)
            if (_options.EnableHistory && _options.MaxHistoryEntries > 0)
            {
                var currentHistoryCount = _history.Count;
                var toRemove = currentHistoryCount - _options.MaxHistoryEntries;
                
                for (int i = 0; i < toRemove; i++)
                {
                    if (_history.TryDequeue(out _))
                    {
                        removedHistoryCount++;
                    }
                }
            }
            
            // 사용되지 않는 패턴 정리
            var unusedPatterns = _patterns.Values
                .Where(p => p.LastUsedAt < DateTimeOffset.UtcNow - _options.PatternCleanupAge)
                .ToList();
                
            foreach (var pattern in unusedPatterns)
            {
                _patterns.TryRemove(pattern.Name, out _);
                _compiledPatterns.TryRemove(pattern.Name, out _);
            }
            
            _lastCleanupTime = cleanupStartTime;
            
            if (removedKeysCount > 0 || removedHistoryCount > 0 || unusedPatterns.Any())
            {
                _logger.LogDebug("Cleanup completed: removed {RemovedKeys} keys, {RemovedHistory} history entries, {RemovedPatterns} unused patterns",
                    removedKeysCount, removedHistoryCount, unusedPatterns.Count);
            }
        }
        finally
        {
            _dataLock.ExitWriteLock();
        }
    }

    public async Task ClearAllTrackingAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        _dataLock.EnterWriteLock();
        try
        {
            var totalKeys = _keyToTables.Count;
            var totalTables = _tableToKeys.Count;
            
            _keyToTables.Clear();
            _tableToKeys.Clear();
            _keyToTags.Clear();
            _tagToKeys.Clear();
            _tableDependencies.Clear();
            _reverseTableDependencies.Clear();
            
            // 히스토리도 클리어
            while (_history.TryDequeue(out _)) { }
            
            // 통계 리셋
            Interlocked.Exchange(ref _totalTrackingOperations, 0);
            Interlocked.Exchange(ref _totalUntrackingOperations, 0);
            Interlocked.Exchange(ref _totalLookupOperations, 0);
            
            _logger.LogWarning("Cleared all tracking data: {TotalKeys} keys, {TotalTables} tables", 
                totalKeys, totalTables);
        }
        finally
        {
            _dataLock.ExitWriteLock();
        }
    }

    // Advanced tracking methods

    public async Task RegisterPatternAsync(string patternName, string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        var cachePattern = new CacheKeyPattern
        {
            Name = patternName,
            Pattern = pattern,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUsedAt = DateTimeOffset.UtcNow,
            MatchCount = 0,
            IsActive = true
        };
        
        _patterns[patternName] = cachePattern;
        _compiledPatterns[patternName] = CreatePatternRegex(pattern);
        
        _logger.LogDebug("Registered cache key pattern: {PatternName} = {Pattern}", patternName, pattern);
    }

    public async Task UnregisterPatternAsync(string patternName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        _patterns.TryRemove(patternName, out _);
        _compiledPatterns.TryRemove(patternName, out _);
        
        _logger.LogDebug("Unregistered cache key pattern: {PatternName}", patternName);
    }

    public async Task<IEnumerable<CacheKeyPattern>> GetRegisteredPatternsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        return _patterns.Values.ToList();
    }

    public async Task<bool> MatchesPatternAsync(string cacheKey, string patternName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        if (_compiledPatterns.TryGetValue(patternName, out var regex))
        {
            var matches = regex.IsMatch(cacheKey);
            
            if (matches && _patterns.TryGetValue(patternName, out var pattern))
            {
                pattern.LastUsedAt = DateTimeOffset.UtcNow;
                pattern.MatchCount++;
            }
            
            return matches;
        }
        
        return false;
    }

    public async Task AddTagToKeyAsync(string cacheKey, string tag, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        _dataLock.EnterWriteLock();
        try
        {
            _keyToTags.AddOrUpdate(cacheKey,
                [tag],
                (_, existing) =>
                {
                    existing.Add(tag);
                    return existing;
                });
            
            _tagToKeys.AddOrUpdate(tag,
                [cacheKey],
                (_, existing) =>
                {
                    existing.Add(cacheKey);
                    return existing;
                });
            
            _logger.LogTrace("Added tag {Tag} to cache key {CacheKey}", tag, cacheKey);
        }
        finally
        {
            _dataLock.ExitWriteLock();
        }
    }

    public async Task RemoveTagFromKeyAsync(string cacheKey, string tag, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        _dataLock.EnterWriteLock();
        try
        {
            if (_keyToTags.TryGetValue(cacheKey, out var tags))
            {
                tags.Remove(tag);
                if (!tags.Any())
                {
                    _keyToTags.TryRemove(cacheKey, out _);
                }
            }
            
            if (_tagToKeys.TryGetValue(tag, out var keys))
            {
                keys.Remove(cacheKey);
                if (!keys.Any())
                {
                    _tagToKeys.TryRemove(tag, out _);
                }
            }
            
            _logger.LogTrace("Removed tag {Tag} from cache key {CacheKey}", tag, cacheKey);
        }
        finally
        {
            _dataLock.ExitWriteLock();
        }
    }

    public async Task<IEnumerable<string>> GetKeysByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        Interlocked.Increment(ref _totalLookupOperations);
        
        _dataLock.EnterReadLock();
        try
        {
            return _tagToKeys.TryGetValue(tag, out var keys) 
                ? keys.ToList() 
                : Enumerable.Empty<string>();
        }
        finally
        {
            _dataLock.ExitReadLock();
        }
    }

    public async Task<IEnumerable<string>> GetTagsForKeyAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        Interlocked.Increment(ref _totalLookupOperations);
        
        _dataLock.EnterReadLock();
        try
        {
            return _keyToTags.TryGetValue(cacheKey, out var tags) 
                ? tags.ToList() 
                : Enumerable.Empty<string>();
        }
        finally
        {
            _dataLock.ExitReadLock();
        }
    }

    public async Task<IEnumerable<CacheKeyHistoryEntry>> GetKeyHistoryAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        if (!_options.EnableHistory) return [];
        
        return _history.Where(entry => entry.CacheKey == cacheKey || entry.CacheKey == "*").ToList();
    }

    public async Task AddTableDependencyAsync(string tableName, string dependentTable, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        _dataLock.EnterWriteLock();
        try
        {
            _tableDependencies.AddOrUpdate(tableName,
                [dependentTable],
                (_, existing) =>
                {
                    existing.Add(dependentTable);
                    return existing;
                });
            
            _reverseTableDependencies.AddOrUpdate(dependentTable,
                [tableName],
                (_, existing) =>
                {
                    existing.Add(tableName);
                    return existing;
                });
            
            _logger.LogDebug("Added table dependency: {TableName} -> {DependentTable}", tableName, dependentTable);
        }
        finally
        {
            _dataLock.ExitWriteLock();
        }
    }

    public async Task RemoveTableDependencyAsync(string tableName, string dependentTable, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        _dataLock.EnterWriteLock();
        try
        {
            if (_tableDependencies.TryGetValue(tableName, out var dependencies))
            {
                dependencies.Remove(dependentTable);
                if (!dependencies.Any())
                {
                    _tableDependencies.TryRemove(tableName, out _);
                }
            }
            
            if (_reverseTableDependencies.TryGetValue(dependentTable, out var parents))
            {
                parents.Remove(tableName);
                if (!parents.Any())
                {
                    _reverseTableDependencies.TryRemove(dependentTable, out _);
                }
            }
            
            _logger.LogDebug("Removed table dependency: {TableName} -> {DependentTable}", tableName, dependentTable);
        }
        finally
        {
            _dataLock.ExitWriteLock();
        }
    }

    public async Task<TableDependencyGraph> GetTableDependencyGraphAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AdvancedCacheKeyTracker));
        
        _dataLock.EnterReadLock();
        try
        {
            return new TableDependencyGraph
            {
                Dependencies = _tableDependencies.ToDictionary(
                    kvp => kvp.Key, 
                    kvp => kvp.Value.ToHashSet()),
                ReverseDependencies = _reverseTableDependencies.ToDictionary(
                    kvp => kvp.Key, 
                    kvp => kvp.Value.ToHashSet())
            };
        }
        finally
        {
            _dataLock.ExitReadLock();
        }
    }

    private Regex CreatePatternRegex(string pattern)
    {
        // 와일드카드 패턴을 정규식으로 변환
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        
        return new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, _options.RegexTimeout);
    }

    private void AddHistoryEntry(string cacheKey, string action, string? tableName, Dictionary<string, object>? metadata = null)
    {
        if (!_options.EnableHistory) return;
        
        var entry = new CacheKeyHistoryEntry
        {
            CacheKey = cacheKey,
            Action = action,
            TableName = tableName,
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = metadata
        };
        
        _history.Enqueue(entry);
        
        // 히스토리 크기 제한
        if (_history.Count > _options.MaxHistoryEntries * 1.1) // 약간의 버퍼 허용
        {
            for (int i = 0; i < _options.MaxHistoryEntries * 0.1; i++) // 10% 제거
            {
                _history.TryDequeue(out _);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        try
        {
            _cleanupTimer?.Dispose();
            _dataLock.Dispose();
            
            _logger.LogDebug("AdvancedCacheKeyTracker disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AdvancedCacheKeyTracker disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// 캐시 키 추적 옵션
/// </summary>
public class CacheKeyTrackingOptions
{
    /// <summary>주기적 정리 활성화</summary>
    public bool EnablePeriodicCleanup { get; set; } = true;
    
    /// <summary>정리 주기</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(30);
    
    /// <summary>패턴 정리 기준 나이</summary>
    public TimeSpan PatternCleanupAge { get; set; } = TimeSpan.FromHours(24);
    
    /// <summary>히스토리 기능 활성화</summary>
    public bool EnableHistory { get; set; } = true;
    
    /// <summary>최대 히스토리 항목 수</summary>
    public int MaxHistoryEntries { get; set; } = 10000;
    
    /// <summary>정규식 시간 제한</summary>
    public TimeSpan RegexTimeout { get; set; } = TimeSpan.FromSeconds(5);
    
    /// <summary>최대 추적 키 수</summary>
    public int MaxTrackedKeys { get; set; } = 100000;
    
    /// <summary>메모리 압축 임계값</summary>
    public int MemoryCompactionThreshold { get; set; } = 50000;
}
