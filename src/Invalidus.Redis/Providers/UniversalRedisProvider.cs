using System.Text.Json;
using Invalidus.Core.Abstractions;
using StackExchange.Redis;

namespace Invalidus.Redis.Providers;

/// <summary>
/// 통합 Redis 프로바이더 - 캐싱과 무효화를 모두 처리하는 Redis 구현체
/// Universal Redis provider that handles both caching and invalidation operations
/// </summary>
public class UniversalRedisProvider : ICacheProvider, IDisposable
{
    #region Fields

    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IDatabase _database;
    private readonly ILogger<UniversalRedisProvider> _logger;
    private readonly RedisProviderOptions _options;
    
    private volatile bool _disposed = false;
    
    // Statistics tracking
    private long _hitCount = 0;
    private long _missCount = 0;
    private readonly DateTime _startTime = DateTime.UtcNow;

    #endregion

    #region Constructor

    public UniversalRedisProvider(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<UniversalRedisProvider> logger,
        IOptions<RedisProviderOptions> options)
    {
        _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new RedisProviderOptions();
        
        _database = _connectionMultiplexer.GetDatabase(_options.Database);
        
        // Connection event handlers
        _connectionMultiplexer.ConnectionFailed += OnConnectionFailed;
        _connectionMultiplexer.ConnectionRestored += OnConnectionRestored;
        
        _logger.LogInformation("UniversalRedisProvider initialized for database {Database}", _options.Database);
    }

    #endregion

    #region ICacheProvider Implementation

    public string ProviderName => _options.ProviderName;
    public CacheProviderType ProviderType => CacheProviderType.Distributed;
    public bool IsAvailable => _connectionMultiplexer?.IsConnected ?? false;

    #endregion

    #region Cache Operations (Get/Set)

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UniversalRedisProvider));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

        try
        {
            var prefixedKey = AddKeyPrefix(key);
            var redisValue = await _database.StringGetAsync(prefixedKey);

            if (redisValue.HasValue)
            {
                Interlocked.Increment(ref _hitCount);
                
                if (_options.Logging.LogCacheOperations)
                {
                    _logger.LogDebug("Cache HIT for key: {Key}", key);
                }

                var jsonString = redisValue.ToString();
                if (!string.IsNullOrEmpty(jsonString))
                {
                    return JsonSerializer.Deserialize<T>(jsonString, _options.JsonOptions);
                }
            }
            else
            {
                Interlocked.Increment(ref _missCount);
                
                if (_options.Logging.LogCacheOperations)
                {
                    _logger.LogDebug("Cache MISS for key: {Key}", key);
                }
            }

            return default;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error getting value for key: {Key}", key);
            
            if (_options.FailureHandling.ThrowOnRedisError)
                throw;
            
            return default;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize cached value for key: {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UniversalRedisProvider));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));
        if (value == null) return;

        try
        {
            var prefixedKey = AddKeyPrefix(key);
            var effectiveExpiration = expiration ?? _options.DefaultExpiration;
            
            var jsonString = JsonSerializer.Serialize(value, _options.JsonOptions);
            var success = await _database.StringSetAsync(prefixedKey, jsonString, effectiveExpiration);

            if (success && _options.Logging.LogCacheOperations)
            {
                _logger.LogDebug("Cache SET for key: {Key}, Expiration: {Expiration}", key, effectiveExpiration);
            }
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error setting value for key: {Key}", key);
            
            if (_options.FailureHandling.ThrowOnRedisError)
                throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to serialize value for key: {Key}", key);
            
            if (_options.FailureHandling.ThrowOnJsonError)
                throw;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UniversalRedisProvider));
        if (string.IsNullOrEmpty(key)) return false;

        try
        {
            var prefixedKey = AddKeyPrefix(key);
            return await _database.KeyExistsAsync(prefixedKey);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error checking key existence: {Key}", key);
            return false;
        }
    }

    public async Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UniversalRedisProvider));
        
        var result = new Dictionary<string, T?>();
        var keyList = keys?.ToList() ?? new List<string>();
        
        if (!keyList.Any()) return result;

        try
        {
            var redisKeys = keyList.Select(k => (RedisKey)AddKeyPrefix(k)).ToArray();
            var values = await _database.StringGetAsync(redisKeys);

            for (int i = 0; i < keyList.Count; i++)
            {
                var originalKey = keyList[i];
                var redisValue = values[i];

                if (redisValue.HasValue)
                {
                    try
                    {
                        var deserializedValue = JsonSerializer.Deserialize<T>(redisValue!, _options.JsonOptions);
                        result[originalKey] = deserializedValue;
                        Interlocked.Increment(ref _hitCount);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to deserialize cached value for key: {Key}", originalKey);
                        result[originalKey] = default;
                    }
                }
                else
                {
                    result[originalKey] = default;
                    Interlocked.Increment(ref _missCount);
                }
            }
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error getting multiple values");
            
            if (_options.FailureHandling.ThrowOnRedisError)
                throw;
        }

        return result;
    }

    public async Task SetManyAsync<T>(Dictionary<string, T> keyValuePairs, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UniversalRedisProvider));
        if (!keyValuePairs?.Any() == true) return;

        try
        {
            var effectiveExpiration = expiration ?? _options.DefaultExpiration;
            var redisKeyValues = new List<KeyValuePair<RedisKey, RedisValue>>();

            foreach (var kvp in keyValuePairs!)
            {
                if (kvp.Value != null)
                {
                    var jsonString = JsonSerializer.Serialize(kvp.Value, _options.JsonOptions);
                    redisKeyValues.Add(new KeyValuePair<RedisKey, RedisValue>(
                        AddKeyPrefix(kvp.Key), jsonString));
                }
            }

            if (redisKeyValues.Any())
            {
                // Use pipeline for better performance
                var batch = _database.CreateBatch();
                var tasks = new List<Task>();

                foreach (var kvp in redisKeyValues)
                {
                    tasks.Add(batch.StringSetAsync(kvp.Key, kvp.Value, effectiveExpiration));
                }

                batch.Execute();
                await Task.WhenAll(tasks);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting multiple values in cache");
            
            if (_options.FailureHandling.ThrowOnRedisError)
                throw;
        }
    }

    #endregion

    #region Invalidation Operations (Remove)

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UniversalRedisProvider));
        if (string.IsNullOrEmpty(key)) return false;

        try
        {
            var prefixedKey = AddKeyPrefix(key);
            var deleted = await _database.KeyDeleteAsync(prefixedKey);

            if (deleted && _options.Logging.LogInvalidation)
            {
                _logger.LogDebug("Invalidated Redis key: {Key}", key);
            }

            return deleted;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error removing key: {Key}", key);
            
            if (_options.FailureHandling.ThrowOnRedisError)
                throw;
                
            return false;
        }
    }

    public async Task<int> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UniversalRedisProvider));
        
        var keyList = keys?.ToList() ?? new List<string>();
        if (!keyList.Any()) return 0;

        try
        {
            var redisKeys = keyList.Select(k => (RedisKey)AddKeyPrefix(k)).ToArray();
            
            if (_options.UseTransaction && redisKeys.Length > 1)
            {
                // Use transaction for atomicity
                var transaction = _database.CreateTransaction();
                var deleteTask = transaction.KeyDeleteAsync(redisKeys);
                
                var success = await transaction.ExecuteAsync();
                if (success)
                {
                    var deletedCount = (int)await deleteTask;
                    
                    if (_options.Logging.LogInvalidation)
                    {
                        _logger.LogDebug("Invalidated {DeletedCount}/{TotalCount} Redis keys in transaction", 
                            deletedCount, redisKeys.Length);
                    }
                    
                    return deletedCount;
                }
                else
                {
                    _logger.LogWarning("Transaction failed for batch invalidation of {Count} keys", redisKeys.Length);
                    return 0;
                }
            }
            else
            {
                // Simple batch delete
                var deletedCount = await _database.KeyDeleteAsync(redisKeys);
                
                if (_options.Logging.LogInvalidation)
                {
                    _logger.LogDebug("Invalidated {DeletedCount}/{TotalCount} Redis keys in batch", 
                        deletedCount, redisKeys.Length);
                }
                
                return (int)deletedCount;
            }
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error removing multiple keys");
            
            if (_options.FailureHandling.ThrowOnRedisError)
                throw;
                
            return 0;
        }
    }

    public async Task<int> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UniversalRedisProvider));
        if (string.IsNullOrEmpty(pattern)) return 0;

        try
        {
            var server = GetServer();
            if (server == null)
            {
                _logger.LogWarning("No Redis server available for pattern deletion");
                return 0;
            }

            var prefixedPattern = AddKeyPrefix(pattern);
            var keys = new List<string>();
            
            await foreach (var key in server.KeysAsync(
                database: _options.Database,
                pattern: prefixedPattern,
                pageSize: _options.ScanPageSize))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (!string.IsNullOrEmpty(key))
                    keys.Add(key!);

                // Memory usage limit
                if (keys.Count >= _options.MaxScanResults)
                {
                    _logger.LogWarning("Pattern scan results limited to {MaxResults} keys for pattern: {Pattern}", 
                        _options.MaxScanResults, pattern);
                    break;
                }
            }

            if (!keys.Any())
            {
                if (_options.Logging.LogInvalidation)
                {
                    _logger.LogDebug("No keys found matching pattern: {Pattern}", pattern);
                }
                return 0;
            }

            // Remove found keys
            var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
            var deletedCount = await _database.KeyDeleteAsync(redisKeys);
            
            if (_options.Logging.LogInvalidation)
            {
                _logger.LogInformation("Invalidated {Count} keys matching pattern: {Pattern}", deletedCount, pattern);
            }

            return (int)deletedCount;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error removing keys by pattern: {Pattern}", pattern);
            
            if (_options.FailureHandling.ThrowOnRedisError)
                throw;
                
            return 0;
        }
    }

    public async Task<IEnumerable<string>> ScanKeysAsync(string pattern, int? limit = null, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UniversalRedisProvider));
        if (string.IsNullOrEmpty(pattern)) return Enumerable.Empty<string>();

        try
        {
            var server = GetServer();
            if (server == null)
            {
                _logger.LogWarning("No Redis server available for key scanning");
                return Enumerable.Empty<string>();
            }

            var prefixedPattern = AddKeyPrefix(pattern);
            var keys = new List<string>();
            var maxResults = limit ?? _options.MaxScanResults;

            await foreach (var key in server.KeysAsync(
                database: _options.Database,
                pattern: prefixedPattern,
                pageSize: _options.ScanPageSize))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (!string.IsNullOrEmpty(key))
                {
                    // Remove prefix for consistent API
                    var originalKey = RemoveKeyPrefix(key!);
                    keys.Add(originalKey);
                }

                if (keys.Count >= maxResults)
                    break;
            }

            return keys;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error scanning keys with pattern: {Pattern}", pattern);
            
            if (_options.FailureHandling.ThrowOnRedisError)
                throw;
                
            return Enumerable.Empty<string>();
        }
    }

    #endregion

    #region Advanced Operations

    public async Task<bool> SetExpiryAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UniversalRedisProvider));
        if (string.IsNullOrEmpty(key)) return false;

        try
        {
            var prefixedKey = AddKeyPrefix(key);
            return await _database.KeyExpireAsync(prefixedKey, expiry);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error setting expiry for key: {Key}", key);
            return false;
        }
    }

    public async Task<TimeSpan?> GetTtlAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UniversalRedisProvider));
        if (string.IsNullOrEmpty(key)) return null;

        try
        {
            var prefixedKey = AddKeyPrefix(key);
            return await _database.KeyTimeToLiveAsync(prefixedKey);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error getting TTL for key: {Key}", key);
            return null;
        }
    }

    public async Task<T?> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UniversalRedisProvider));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        // Try to get existing value first
        var existing = await GetAsync<T>(key, cancellationToken);
        if (existing != null) return existing;

        // Generate new value and set it
        try
        {
            var newValue = await factory(cancellationToken);
            if (newValue != null)
            {
                await SetAsync(key, newValue, expiration, cancellationToken);
            }
            return newValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in factory method for GetOrSet key: {Key}", key);
            throw;
        }
    }

    #endregion

    #region Statistics and Health

    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var statistics = new CacheStatistics
        {
            ProviderName = ProviderName,
            Type = ProviderType,
            TotalKeys = 0,
            DatabaseSize = 0,
            ExpiredKeys = 0,
            EvictedKeys = 0,
            HitRatio = CalculateHitRatio(),
            Uptime = DateTime.UtcNow - _startTime,
            LastAccess = DateTime.UtcNow
        };

        try
        {
            var server = GetServer();
            if (server != null)
            {
                var info = await server.InfoAsync();
                
                // Parse Redis INFO command results
                var serverSection = info.FirstOrDefault(section => section.Key == "Server");
                var statsSection = info.FirstOrDefault(section => section.Key == "Stats");
                var keyspaceSection = info.FirstOrDefault(section => section.Key == "Keyspace");
                
                // Extract statistics
                if (statsSection != null)
                {
                    foreach (var item in statsSection)
                    {
                        if (item.Key == "expired_keys" && long.TryParse(item.Value, out var expired))
                            statistics.ExpiredKeys = expired;
                        else if (item.Key == "evicted_keys" && long.TryParse(item.Value, out var evicted))
                            statistics.EvictedKeys = evicted;
                    }
                }
                
                if (keyspaceSection != null)
                {
                    var dbKey = $"db{_options.Database}";
                    var dbInfo = keyspaceSection.FirstOrDefault(item => item.Key == dbKey);
                    if (!dbInfo.Equals(default(KeyValuePair<string, string>)))
                    {
                        // Parse "db0:keys=2,expires=0,avg_ttl=0" format
                        var parts = dbInfo.Value.Split(',');
                        var keysPart = parts.FirstOrDefault(p => p.StartsWith("keys="));
                        if (keysPart != null && long.TryParse(keysPart.Substring(5), out var keys))
                        {
                            statistics.TotalKeys = keys;
                            statistics.DatabaseSize = keys;
                        }
                    }
                }

                // Additional metrics
                statistics.AdditionalMetrics["database_number"] = _options.Database;
                statistics.AdditionalMetrics["connection_string"] = _connectionMultiplexer.Configuration;
                statistics.AdditionalMetrics["is_connected"] = IsAvailable;
                statistics.AdditionalMetrics["scan_page_size"] = _options.ScanPageSize;
                statistics.AdditionalMetrics["use_transaction"] = _options.UseTransaction;
                statistics.AdditionalMetrics["hit_count"] = Interlocked.Read(ref _hitCount);
                statistics.AdditionalMetrics["miss_count"] = Interlocked.Read(ref _missCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Redis statistics");
        }

        return statistics;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

        try
        {
            if (!IsAvailable) return false;

            // Simple health check using PING
            var testKey = $"{_options.HealthCheckKeyPrefix}{Guid.NewGuid():N}";
            var testValue = DateTimeOffset.UtcNow.ToString();
            
            await _database.StringSetAsync(testKey, testValue, TimeSpan.FromSeconds(10));
            var retrieved = await _database.StringGetAsync(testKey);
            await _database.KeyDeleteAsync(testKey);

            return retrieved == testValue;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis health check failed");
            return false;
        }
    }

    public async Task<HealthCheckResult> GetHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            var isHealthy = await IsHealthyAsync(cancellationToken);
            var responseTime = DateTime.UtcNow - startTime;

            return new HealthCheckResult
            {
                IsHealthy = isHealthy,
                Status = isHealthy ? "Healthy" : "Unhealthy",
                Description = isHealthy ? "Redis connection is working properly" : "Redis connection issues detected",
                ResponseTime = responseTime,
                Data = new Dictionary<string, object>
                {
                    ["Provider"] = ProviderName,
                    ["Database"] = _options.Database,
                    ["IsConnected"] = IsAvailable,
                    ["ResponseTime"] = responseTime.TotalMilliseconds
                }
            };
        }
        catch (Exception ex)
        {
            var responseTime = DateTime.UtcNow - startTime;
            
            return new HealthCheckResult
            {
                IsHealthy = false,
                Status = "Unhealthy",
                Description = "Redis health check failed with exception",
                ResponseTime = responseTime,
                Exception = ex,
                Data = new Dictionary<string, object>
                {
                    ["Provider"] = ProviderName,
                    ["Error"] = ex.Message
                }
            };
        }
    }

    #endregion

    #region Events

    public event EventHandler<CacheKeyRemovedEventArgs>? KeyRemoved;
    public event EventHandler<CacheKeyExpiredEventArgs>? KeyExpired;

    #endregion

    #region Private Helper Methods

    private string AddKeyPrefix(string key)
    {
        return string.IsNullOrEmpty(_options.KeyPrefix) 
            ? key 
            : $"{_options.KeyPrefix}:{key}";
    }

    private string RemoveKeyPrefix(string prefixedKey)
    {
        if (string.IsNullOrEmpty(_options.KeyPrefix)) 
            return prefixedKey;

        var prefix = $"{_options.KeyPrefix}:";
        return prefixedKey.StartsWith(prefix) 
            ? prefixedKey.Substring(prefix.Length) 
            : prefixedKey;
    }

    private IServer? GetServer()
    {
        try
        {
            var endpoints = _connectionMultiplexer.GetEndPoints();
            if (!endpoints.Any())
            {
                _logger.LogWarning("No Redis endpoints available");
                return null;
            }

            var server = _connectionMultiplexer.GetServer(endpoints.First());
            if (!server.IsConnected)
            {
                _logger.LogWarning("Redis server is not connected");
                return null;
            }

            return server;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Redis server instance");
            return null;
        }
    }

    private double CalculateHitRatio()
    {
        var hits = Interlocked.Read(ref _hitCount);
        var misses = Interlocked.Read(ref _missCount);
        var total = hits + misses;
        
        return total == 0 ? 0.0 : (double)hits / total;
    }

    private void OnConnectionFailed(object? sender, ConnectionFailedEventArgs e)
    {
        _logger.LogError(e.Exception, "Redis connection failed: {FailureType} - {EndPoint}", 
            e.FailureType, e.EndPoint);
    }

    private void OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        _logger.LogInformation("Redis connection restored: {EndPoint}", e.EndPoint);
    }

    #endregion

    #region IDisposable Implementation

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _connectionMultiplexer.ConnectionFailed -= OnConnectionFailed;
            _connectionMultiplexer.ConnectionRestored -= OnConnectionRestored;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during UniversalRedisProvider disposal");
        }
        finally
        {
            _disposed = true;
        }
    }

    #endregion
}