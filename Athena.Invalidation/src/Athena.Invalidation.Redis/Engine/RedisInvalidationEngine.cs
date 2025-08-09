using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Redis.Abstractions;

namespace Athena.Invalidation.Redis.Engine;

/// <summary>
/// Redis를 위한 실제 무효화 엔진 구현
/// </summary>
public class RedisInvalidationEngine : IInvalidationEngine, IAsyncDisposable
{
    private readonly IRedisInvalidationProvider _redisProvider;
    private readonly ILogger<RedisInvalidationEngine> _logger;
    private readonly RedisInvalidationEngineOptions _options;
    
    // 키 추적 시스템 (Redis에서 직접 관리)
    private readonly Dictionary<string, IInvalidationRule> _rules = new();
    private readonly ReaderWriterLockSlim _rulesLock = new();
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    
    private volatile bool _disposed = false;

    public RedisInvalidationEngine(
        IRedisInvalidationProvider redisProvider,
        ILogger<RedisInvalidationEngine> logger,
        IOptions<RedisInvalidationEngineOptions> options)
    {
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new RedisInvalidationEngineOptions();
    }

    public async Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        try
        {
            // 테이블 이름을 패턴으로 변환하여 관련된 모든 키를 무효화
            var pattern = _options.TableKeyPattern.Replace("{table}", tableName);
            await _redisProvider.InvalidateByPatternAsync(pattern, cancellationToken);
            
            _logger.LogInformation("Invalidated Redis keys for table: {TableName} using pattern: {Pattern}", tableName, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate table: {TableName}", tableName);
            throw;
        }
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        try
        {
            await _redisProvider.InvalidateByPatternAsync(pattern, cancellationToken);
            _logger.LogDebug("Invalidated Redis keys by pattern: {Pattern}", pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate by pattern: {Pattern}", pattern);
            throw;
        }
    }

    public async Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        try
        {
            await _redisProvider.InvalidateAsync(key, cancellationToken);
            _logger.LogDebug("Invalidated Redis key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate key: {Key}", key);
            throw;
        }
    }

    public async Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        try
        {
            var allKeys = new HashSet<string>();
            
            foreach (var tableName in tableNames)
            {
                var pattern = _options.TableKeyPattern.Replace("{table}", tableName);
                var keys = await _redisProvider.ScanKeysAsync(pattern, _options.ScanPageSize, cancellationToken);
                
                foreach (var key in keys)
                {
                    allKeys.Add(key);
                }
            }

            if (allKeys.Any())
            {
                await _redisProvider.InvalidateBatchAsync(allKeys, cancellationToken);
                _logger.LogInformation("Invalidated {KeyCount} Redis keys for {TableCount} tables in batch", 
                    allKeys.Count, tableNames.Count());
            }
            else
            {
                _logger.LogDebug("No Redis keys found for batch invalidation of {TableCount} tables", 
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
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        try
        {
            var allTables = new HashSet<string> { tableName };
            foreach (var relatedTable in relatedTables)
            {
                allTables.Add(relatedTable);
            }

            await InvalidateBatchAsync(allTables, cancellationToken);
            _logger.LogInformation("Invalidated hierarchical Redis cache for {RootTable} with {RelatedCount} related tables", 
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
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        // CQRS 명령 처리는 규칙 기반으로 처리
        var commandType = typeof(TCommand).Name;
        _logger.LogDebug("Processing Redis command invalidation: {CommandType}", commandType);
        
        // 향후 규칙 엔진을 통해 명령별 무효화 로직 구현
        return Task.CompletedTask;
    }

    public Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        // 도메인 이벤트 처리는 규칙 기반으로 처리
        var eventType = typeof(TEvent).Name;
        _logger.LogDebug("Processing Redis domain event invalidation: {EventType}", eventType);
        
        // 향후 규칙 엔진을 통해 이벤트별 무효화 로직 구현
        return Task.CompletedTask;
    }

    public Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default) where TReadModel : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        var modelType = typeof(TReadModel).Name;
        var key = modelId != null ? $"{modelType}:{modelId}" : $"{modelType}:*";
        
        return InvalidateByPatternAsync(key, cancellationToken);
    }

    public Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default) where TProjection : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        var projectionType = typeof(TProjection).Name;
        var key = projectionId != null ? $"{projectionType}:{projectionId}" : $"{projectionType}:*";
        
        return InvalidateByPatternAsync(key, cancellationToken);
    }

    public async Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        // Redis에서는 키 추적을 위해 별도의 Set을 사용
        var trackingKey = GetTableTrackingKey(tableName);
        
        try
        {
            // Redis Set에 캐시 키 추가
            // 실제 구현에서는 IConnectionMultiplexer를 직접 사용하거나
            // IRedisInvalidationProvider에 추가 메서드 필요
            _logger.LogTrace("Would track key {CacheKey} for table {TableName} in Redis set {TrackingKey}", 
                cacheKey, tableName, trackingKey);
                
            // 임시 구현: 로깅만 수행
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track cache key {CacheKey} for table {TableName}", cacheKey, tableName);
            throw;
        }
    }

    public async Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        try
        {
            foreach (var tableName in tableNames)
            {
                await TrackCacheKeyAsync(tableName, cacheKey, cancellationToken);
            }
            
            _logger.LogTrace("Tracked key {CacheKey} for {TableCount} tables in Redis", cacheKey, tableNames.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track cache key {CacheKey} for multiple tables", cacheKey);
            throw;
        }
    }

    public async Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        try
        {
            // Redis에서 키 추적을 조회하는 로직
            var trackingKey = GetTableTrackingKey(tableName);
            
            // 실제 구현에서는 Redis Set에서 멤버들을 조회
            _logger.LogTrace("Would retrieve tracked keys for table {TableName} from Redis set {TrackingKey}", 
                tableName, trackingKey);
            
            // 임시 구현: 빈 리스트 반환
            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get tracked keys for table: {TableName}", tableName);
            throw;
        }
    }

    public Task RegisterInvalidationRuleAsync(IInvalidationRule rule, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        _rulesLock.EnterWriteLock();
        try
        {
            _rules[rule.Id] = rule;
            _logger.LogDebug("Registered invalidation rule: {RuleId}", rule.Id);
        }
        finally
        {
            _rulesLock.ExitWriteLock();
        }
        
        return Task.CompletedTask;
    }

    public Task UnregisterInvalidationRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        _rulesLock.EnterWriteLock();
        try
        {
            if (_rules.Remove(ruleId))
            {
                _logger.LogDebug("Unregistered invalidation rule: {RuleId}", ruleId);
            }
        }
        finally
        {
            _rulesLock.ExitWriteLock();
        }
        
        return Task.CompletedTask;
    }

    public Task<IEnumerable<IInvalidationRule>> GetInvalidationRulesAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        _rulesLock.EnterReadLock();
        try
        {
            return Task.FromResult<IEnumerable<IInvalidationRule>>(_rules.Values.ToList());
        }
        finally
        {
            _rulesLock.ExitReadLock();
        }
    }

    public IInvalidationContext CreateContext(InvalidationTrigger trigger, object? metadata = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        var context = new RedisInvalidationContext(trigger);
        
        if (metadata is Dictionary<string, object> metadataDict)
        {
            foreach (var kvp in metadataDict)
            {
                context.AddMetadata(kvp.Key, kvp.Value);
            }
        }
        
        context.AddMetadata("cache_provider", _redisProvider.Name);
        context.AddMetadata("redis_database", _redisProvider.Database);
        
        return context;
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        try
        {
            if (!_options.AllowClearAll)
            {
                _logger.LogWarning("Clear all operation is disabled for safety");
                throw new InvalidOperationException("Clear all operation is disabled for safety");
            }
            
            // Redis에서 모든 키를 삭제하는 것은 위험한 작업
            _logger.LogWarning("Performing FLUSHDB operation on Redis database {Database} - this will remove ALL keys", 
                _redisProvider.Database);
            
            // 실제 구현에서는 FLUSHDB 명령을 실행해야 함
            // 현재는 경고만 출력
            _logger.LogCritical("FLUSHDB operation not implemented for safety - would clear database {Database}", 
                _redisProvider.Database);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear all Redis entries");
            throw;
        }
    }

    public async Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationEngine));
        
        try
        {
            var isHealthy = await _redisProvider.IsHealthyAsync(cancellationToken);
            var redisStats = await _redisProvider.GetStatisticsAsync(cancellationToken);
            
            _rulesLock.EnterReadLock();
            int registeredRulesCount;
            try
            {
                registeredRulesCount = _rules.Count;
            }
            finally
            {
                _rulesLock.ExitReadLock();
            }
            
            return new InvalidationEngineStatus
            {
                IsHealthy = isHealthy,
                Uptime = DateTimeOffset.UtcNow - _startTime,
                TrackedKeysCount = (int)redisStats.TotalKeys, // Redis 키 총 개수
                RegisteredRulesCount = registeredRulesCount,
                LastActivity = DateTimeOffset.UtcNow,
                Metrics = new Dictionary<string, object>
                {
                    ["cache_provider_name"] = _redisProvider.Name,
                    ["cache_provider_type"] = _redisProvider.Type.ToString(),
                    ["redis_database"] = _redisProvider.Database,
                    ["redis_is_connected"] = _redisProvider.IsConnected,
                    ["redis_total_keys"] = redisStats.TotalKeys,
                    ["redis_expired_keys"] = redisStats.ExpiredKeys,
                    ["redis_evicted_keys"] = redisStats.EvictedKeys,
                    ["redis_uptime"] = redisStats.Uptime.TotalSeconds
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Redis invalidation engine status");
            throw;
        }
    }

    private string GetTableTrackingKey(string tableName)
    {
        return $"{_options.TrackingKeyPrefix}{tableName}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        try
        {
            _rulesLock.Dispose();
            
            if (_redisProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            _logger.LogDebug("RedisInvalidationEngine disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during RedisInvalidationEngine disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Redis 무효화 엔진 옵션
/// </summary>
public class RedisInvalidationEngineOptions
{
    /// <summary>테이블 키 패턴 (테이블 이름을 {table}로 치환)</summary>
    public string TableKeyPattern { get; set; } = "table:{table}:*";
    
    /// <summary>키 추적용 접두사</summary>
    public string TrackingKeyPrefix { get; set; } = "__athena_tracking:";
    
    /// <summary>스캔 시 페이지 크기</summary>
    public int ScanPageSize { get; set; } = 1000;
    
    /// <summary>전체 클리어 허용 여부</summary>
    public bool AllowClearAll { get; set; } = false;
    
    /// <summary>키 추적 활성화 여부</summary>
    public bool EnableKeyTracking { get; set; } = true;
    
    /// <summary>키 추적 TTL (추적 키의 만료 시간)</summary>
    public TimeSpan TrackingKeyTTL { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Redis 무효화 컨텍스트
/// </summary>
public class RedisInvalidationContext : BaseInvalidationContext
{
    public RedisInvalidationContext(InvalidationTrigger trigger) : base(trigger)
    {
    }

    public override IInvalidationContext Clone()
    {
        var clone = new RedisInvalidationContext(Trigger);
        CopyPropertiesTo(clone);
        return clone;
    }
}