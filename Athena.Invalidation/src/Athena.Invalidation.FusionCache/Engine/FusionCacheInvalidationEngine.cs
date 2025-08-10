using Athena.Invalidation.Core.Abstractions;

namespace Athena.Invalidation.FusionCache.Engine;

/// <summary>
/// FusionCache를 위한 실제 무효화 엔진 구현
/// </summary>
public class FusionCacheInvalidationEngine(
    ICacheProvider cacheProvider,
    ILogger<FusionCacheInvalidationEngine> logger,
    IOptions<FusionCacheInvalidationOptions> options)
    : IInvalidationEngine, IAsyncDisposable
{
    private readonly ICacheProvider _cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));
    private readonly ILogger<FusionCacheInvalidationEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly FusionCacheInvalidationOptions _options = options.Value ?? new FusionCacheInvalidationOptions();
    
    // 키 추적 시스템
    private readonly Dictionary<string, HashSet<string>> _tableKeyMappings = new();
    private readonly Dictionary<string, IInvalidationRule> _rules = new();
    private readonly ReaderWriterLockSlim _mappingLock = new();
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    
    private volatile bool _disposed = false;

    public async Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        try
        {
            var keys = GetTrackedKeysForTable(tableName);
            if (!keys.Any())
            {
                _logger.LogDebug("No tracked keys found for table: {TableName}", tableName);
                return;
            }

            await _cacheProvider.RemoveManyAsync(keys, cancellationToken);
            _logger.LogInformation("Invalidated {Count} keys for table: {TableName}", keys.Count, tableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate table: {TableName}", tableName);
            throw;
        }
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        try
        {
            // 패턴을 캐시 태그로 변환 시도
            if (_options.ConvertPatternToTag && IsValidTagPattern(pattern))
            {
                var tag = ConvertPatternToTag(pattern);
                // ICacheProvider는 태그 기반 무효화를 직접 지원하지 않음
            _logger.LogWarning("Tag-based invalidation not supported by ICacheProvider interface");
                _logger.LogDebug("Invalidated by tag (from pattern): {Tag}", tag);
            }
            else
            {
                // 패턴 매칭을 통한 무효화
                await _cacheProvider.RemoveByPatternAsync(pattern, cancellationToken);
                _logger.LogDebug("Invalidated by pattern: {Pattern}", pattern);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate by pattern: {Pattern}", pattern);
            throw;
        }
    }

    public async Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        try
        {
            await _cacheProvider.RemoveAsync(key, cancellationToken);
            _logger.LogDebug("Invalidated key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate key: {Key}", key);
            throw;
        }
    }

    public async Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
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
            }

            if (allKeys.Any())
            {
                await _cacheProvider.RemoveManyAsync(allKeys, cancellationToken);
                _logger.LogInformation("Invalidated {KeyCount} keys for {TableCount} tables in batch", 
                    allKeys.Count, tableNames.Count());
            }
            else
            {
                _logger.LogDebug("No tracked keys found for batch invalidation of {TableCount} tables", 
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
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        try
        {
            var allTables = new HashSet<string> { tableName };
            foreach (var relatedTable in relatedTables)
            {
                allTables.Add(relatedTable);
            }

            await InvalidateBatchAsync(allTables, cancellationToken);
            _logger.LogInformation("Invalidated hierarchical cache for {RootTable} with {RelatedCount} related tables", 
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
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        // CQRS 명령 처리는 규칙 기반으로 처리
        var commandType = typeof(TCommand).Name;
        _logger.LogDebug("Processing command invalidation: {CommandType}", commandType);
        
        // 향후 규칙 엔진을 통해 명령별 무효화 로직 구현
        return Task.CompletedTask;
    }

    public Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        // 도메인 이벤트 처리는 규칙 기반으로 처리
        var eventType = typeof(TEvent).Name;
        _logger.LogDebug("Processing domain event invalidation: {EventType}", eventType);
        
        // 향후 규칙 엔진을 통해 이벤트별 무효화 로직 구현
        return Task.CompletedTask;
    }

    public Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default) where TReadModel : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        var modelType = typeof(TReadModel).Name;
        var key = modelId != null ? $"{modelType}:{modelId}" : $"{modelType}:*";
        
        return InvalidateByPatternAsync(key, cancellationToken);
    }

    public Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default) where TProjection : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        var projectionType = typeof(TProjection).Name;
        var key = projectionId != null ? $"{projectionType}:{projectionId}" : $"{projectionType}:*";
        
        return InvalidateByPatternAsync(key, cancellationToken);
    }

    public Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        _mappingLock.EnterWriteLock();
        try
        {
            if (!_tableKeyMappings.ContainsKey(tableName))
            {
                _tableKeyMappings[tableName] = new HashSet<string>();
            }
            
            _tableKeyMappings[tableName].Add(cacheKey);
            _logger.LogTrace("Tracked key {CacheKey} for table {TableName}", cacheKey, tableName);
        }
        finally
        {
            _mappingLock.ExitWriteLock();
        }
        
        return Task.CompletedTask;
    }

    public Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
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
            }
            
            _logger.LogTrace("Tracked key {CacheKey} for {TableCount} tables", cacheKey, tableNames.Length);
        }
        finally
        {
            _mappingLock.ExitWriteLock();
        }
        
        return Task.CompletedTask;
    }

    public Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        var keys = GetTrackedKeysForTable(tableName);
        return Task.FromResult<IEnumerable<string>>(keys);
    }

    public Task RegisterInvalidationRuleAsync(IInvalidationRule rule, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        _rules[rule.Id] = rule;
        _logger.LogDebug("Registered invalidation rule: {RuleId}", rule.Id);
        
        return Task.CompletedTask;
    }

    public Task UnregisterInvalidationRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        if (_rules.Remove(ruleId))
        {
            _logger.LogDebug("Unregistered invalidation rule: {RuleId}", ruleId);
        }
        
        return Task.CompletedTask;
    }

    public Task<IEnumerable<IInvalidationRule>> GetInvalidationRulesAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        return Task.FromResult<IEnumerable<IInvalidationRule>>(_rules.Values);
    }

    public IInvalidationContext CreateContext(InvalidationTrigger trigger, object? metadata = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        var context = new FusionCacheInvalidationContext(trigger);
        
        if (metadata != null)
        {
            if (metadata is Dictionary<string, object> metaDict)
            {
                foreach (var kvp in metaDict)
                {
                    context.AddMetadata(kvp.Key, kvp.Value);
                }
            }
            else
            {
                context.AddMetadata("metadata", metadata);
            }
        }
        
        context.AddMetadata("cache_provider", _cacheProvider.ProviderName);
        
        return context;
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        try
        {
            // ICacheProvider does not have InvalidateAllAsync, clear tracked keys instead
            var allKeys = GetAllTrackedKeys();
            if (allKeys.Any())
            {
                await _cacheProvider.RemoveManyAsync(allKeys, cancellationToken);
            }
            
            // 추적 정보도 클리어
            _mappingLock.EnterWriteLock();
            try
            {
                _tableKeyMappings.Clear();
                _logger.LogWarning("Cleared all cache entries and tracking information");
            }
            finally
            {
                _mappingLock.ExitWriteLock();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear all cache entries");
            throw;
        }
    }

    public async Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheInvalidationEngine));
        
        try
        {
            var isHealthy = await _cacheProvider.IsHealthyAsync(cancellationToken);
            var cacheStats = await _cacheProvider.GetStatisticsAsync(cancellationToken);
            
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
                    ["cache_provider_name"] = _cacheProvider.ProviderName,
                    ["tracked_tables"] = _tableKeyMappings.Count,
                    ["cache_hit_ratio"] = cacheStats.HitRatio,
                    ["cache_total_keys"] = cacheStats.TotalKeys
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get invalidation engine status");
            throw;
        }
    }

    private List<string> GetAllTrackedKeys()
    {
        _mappingLock.EnterReadLock();
        try
        {
            return _tableKeyMappings.Values
                .SelectMany(keys => keys)
                .Distinct()
                .ToList();
        }
        finally
        {
            _mappingLock.ExitReadLock();
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

    private static bool IsValidTagPattern(string pattern)
    {
        // 태그로 변환 가능한 패턴인지 확인
        // 예: "user:*" -> "user" 태그
        return pattern.Contains(':') && pattern.EndsWith("*");
    }

    private static string ConvertPatternToTag(string pattern)
    {
        // 패턴을 태그로 변환
        // 예: "user:*" -> "user"
        var colonIndex = pattern.IndexOf(':');
        return colonIndex > 0 ? pattern[..colonIndex] : pattern.TrimEnd('*');
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        try
        {
            _mappingLock.Dispose();
            
            if (_cacheProvider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_cacheProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            _logger.LogDebug("FusionCacheInvalidationEngine disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during FusionCacheInvalidationEngine disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// FusionCache 무효화 옵션
/// </summary>
public class FusionCacheInvalidationOptions
{
    /// <summary>패턴을 태그로 변환 시도 여부</summary>
    public bool ConvertPatternToTag { get; set; } = true;
    
    /// <summary>키 추적 활성화 여부</summary>
    public bool EnableKeyTracking { get; set; } = true;
    
    /// <summary>최대 추적 키 수</summary>
    public int MaxTrackedKeys { get; set; } = 100000;
    
    /// <summary>키 추적 정리 간격</summary>
    public TimeSpan KeyTrackingCleanupInterval { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// FusionCache 무효화 컨텍스트
/// </summary>
public class FusionCacheInvalidationContext(InvalidationTrigger trigger) : BaseInvalidationContext(trigger)
{
    public override IInvalidationContext Clone()
    {
        var clone = new FusionCacheInvalidationContext(Trigger);
        CopyPropertiesTo(clone);
        return clone;
    }
}
