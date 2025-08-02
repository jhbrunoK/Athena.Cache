using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using MessagePack;

namespace Athena.Cache.Core.Implementations;

/// <summary>
/// MemoryCache 기반 Athena 캐시 구현체
/// </summary>
public class MemoryCacheProvider(
    IMemoryCache memoryCache,
    AthenaCacheOptions options,
    ILogger<MemoryCacheProvider> logger)
    : IAthenaCache
{
    private readonly ConcurrentDictionary<string, DateTime> _keyRegistry = new();
    private readonly CacheStatistics _statistics = new();
    private readonly DateTime _startTime = DateTime.UtcNow;

    // 통계를 위한 필드 (Interlocked 사용을 위해)
    private long _hitCount = 0;
    private long _missCount = 0;

    /// <summary>
    /// 캐시에서 값 조회
    /// </summary>
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = memoryCache.TryGetValue(key, out var cachedValue);

            if (exists)
            {
                Interlocked.Increment(ref _hitCount);

                if (options.Logging.LogCacheHitMiss)
                {
                    logger.LogDebug("Cache HIT for key: {CacheKey}", key);
                }

                if (cachedValue is T typedValue)
                {
                    return Task.FromResult<T?>(typedValue);
                }

                // MessagePack 역직렬화 시도
                if (cachedValue is byte[] messagePackValue && messagePackValue.Length > 0)
                {
                    try
                    {
                        var deserializedValue = MessagePackSerializer.Deserialize<T>(messagePackValue);
                        return Task.FromResult<T?>(deserializedValue);
                    }
                    catch (MessagePackSerializationException ex)
                    {
                        logger.LogWarning(ex, "Failed to deserialize cached value for key: {CacheKey}", key);
                    }
                }
                
                // 하위 호환성을 위한 JSON 역직렬화 시도
                if (cachedValue is string jsonValue && !string.IsNullOrEmpty(jsonValue))
                {
                    try
                    {
                        var deserializedValue = JsonSerializer.Deserialize<T>(jsonValue);
                        return Task.FromResult(deserializedValue);
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "Failed to deserialize cached JSON value for key: {CacheKey}", key);
                    }
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

            return Task.FromResult<T?>(default);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting value from cache for key: {CacheKey}", key);

            if (options.ErrorHandling.SilentFallback)
            {
                return Task.FromResult<T?>(default);
            }

            throw;
        }
    }

    /// <summary>
    /// 캐시에 값 저장
    /// </summary>
    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (value == null)
            {
                return Task.CompletedTask;
            }

            var expirationTime = expiration ?? TimeSpan.FromMinutes(options.DefaultExpirationMinutes);

            var entryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expirationTime,
                Priority = CacheItemPriority.Normal
            };

            // 만료 시 키 레지스트리에서도 제거
            entryOptions.RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
            {
                if (evictedKey is string keyStr)
                {
                    _keyRegistry.TryRemove(keyStr, out _);

                    if (options.Logging.LogCacheHitMiss)
                    {
                        logger.LogDebug("Cache key evicted: {CacheKey}, Reason: {Reason}", keyStr, reason);
                    }
                }
            });

            // 복잡한 객체는 MessagePack 직렬화하여 저장 (성능 최적화)
            object cacheValue = value;
            if (typeof(T) != typeof(string) && !typeof(T).IsPrimitive && !typeof(T).IsValueType)
            {
                try
                {
                    cacheValue = MessagePackSerializer.Serialize(value);
                }
                catch (MessagePackSerializationException)
                {
                    // MessagePack 직렬화 실패 시 JSON으로 fallback
                    cacheValue = JsonSerializer.Serialize(value);
                    logger.LogDebug("Fallback to JSON serialization for key: {CacheKey}", key);
                }
            }

            memoryCache.Set(key, cacheValue, entryOptions);
            _keyRegistry[key] = DateTime.UtcNow;

            if (options.Logging.LogCacheHitMiss)
            {
                logger.LogDebug("Cache SET for key: {CacheKey}, Expiration: {Expiration}mins",
                    key, expirationTime.TotalMinutes);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting value in cache for key: {CacheKey}", key);

            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 캐시에서 특정 키 삭제
    /// </summary>
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            memoryCache.Remove(key);
            _keyRegistry.TryRemove(key, out _);

            if (options.Logging.LogInvalidation)
            {
                logger.LogDebug("Cache key removed: {CacheKey}", key);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing cache key: {CacheKey}", key);

            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 패턴에 맞는 캐시 키들 삭제 (와일드카드 지원)
    /// MemoryCache는 키 스캔이 제한적이므로 키 레지스트리 활용
    /// </summary>
    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            var regexPattern = ConvertWildcardToRegex(pattern);
            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var keysToRemove = _keyRegistry.Keys
                .Where(key => regex.IsMatch(key))
                .ToList();

            foreach (var key in keysToRemove)
            {
                memoryCache.Remove(key);
                _keyRegistry.TryRemove(key, out _);
            }

            if (options.Logging.LogInvalidation)
            {
                logger.LogInformation("Removed {Count} cache keys matching pattern: {Pattern}",
                    keysToRemove.Count, pattern);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing cache keys by pattern: {Pattern}", pattern);

            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 캐시 키 존재 여부 확인
    /// </summary>
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = _keyRegistry.ContainsKey(key) && memoryCache.TryGetValue(key, out _);
            return Task.FromResult(exists);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if cache key exists: {CacheKey}", key);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 여러 키를 배치로 조회
    /// </summary>
    public async Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, T?>();

        foreach (var key in keys)
        {
            var value = await GetAsync<T>(key, cancellationToken);
            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// 여러 키-값 쌍을 배치로 저장
    /// </summary>
    public async Task SetManyAsync<T>(Dictionary<string, T> keyValuePairs, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var tasks = keyValuePairs.Select(kvp => SetAsync(kvp.Key, kvp.Value, expiration, cancellationToken));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 여러 키를 배치로 삭제
    /// </summary>
    public async Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var tasks = keys.Select(key => RemoveAsync(key, cancellationToken));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 캐시 통계 정보 조회
    /// </summary>
    public Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var statistics = new CacheStatistics
        {
            TotalKeys = _keyRegistry.Count,
            HitCount = Interlocked.Read(ref _hitCount),
            MissCount = Interlocked.Read(ref _missCount),
            Uptime = DateTime.UtcNow - _startTime,
            MemoryUsage = GC.GetTotalMemory(false)
        };

        return Task.FromResult(statistics);
    }

    /// <summary>
    /// 와일드카드 패턴을 정규식으로 변환
    /// </summary>
    private static string ConvertWildcardToRegex(string wildcard)
    {
        // * -> .* (0개 이상의 문자)
        // ? -> . (1개 문자)
        var pattern = wildcard
            .Replace(".", "\\.")
            .Replace("*", ".*")
            .Replace("?", ".");

        return $"^{pattern}$";
    }
}
