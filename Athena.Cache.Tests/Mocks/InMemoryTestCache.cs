using Athena.Cache.Core.Abstractions;

namespace Athena.Cache.Tests.Mocks;

/// <summary>
/// 테스트용 인메모리 캐시 구현체
/// </summary>
public class InMemoryTestCache : IAthenaCache
{
    private readonly Dictionary<string, (object Value, DateTime ExpiredAt)> _cache = new();
    private long _hitCount = 0;
    private long _missCount = 0;

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var cacheItem))
        {
            if (DateTime.UtcNow < cacheItem.ExpiredAt)
            {
                Interlocked.Increment(ref _hitCount);

                if (cacheItem.Value is T typedValue)
                {
                    return Task.FromResult<T?>(typedValue);
                }

                // JSON 문자열인 경우 역직렬화 시도
                if (cacheItem.Value is string jsonString && typeof(T) != typeof(string))
                {
                    try
                    {
                        var deserialized = System.Text.Json.JsonSerializer.Deserialize<T>(jsonString);
                        return Task.FromResult(deserialized);
                    }
                    catch
                    {
                        // 역직렬화 실패 시 기본값 반환
                    }
                }
            }
            else
            {
                // 만료된 항목 제거
                _cache.Remove(key);
            }
        }

        Interlocked.Increment(ref _missCount);
        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        if (value == null) return Task.CompletedTask;

        var expiredAt = DateTime.UtcNow.Add(expiration ?? TimeSpan.FromMinutes(30));

        // 복합 객체는 JSON으로 직렬화
        object storeValue = value;
        if (typeof(T) != typeof(string) && !typeof(T).IsPrimitive && !typeof(T).IsValueType)
        {
            storeValue = System.Text.Json.JsonSerializer.Serialize(value);
        }

        _cache[key] = (storeValue, expiredAt);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var regex = new System.Text.RegularExpressions.Regex(
            pattern.Replace("*", ".*").Replace("?", "."),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var keysToRemove = _cache.Keys.Where(key => regex.IsMatch(key)).ToList();

        foreach (var key in keysToRemove)
        {
            _cache.Remove(key);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var exists = _cache.ContainsKey(key) && DateTime.UtcNow < _cache[key].ExpiredAt;
        return Task.FromResult(exists);
    }

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

    public async Task SetManyAsync<T>(Dictionary<string, T> keyValuePairs, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        foreach (var kvp in keyValuePairs)
        {
            await SetAsync(kvp.Key, kvp.Value, expiration, cancellationToken);
        }
    }

    public async Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
        {
            await RemoveAsync(key, cancellationToken);
        }
    }

    public Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        // 만료된 항목 정리
        var expiredKeys = _cache.Where(kvp => DateTime.UtcNow >= kvp.Value.ExpiredAt)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.Remove(key);
        }

        var statistics = new CacheStatistics
        {
            TotalKeys = _cache.Count,
            HitCount = Interlocked.Read(ref _hitCount),
            MissCount = Interlocked.Read(ref _missCount),
            Uptime = TimeSpan.FromMinutes(1), // 테스트용 고정값
            MemoryUsage = _cache.Count * 100 // 대략적인 추정값
        };

        return Task.FromResult(statistics);
    }
}