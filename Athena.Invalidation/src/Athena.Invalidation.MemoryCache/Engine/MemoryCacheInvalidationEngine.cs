using Athena.Invalidation.MemoryCache.Abstractions;

namespace Athena.Invalidation.MemoryCache.Engine;

/// <summary>
/// Memory Cache를 위한 실제 무효화 엔진 구현
/// </summary>
public class MemoryCacheInvalidationEngine(
    IMemoryCacheInvalidationProvider memoryCacheProvider,
    ILogger<MemoryCacheInvalidationEngine> logger,
    IOptions<MemoryCacheInvalidationEngineOptions> options)
    : IInvalidationEngine, IAsyncDisposable
{
    private readonly IMemoryCacheInvalidationProvider _memoryCacheProvider = memoryCacheProvider ?? throw new ArgumentNullException(nameof(memoryCacheProvider));
    private readonly ILogger<MemoryCacheInvalidationEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly MemoryCacheInvalidationEngineOptions _options = options.Value ?? new MemoryCacheInvalidationEngineOptions();
    
    // 키 추적 및 규칙 시스템
    private readonly Dictionary<string, HashSet<string>> _tableKeyMappings = new();
    private readonly Dictionary<string, IInvalidationRule> _rules = new();
    private readonly ReaderWriterLockSlim _mappingLock = new();
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    
    private volatile bool _disposed = false;

    public async Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        try
        {
            var keys = GetTrackedKeysForTable(tableName);
            if (!keys.Any())
            {
                // 테이블 기반 패턴으로도 시도
                var pattern = _options.TableKeyPattern.Replace("{table}", tableName);
                await _memoryCacheProvider.InvalidateByPatternAsync(pattern, cancellationToken);
                _logger.LogDebug("Invalidated memory cache by pattern for table: {TableName}", tableName);
            }
            else
            {
                await _memoryCacheProvider.InvalidateBatchAsync(keys, cancellationToken);
                _logger.LogInformation("Invalidated {Count} memory cache keys for table: {TableName}", keys.Count, tableName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate table: {TableName}", tableName);
            throw;
        }
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        try
        {
            await _memoryCacheProvider.InvalidateByPatternAsync(pattern, cancellationToken);
            _logger.LogDebug("Invalidated memory cache by pattern: {Pattern}", pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate by pattern: {Pattern}", pattern);
            throw;
        }
    }

    public async Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        try
        {
            await _memoryCacheProvider.InvalidateAsync(key, cancellationToken);
            _logger.LogDebug("Invalidated memory cache key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate key: {Key}", key);
            throw;
        }
    }

    public async Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        try
        {
            var allKeys = new HashSet<string>();
            
            foreach (var tableName in tableNames)
            {
                var keys = GetTrackedKeysForTable(tableName);
                foreach (var key in keys)
                {
                    allKeys.Add(key);
                }
                
                // 추적된 키가 없다면 패턴으로도 확인
                if (!keys.Any())
                {
                    var pattern = _options.TableKeyPattern.Replace("{table}", tableName);
                    var patternKeys = await _memoryCacheProvider.GetKeysByPatternAsync(pattern, cancellationToken);
                    foreach (var key in patternKeys)
                    {
                        allKeys.Add(key);
                    }
                }
            }

            if (allKeys.Any())
            {
                await _memoryCacheProvider.InvalidateBatchAsync(allKeys, cancellationToken);
                _logger.LogInformation("Invalidated {KeyCount} memory cache keys for {TableCount} tables in batch", 
                    allKeys.Count, tableNames.Count());
            }
            else
            {
                _logger.LogDebug("No memory cache keys found for batch invalidation of {TableCount} tables", 
                    tableNames.Count());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate batch of tables: {Tables}", 
                string.Join(", ", tableNames));
            throw;
        }
    }

    public async Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        try
        {
            var allTables = new HashSet<string> { tableName };
            foreach (var relatedTable in relatedTables)
            {
                allTables.Add(relatedTable);
            }

            await InvalidateBatchAsync(allTables, cancellationToken);
            _logger.LogInformation("Invalidated hierarchical memory cache for {RootTable} with {RelatedCount} related tables", 
                tableName, relatedTables.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate hierarchy for table: {TableName}", tableName);
            throw;
        }
    }

    public Task InvalidateOnCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        // CQRS 명령 처리는 규칙 기반으로 처리
        var commandType = typeof(TCommand).Name;
        _logger.LogDebug("Processing memory cache command invalidation: {CommandType}", commandType);
        
        // 향후 규칙 엔진을 통해 명령별 무효화 로직 구현
        return Task.CompletedTask;
    }

    public Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        // 도메인 이벤트 처리는 규칙 기반으로 처리
        var eventType = typeof(TEvent).Name;
        _logger.LogDebug("Processing memory cache domain event invalidation: {EventType}", eventType);
        
        // 향후 규칙 엔진을 통해 이벤트별 무효화 로직 구현
        return Task.CompletedTask;
    }

    public Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default) where TReadModel : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        var modelType = typeof(TReadModel).Name;
        var key = modelId != null ? $"{modelType}:{modelId}" : $"{modelType}:*";
        
        return InvalidateByPatternAsync(key, cancellationToken);
    }

    public Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default) where TProjection : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        var projectionType = typeof(TProjection).Name;
        var key = projectionId != null ? $"{projectionType}:{projectionId}" : $"{projectionType}:*";
        
        return InvalidateByPatternAsync(key, cancellationToken);
    }

    public async Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        _mappingLock.EnterWriteLock();
        try
        {
            if (!_tableKeyMappings.ContainsKey(tableName))
            {
                _tableKeyMappings[tableName] = new HashSet<string>();
            }
            
            _tableKeyMappings[tableName].Add(cacheKey);
            
            // 태그 기반 추적도 활성화
            if (_options.EnableTagBasedTracking)
            {
                await _memoryCacheProvider.TagKeyAsync(cacheKey, tableName, cancellationToken);
            }
            
            _logger.LogTrace("Tracked memory cache key {CacheKey} for table {TableName}", cacheKey, tableName);
        }
        finally
        {
            _mappingLock.ExitWriteLock();
        }
    }

    public async Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        _mappingLock.EnterWriteLock();
        try
        {
            foreach (var tableName in tableNames)
            {
                if (!_tableKeyMappings.ContainsKey(tableName))
                {
                    _tableKeyMappings[tableName] = new HashSet<string>();
                }
                
                _tableKeyMappings[tableName].Add(cacheKey);
                
                // 태그 기반 추적도 활성화
                if (_options.EnableTagBasedTracking)
                {
                    await _memoryCacheProvider.TagKeyAsync(cacheKey, tableName, cancellationToken);
                }
            }
            
            _logger.LogTrace("Tracked memory cache key {CacheKey} for {TableCount} tables", cacheKey, tableNames.Length);
        }
        finally
        {
            _mappingLock.ExitWriteLock();
        }
    }

    public Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        var keys = GetTrackedKeysForTable(tableName);
        return Task.FromResult<IEnumerable<string>>(keys);
    }

    public Task RegisterInvalidationRuleAsync(IInvalidationRule rule, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        _rules[rule.Id] = rule;
        _logger.LogDebug("Registered invalidation rule: {RuleId}", rule.Id);
        
        return Task.CompletedTask;
    }

    public Task UnregisterInvalidationRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        if (_rules.Remove(ruleId))
        {
            _logger.LogDebug("Unregistered invalidation rule: {RuleId}", ruleId);
        }
        
        return Task.CompletedTask;
    }

    public Task<IEnumerable<IInvalidationRule>> GetInvalidationRulesAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        return Task.FromResult<IEnumerable<IInvalidationRule>>(_rules.Values);
    }

    public IInvalidationContext CreateContext(InvalidationTrigger trigger, object? metadata = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        var context = new MemoryCacheInvalidationContext(trigger);
        
        if (metadata is Dictionary<string, object> metadataDict)
        {
            foreach (var kvp in metadataDict)
            {
                context.AddMetadata(kvp.Key, kvp.Value);
            }
        }
        
        context.AddMetadata("cache_provider", _memoryCacheProvider.Name);
        context.AddMetadata("cache_count", _memoryCacheProvider.Count);
        
        return context;
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        try
        {
            await _memoryCacheProvider.InvalidateAllAsync(cancellationToken);
            
            // 추적 정보도 클리어
            _mappingLock.EnterWriteLock();
            try
            {
                _tableKeyMappings.Clear();
                _logger.LogWarning("Cleared all memory cache entries and tracking information");
            }
            finally
            {
                _mappingLock.ExitWriteLock();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear all memory cache entries");
            throw;
        }
    }

    public async Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationEngine));
        
        try
        {
            var isHealthy = await _memoryCacheProvider.IsHealthyAsync(cancellationToken);
            var cacheStats = await _memoryCacheProvider.GetStatisticsAsync(cancellationToken);
            
            _mappingLock.EnterReadLock();
            int trackedKeysCount, registeredRulesCount;
            try
            {
                trackedKeysCount = _tableKeyMappings.Values.Sum(keys => keys.Count);
                registeredRulesCount = _rules.Count;
            }
            finally
            {
                _mappingLock.ExitReadLock();
            }
            
            return new InvalidationEngineStatus
            {
                IsHealthy = isHealthy,
                Uptime = DateTimeOffset.UtcNow - _startTime,
                TrackedKeysCount = trackedKeysCount,
                RegisteredRulesCount = registeredRulesCount,
                LastActivity = DateTimeOffset.UtcNow,
                Metrics = new Dictionary<string, object>
                {
                    ["cache_provider_name"] = _memoryCacheProvider.Name,
                    ["cache_provider_type"] = _memoryCacheProvider.Type.ToString(),
                    ["tracked_tables"] = _tableKeyMappings.Count,
                    ["cache_total_keys"] = cacheStats.TotalKeys,
                    ["cache_total_tags"] = cacheStats.TotalTags,
                    ["cache_hit_ratio"] = cacheStats.HitRatio,
                    ["cache_total_evictions"] = cacheStats.TotalEvictions
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get memory cache invalidation engine status");
            throw;
        }
    }

    private List<string> GetTrackedKeysForTable(string tableName)
    {
        _mappingLock.EnterReadLock();
        try
        {
            return _tableKeyMappings.TryGetValue(tableName, out var keys) 
                ? keys.ToList() 
                : new List<string>();
        }
        finally
        {
            _mappingLock.ExitReadLock();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        await Task.Yield(); // Make the method genuinely async
        try
        {
            _mappingLock.Dispose();
            
            if (_memoryCacheProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            _logger.LogDebug("MemoryCacheInvalidationEngine disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during MemoryCacheInvalidationEngine disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Memory Cache 무효화 엔진 옵션
/// </summary>
public class MemoryCacheInvalidationEngineOptions
{
    /// <summary>테이블 키 패턴 (테이블 이름을 {table}로 치환)</summary>
    public string TableKeyPattern { get; set; } = "table:{table}:*";
    
    /// <summary>키 추적 활성화 여부</summary>
    public bool EnableKeyTracking { get; set; } = true;
    
    /// <summary>태그 기반 추적 활성화 여부</summary>
    public bool EnableTagBasedTracking { get; set; } = true;
    
    /// <summary>최대 추적 키 수</summary>
    public int MaxTrackedKeys { get; set; } = 100000;
    
    /// <summary>키 추적 정리 간격</summary>
    public TimeSpan KeyTrackingCleanupInterval { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Memory Cache 무효화 컨텍스트
/// </summary>
public class MemoryCacheInvalidationContext(InvalidationTrigger trigger) : BaseInvalidationContext(trigger)
{
    public override IInvalidationContext Clone()
    {
        var clone = new MemoryCacheInvalidationContext(Trigger);
        CopyPropertiesTo(clone);
        return clone;
    }
}
