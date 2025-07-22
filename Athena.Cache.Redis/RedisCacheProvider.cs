using System.Text.Json;
using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Enums;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Athena.Cache.Redis;

/// <summary>
/// Redis 기반 Athena 캐시 구현체
/// </summary>
public class RedisCacheProvider(
    IConnectionMultiplexer redis,
    AthenaCacheOptions options,
    RedisCacheOptions redisOptions,
    ILogger<RedisCacheProvider> logger)
    : IAthenaCache, IDisposable
{
    private readonly IDatabase _database = redis.GetDatabase(redisOptions.DatabaseId);
    private readonly ISubscriber _subscriber = redis.GetSubscriber();
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private bool _disposed = false;

    // 통계를 위한 필드
    private long _hitCount = 0;
    private long _missCount = 0;
    private readonly DateTime _startTime = DateTime.UtcNow;

    /// <summary>
    /// 캐시에서 값 조회
    /// </summary>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureConnectionAsync();

            var redisValue = await _database.StringGetAsync(AddKeyPrefix(key));

            if (redisValue.HasValue)
            {
                Interlocked.Increment(ref _hitCount);

                if (options.Logging.LogCacheHitMiss)
                {
                    logger.LogDebug("Cache HIT for key: {CacheKey}", key);
                }

                // JSON 역직렬화
                var jsonString = redisValue.ToString();
                if (!string.IsNullOrEmpty(jsonString))
                {
                    return JsonSerializer.Deserialize<T>(jsonString, redisOptions.JsonOptions);
                }
            }
            else
            {
                Interlocked.Increment(ref _missCount);

                if (options.Logging.LogCacheHitMiss)
                {
                    logger.LogDebug("Cache MISS for key: {CacheKey}", key);
                }
            }

            return default(T);
        }
        catch (RedisException ex)
        {
            logger.LogError(ex, "Redis error getting value for key: {CacheKey}", key);
            await HandleRedisConnectionIssue();

            if (options.ErrorHandling.SilentFallback)
            {
                return default(T);
            }

            throw;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize cached value for key: {CacheKey}", key);
            return default(T);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting value from cache for key: {CacheKey}", key);

            if (options.ErrorHandling.SilentFallback)
            {
                return default(T);
            }

            throw;
        }
    }

    /// <summary>
    /// 캐시에 값 저장
    /// </summary>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (value == null) return;

            await EnsureConnectionAsync();

            var expirationTime = expiration ?? TimeSpan.FromMinutes(options.DefaultExpirationMinutes);

            // JSON 직렬화
            var jsonString = JsonSerializer.Serialize(value, redisOptions.JsonOptions);

            // Redis에 저장
            var success = await _database.StringSetAsync(
                AddKeyPrefix(key),
                jsonString,
                expirationTime);

            if (success && options.Logging.LogCacheHitMiss)
            {
                logger.LogDebug("Cache SET for key: {CacheKey}, Expiration: {Expiration}mins",
                    key, expirationTime.TotalMinutes);
            }
        }
        catch (RedisException ex)
        {
            logger.LogError(ex, "Redis error setting value for key: {CacheKey}", key);
            await HandleRedisConnectionIssue();

            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to serialize value for key: {CacheKey}", key);

            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting value in cache for key: {CacheKey}", key);

            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 캐시에서 특정 키 삭제
    /// </summary>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureConnectionAsync();

            var deleted = await _database.KeyDeleteAsync(AddKeyPrefix(key));

            if (deleted && options.Logging.LogInvalidation)
            {
                logger.LogDebug("Cache key removed: {CacheKey}", key);
            }
        }
        catch (RedisException ex)
        {
            logger.LogError(ex, "Redis error removing cache key: {CacheKey}", key);
            await HandleRedisConnectionIssue();

            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing cache key: {CacheKey}", key);

            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 패턴에 맞는 캐시 키들 삭제 (Redis SCAN 사용)
    /// </summary>
    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureConnectionAsync();

            var server = GetServer();
            if (server == null)
            {
                logger.LogWarning("No Redis server available for pattern deletion");
                return;
            }

            var prefixedPattern = AddKeyPrefix(pattern);
            var keys = server.KeysAsync(pattern: prefixedPattern, database: redisOptions.DatabaseId);
            var deletedCount = 0;

            await foreach (var key in keys)
            {
                await _database.KeyDeleteAsync(key);
                deletedCount++;

                // 배치 처리로 성능 최적화
                if (deletedCount % redisOptions.BatchSize == 0)
                {
                    await Task.Delay(10, cancellationToken); // 작은 딜레이로 Redis 부하 감소
                }
            }

            if (options.Logging.LogInvalidation)
            {
                logger.LogInformation("Removed {Count} cache keys matching pattern: {Pattern}",
                    deletedCount, pattern);
            }
        }
        catch (RedisException ex)
        {
            logger.LogError(ex, "Redis error removing keys by pattern: {Pattern}", pattern);
            await HandleRedisConnectionIssue();

            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing cache keys by pattern: {Pattern}", pattern);

            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 캐시 키 존재 여부 확인
    /// </summary>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureConnectionAsync();
            return await _database.KeyExistsAsync(AddKeyPrefix(key));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if cache key exists: {CacheKey}", key);
            return false;
        }
    }

    /// <summary>
    /// 여러 키를 배치로 조회 (Redis MGET 사용)
    /// </summary>
    public async Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, T?>();
        var keyList = keys.ToList();

        if (keyList.Count == 0) return result;

        try
        {
            await EnsureConnectionAsync();

            // Redis 키에 접두사 추가
            var redisKeys = keyList.Select(key => (RedisKey)AddKeyPrefix(key)).ToArray();

            // MGET으로 배치 조회
            var values = await _database.StringGetAsync(redisKeys);

            for (var i = 0; i < keyList.Count; i++)
            {
                var originalKey = keyList[i];
                var redisValue = values[i];

                if (redisValue.HasValue)
                {
                    try
                    {
                        var deserializedValue = JsonSerializer.Deserialize<T>(redisValue!, redisOptions.JsonOptions);
                        result[originalKey] = deserializedValue;
                        Interlocked.Increment(ref _hitCount);
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "Failed to deserialize cached value for key: {CacheKey}", originalKey);
                        result[originalKey] = default(T);
                    }
                }
                else
                {
                    result[originalKey] = default(T);
                    Interlocked.Increment(ref _missCount);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting multiple values from cache");

            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }

        return result;
    }

    /// <summary>
    /// 여러 키-값 쌍을 배치로 저장 (Redis MSET 사용)
    /// </summary>
    public async Task SetManyAsync<T>(Dictionary<string, T> keyValuePairs, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        if (keyValuePairs.Count == 0) return;

        try
        {
            await EnsureConnectionAsync();

            var expirationTime = expiration ?? TimeSpan.FromMinutes(options.DefaultExpirationMinutes);

            // Redis key-value 쌍 준비
            var redisKeyValues = new List<KeyValuePair<RedisKey, RedisValue>>();

            foreach (var kvp in keyValuePairs)
            {
                if (kvp.Value != null)
                {
                    var jsonString = JsonSerializer.Serialize(kvp.Value, redisOptions.JsonOptions);
                    redisKeyValues.Add(new KeyValuePair<RedisKey, RedisValue>(AddKeyPrefix(kvp.Key), jsonString));
                }
            }

            if (redisKeyValues.Count > 0)
            {
                // MSET으로 배치 저장
                await _database.StringSetAsync(redisKeyValues.ToArray());

                // 만료 시간 설정 (개별적으로 처리해야 함)
                var expireTasks = redisKeyValues.Select(kvp =>
                    _database.KeyExpireAsync(kvp.Key, expirationTime));

                await Task.WhenAll(expireTasks);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting multiple values in cache");

            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 여러 키를 배치로 삭제 (Redis DEL 사용)
    /// </summary>
    public async Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0) return;

        try
        {
            await EnsureConnectionAsync();

            var redisKeys = keyList.Select(key => (RedisKey)AddKeyPrefix(key)).ToArray();
            var deletedCount = await _database.KeyDeleteAsync(redisKeys);

            if (options.Logging.LogInvalidation)
            {
                logger.LogDebug("Removed {DeletedCount}/{TotalCount} cache keys", deletedCount, keyList.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing multiple cache keys");

            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 캐시 통계 정보 조회
    /// </summary>
    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var statistics = new CacheStatistics
        {
            HitCount = Interlocked.Read(ref _hitCount),
            MissCount = Interlocked.Read(ref _missCount),
            Uptime = DateTime.UtcNow - _startTime
        };

        try
        {
            await EnsureConnectionAsync();

            var server = GetServer();
            if (server != null)
            {
                // Redis 메모리 정보 조회
                var memoryInfo = await server.InfoAsync("memory");

                // 메모리 사용량 파싱
                if (memoryInfo != null)
                {
                    var memorySection = memoryInfo.FirstOrDefault(section => section.Key == "memory");
                    if (memorySection != null)
                    {
                        var usedMemoryEntry = memorySection.FirstOrDefault(entry => entry.Key == "used_memory");
                        if (!usedMemoryEntry.Equals(default(KeyValuePair<string, string>)) &&
                            long.TryParse(usedMemoryEntry.Value, out var memoryUsage))
                        {
                            statistics.MemoryUsage = memoryUsage;
                        }
                    }
                }

                // 키 개수 조회 (근사치)
                try
                {
                    statistics.TotalKeys = await server.DatabaseSizeAsync(redisOptions.DatabaseId);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to get database size");
                    statistics.TotalKeys = 0;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get Redis statistics");
        }

        return statistics;
    }

    /// <summary>
    /// 키에 네임스페이스 접두사 추가
    /// </summary>
    private string AddKeyPrefix(string key)
    {
        if (string.IsNullOrEmpty(redisOptions.KeyPrefix))
        {
            return key;
        }

        return $"{redisOptions.KeyPrefix}:{key}";
    }

    /// <summary>
    /// Redis 연결 확인 및 재연결
    /// </summary>
    private async Task EnsureConnectionAsync()
    {
        if (!redis.IsConnected)
        {
            await _connectionSemaphore.WaitAsync();
            try
            {
                if (!redis.IsConnected)
                {
                    logger.LogWarning("Redis connection lost, attempting to reconnect...");
                    // ConnectionMultiplexer는 자동 재연결을 시도함
                    await Task.Delay(100); // 짧은 대기
                }
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// Redis 연결 문제 처리
    /// </summary>
    private async Task HandleRedisConnectionIssue()
    {
        logger.LogError("Redis connection issue detected");

        if (options.ErrorHandling.CustomErrorHandler != null)
        {
            await options.ErrorHandling.CustomErrorHandler(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis connection failed"));
        }
    }

    /// <summary>
    /// Redis 서버 인스턴스 가져오기
    /// </summary>
    private IServer? GetServer()
    {
        try
        {
            var endpoints = redis.GetEndPoints();
            return endpoints.Length > 0 ? redis.GetServer(endpoints[0]) : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get Redis server instance");
            return null;
        }
    }

    /// <summary>
    /// 리소스 정리
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _connectionSemaphore?.Dispose();
            _disposed = true;
        }
    }
}