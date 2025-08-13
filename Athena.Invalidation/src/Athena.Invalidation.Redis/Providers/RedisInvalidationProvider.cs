using Athena.Invalidation.Redis.Abstractions;

namespace Athena.Invalidation.Redis.Providers;

/// <summary>
/// Redis 기반 캐시 무효화 제공자
/// </summary>
public class RedisInvalidationProvider : IRedisInvalidationProvider, IDisposable
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IDatabase _database;
    private readonly ILogger<RedisInvalidationProvider> _logger;
    private readonly RedisInvalidationOptions _options;
    
    private volatile bool _disposed = false;

    public string Name { get; }
    public CacheProviderType Type => CacheProviderType.Distributed;
    public bool IsConnected => _connectionMultiplexer?.IsConnected ?? false;
    public int Database { get; }

    public RedisInvalidationProvider(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisInvalidationProvider> logger,
        IOptions<RedisInvalidationOptions> options)
    {
        _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new RedisInvalidationOptions();
        
        Name = _options.ProviderName;
        Database = _options.Database;
        _database = _connectionMultiplexer.GetDatabase(_options.Database);
    }

    public async Task InvalidateAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationProvider));
        
        try
        {
            var deleted = await _database.KeyDeleteAsync(key);
            if (deleted)
            {
                _logger.LogDebug("Invalidated Redis key: {Key}", key);
            }
            else
            {
                _logger.LogTrace("Key not found for invalidation: {Key}", key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate Redis key: {Key}", key);
            throw;
        }
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationProvider));
        
        try
        {
            var keys = await ScanKeysAsync(pattern, _options.ScanPageSize, cancellationToken);
            var keyList = keys.ToList();
            
            if (!keyList.Any())
            {
                _logger.LogDebug("No keys found matching pattern: {Pattern}", pattern);
                return;
            }

            await InvalidateBatchAsync(keyList, cancellationToken);
            _logger.LogInformation("Invalidated {Count} keys matching pattern: {Pattern}", keyList.Count, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate by pattern: {Pattern}", pattern);
            throw;
        }
    }

    public async Task InvalidateBatchAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationProvider));
        
        var keyList = keys.ToList();
        if (!keyList.Any()) return;

        try
        {
            // Redis는 한 번에 여러 키를 삭제할 수 있음
            var redisKeys = keyList.Select(k => (RedisKey)k).ToArray();
            
            if (_options.UseTransaction && redisKeys.Length > 1)
            {
                // 트랜잭션 사용
                var transaction = _database.CreateTransaction();
                foreach (var key in redisKeys)
                {
                    _ = transaction.KeyDeleteAsync(key);
                }
                
                var success = await transaction.ExecuteAsync();
                if (success)
                {
                    _logger.LogDebug("Invalidated {Count} Redis keys in transaction", redisKeys.Length);
                }
                else
                {
                    _logger.LogWarning("Transaction failed for batch invalidation of {Count} keys", redisKeys.Length);
                }
            }
            else
            {
                // 배치 삭제
                var deletedCount = await _database.KeyDeleteAsync(redisKeys);
                _logger.LogDebug("Invalidated {DeletedCount}/{TotalCount} Redis keys in batch", deletedCount, redisKeys.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate batch of {Count} keys", keyList.Count);
            throw;
        }
    }

    public async Task<IEnumerable<string>> ScanKeysAsync(string pattern, int pageSize = 1000, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationProvider));
        
        try
        {
            var server = GetServer();
            var keys = new List<string>();
            
            await foreach (var key in server.KeysAsync(
                database: _options.Database,
                pattern: pattern,
                pageSize: pageSize))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                if (!string.IsNullOrEmpty(key))
                    keys.Add(key!);
                
                // 메모리 사용량 제한
                if (keys.Count >= _options.MaxScanResults)
                {
                    _logger.LogWarning("Scan results limited to {MaxResults} keys for pattern: {Pattern}", 
                        _options.MaxScanResults, pattern);
                    break;
                }
            }
            
            return keys;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan Redis keys with pattern: {Pattern}", pattern);
            throw;
        }
    }

    public async Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationProvider));
        
        try
        {
            return await _database.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check key existence: {Key}", key);
            throw;
        }
    }

    public async Task<bool> SetExpiryAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationProvider));
        
        try
        {
            return await _database.KeyExpireAsync(key, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set expiry for key: {Key}", key);
            throw;
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;
        
        try
        {
            if (!IsConnected) return false;
            
            // 간단한 헬스체크를 위해 PING 명령 실행
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

    public async Task<RedisInvalidationStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisInvalidationProvider));
        
        try
        {
            var server = GetServer();
            var info = await server.InfoAsync();
            
            // Redis INFO 명령에서 통계 파싱
            var serverSection = info.FirstOrDefault(section => section.Key == "Server");
            var statsSection = info.FirstOrDefault(section => section.Key == "Stats");
            var keyspaceSection = info.FirstOrDefault(section => section.Key == "Keyspace");
            
            var totalKeys = 0L;
            var expiredKeys = 0L;
            var evictedKeys = 0L;
            var uptime = TimeSpan.Zero;
            
            if (statsSection != null)
            {
                foreach (var item in statsSection)
                {
                    if (item.Key == "expired_keys" && long.TryParse(item.Value, out var expired))
                        expiredKeys = expired;
                    else if (item.Key == "evicted_keys" && long.TryParse(item.Value, out var evicted))
                        evictedKeys = evicted;
                }
            }
            
            if (serverSection != null)
            {
                var uptimeItem = serverSection.FirstOrDefault(item => item.Key == "uptime_in_seconds");
                if (!uptimeItem.Equals(default(KeyValuePair<string, string>)) && int.TryParse(uptimeItem.Value, out var uptimeSeconds))
                {
                    uptime = TimeSpan.FromSeconds(uptimeSeconds);
                }
            }
            
            if (keyspaceSection != null)
            {
                var dbKey = $"db{_options.Database}";
                var dbInfo = keyspaceSection.FirstOrDefault(item => item.Key == dbKey);
                if (!dbInfo.Equals(default(KeyValuePair<string, string>)))
                {
                    // db0:keys=2,expires=0,avg_ttl=0 형식 파싱
                    var parts = dbInfo.Value.Split(',');
                    var keysPart = parts.FirstOrDefault(p => p.StartsWith("keys="));
                    if (keysPart != null && long.TryParse(keysPart.Substring(5), out var keys))
                    {
                        totalKeys = keys;
                    }
                }
            }
            
            return new RedisInvalidationStatistics
            {
                ProviderName = Name,
                Type = Type,
                TotalKeys = totalKeys,
                DatabaseSize = totalKeys, // Redis에서는 동일
                ExpiredKeys = expiredKeys,
                EvictedKeys = evictedKeys,
                HitRatio = -1, // Redis INFO에서는 제공되지 않음
                Uptime = uptime,
                LastAccess = DateTimeOffset.UtcNow,
                AdditionalMetrics = new Dictionary<string, object>
                {
                    ["database_number"] = _options.Database,
                    ["connection_string"] = _connectionMultiplexer.Configuration,
                    ["is_connected"] = IsConnected,
                    ["scan_page_size"] = _options.ScanPageSize,
                    ["use_transaction"] = _options.UseTransaction
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Redis statistics");
            throw;
        }
    }

    private IServer GetServer()
    {
        var endpoints = _connectionMultiplexer.GetEndPoints();
        if (!endpoints.Any())
        {
            throw new InvalidOperationException("No Redis endpoints available");
        }
        
        var server = _connectionMultiplexer.GetServer(endpoints.First());
        if (!server.IsConnected)
        {
            throw new InvalidOperationException("Redis server is not connected");
        }
        
        return server;
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            // ConnectionMultiplexer는 DI 컨테이너에서 관리되므로 여기서는 Dispose하지 않음
            _disposed = true;
            _logger.LogDebug("RedisInvalidationProvider disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during RedisInvalidationProvider disposal");
        }
    }
}

/// <summary>
/// Redis 무효화 제공자 옵션
/// </summary>
public class RedisInvalidationOptions
{
    /// <summary>제공자 이름</summary>
    public string ProviderName { get; set; } = "Redis";
    
    /// <summary>Redis 데이터베이스 번호</summary>
    public int Database { get; set; } = 0;
    
    /// <summary>키 스캔 시 페이지 크기</summary>
    public int ScanPageSize { get; set; } = 1000;
    
    /// <summary>스캔 결과 최대 개수</summary>
    public int MaxScanResults { get; set; } = 10000;
    
    /// <summary>배치 작업에서 트랜잭션 사용 여부</summary>
    public bool UseTransaction { get; set; } = true;
    
    /// <summary>헬스체크 키 접두사</summary>
    public string HealthCheckKeyPrefix { get; set; } = "__athena_health_check_";
    
    /// <summary>연결 시간 초과</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>명령 시간 초과</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
