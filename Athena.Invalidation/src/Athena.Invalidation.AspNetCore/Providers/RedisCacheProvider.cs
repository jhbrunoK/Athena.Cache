using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Text.RegularExpressions;
using StackExchange.Redis;

namespace Athena.Invalidation.AspNetCore.Providers;

/// <summary>
/// Redis 용 캐시 프로바이더 (StackExchange.Redis 기반)
/// </summary>
public class RedisCacheProvider : ICacheProvider, IDisposable
{
    private readonly IDistributedCache _distributedCache;
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ILogger<RedisCacheProvider> _logger;
    
    private long _hitCount = 0;
    private long _missCount = 0;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;

    public string ProviderName => "Redis";
    public CacheProviderType ProviderType => CacheProviderType.Distributed;

    public RedisCacheProvider(
        IDistributedCache distributedCache,
        IConnectionMultiplexer redis,
        ILogger<RedisCacheProvider> logger)
    {
        _distributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _database = _redis.GetDatabase();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await _database.KeyExistsAsync(key);
            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check existence of key '{Key}'", key);
            return false;
        }
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var removed = await _database.KeyDeleteAsync(key);
            if (removed)
            {
                _logger.LogDebug("Removed key '{Key}' from Redis cache", key);
            }
            return removed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove key '{Key}'", key);
            return false;
        }
    }

    public async Task<int> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        try
        {
            var redisKeys = keys.Select(k => new RedisKey(k)).ToArray();
            var removedCount = await _database.KeyDeleteAsync(redisKeys);
            
            _logger.LogDebug("Removed {Count} keys from Redis cache", removedCount);
            return (int)removedCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove multiple keys");
            return 0;
        }
    }

    public async Task<int> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = server.Keys(pattern: pattern).ToArray();
            
            if (keys.Length == 0)
            {
                return 0;
            }

            var removedCount = await _database.KeyDeleteAsync(keys);
            _logger.LogDebug("Removed {Count} keys matching pattern '{Pattern}'", removedCount, pattern);
            
            return (int)removedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove keys by pattern '{Pattern}'", pattern);
            return 0;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(value);
            var options = new DistributedCacheEntryOptions();
            
            if (expiration.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = expiration.Value;
            }

            await _distributedCache.SetStringAsync(key, json, options, cancellationToken);
            _logger.LogDebug("Set key '{Key}' in Redis cache with expiration {Expiration}", key, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set key '{Key}'", key);
        }
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _distributedCache.GetStringAsync(key, cancellationToken);
            
            if (json != null)
            {
                Interlocked.Increment(ref _hitCount);
                _logger.LogDebug("Cache hit for key '{Key}'", key);
                
                var value = JsonSerializer.Deserialize<T>(json);
                return value;
            }
            else
            {
                Interlocked.Increment(ref _missCount);
                _logger.LogDebug("Cache miss for key '{Key}'", key);
                return default(T);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _missCount);
            _logger.LogWarning(ex, "Failed to get key '{Key}'", key);
            return default(T);
        }
    }

    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var info = await server.InfoAsync("keyspace");
            
            // Redis keyspace 정보에서 키 개수 파싱
            var totalKeys = 0L;
            foreach (var line in info.ToString().Split('\n'))
            {
                if (line.StartsWith("db") && line.Contains("keys="))
                {
                    var keysPart = line.Split(',').FirstOrDefault(p => p.Contains("keys="));
                    if (keysPart != null)
                    {
                        var keyValueParts = keysPart.Split('=');
                        if (keyValueParts.Length > 1 && long.TryParse(keyValueParts[1], out var dbKeys))
                        {
                            totalKeys += dbKeys;
                        }
                    }
                }
            }

            var stats = new CacheStatistics
            {
                HitCount = _hitCount,
                MissCount = _missCount,
                TotalKeys = totalKeys,
                Uptime = DateTimeOffset.UtcNow - _startTime
            };

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Redis statistics");
            
            return new CacheStatistics
            {
                HitCount = _hitCount,
                MissCount = _missCount,
                TotalKeys = 0,
                Uptime = DateTimeOffset.UtcNow - _startTime
            };
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Redis 연결 상태 확인
            if (!_redis.IsConnected)
            {
                return false;
            }

            // 간단한 헬스체크 - ping 명령
            var pong = await _database.PingAsync();
            var isHealthy = pong.TotalMilliseconds < 1000; // 1초 이내 응답
            
            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for RedisCacheProvider");
            return false;
        }
    }

    public void Dispose()
    {
        // IConnectionMultiplexer는 일반적으로 DI 컨테이너에서 관리되므로 여기서 dispose하지 않음
        _logger.LogInformation("RedisCacheProvider disposed");
    }
}