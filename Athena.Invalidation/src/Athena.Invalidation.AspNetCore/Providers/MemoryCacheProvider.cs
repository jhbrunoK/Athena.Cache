using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Athena.Invalidation.AspNetCore.Providers;

/// <summary>
/// Microsoft.Extensions.Caching.Memory 용 캐시 프로바이더
/// </summary>
public class MemoryCacheProvider(IMemoryCache memoryCache, ILogger<MemoryCacheProvider> logger)
    : ICacheProvider, IDisposable
{
    private readonly IMemoryCache _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
    private readonly ILogger<MemoryCacheProvider> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<string, DateTimeOffset> _keyRegistry = new();
    private readonly object _lock = new();
    
    private long _hitCount = 0;
    private long _missCount = 0;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;

    public string ProviderName => "Memory";
    public CacheProviderType ProviderType => CacheProviderType.Memory;

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = _memoryCache.TryGetValue(key, out _);
            return Task.FromResult(exists);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check existence of key '{Key}'", key);
            return Task.FromResult(false);
        }
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = _memoryCache.TryGetValue(key, out _);
            if (exists)
            {
                _memoryCache.Remove(key);
                _keyRegistry.TryRemove(key, out _);
                _logger.LogDebug("Removed key '{Key}' from memory cache", key);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove key '{Key}'", key);
            return Task.FromResult(false);
        }
    }

    public Task<int> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var removedCount = 0;
        
        foreach (var key in keys)
        {
            try
            {
                var exists = _memoryCache.TryGetValue(key, out _);
                if (exists)
                {
                    _memoryCache.Remove(key);
                    _keyRegistry.TryRemove(key, out _);
                    removedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove key '{Key}'", key);
            }
        }

        _logger.LogDebug("Removed {Count} keys from memory cache", removedCount);
        return Task.FromResult(removedCount);
    }

    public Task<int> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var removedCount = 0;
        
        try
        {
            // 패턴을 정규식으로 변환 (* -> .*, ? -> .)
            var regexPattern = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            var regex = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            
            var keysToRemove = _keyRegistry.Keys
                .Where(key => regex.IsMatch(key))
                .ToList();

            foreach (var key in keysToRemove)
            {
                try
                {
                    _memoryCache.Remove(key);
                    _keyRegistry.TryRemove(key, out _);
                    removedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove key '{Key}' during pattern removal", key);
                }
            }

            _logger.LogDebug("Removed {Count} keys matching pattern '{Pattern}'", removedCount, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove keys by pattern '{Pattern}'", pattern);
        }

        return Task.FromResult(removedCount);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new MemoryCacheEntryOptions();
            
            if (expiration.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = expiration.Value;
            }

            // 만료 시 키 레지스트리에서도 제거
            options.RegisterPostEvictionCallback((k, v, reason, state) =>
            {
                _keyRegistry.TryRemove(k.ToString()!, out _);
            });

            _memoryCache.Set(key, value, options);
            _keyRegistry.TryAdd(key, DateTimeOffset.UtcNow);
            
            _logger.LogDebug("Set key '{Key}' in memory cache with expiration {Expiration}", key, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set key '{Key}'", key);
        }

        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_memoryCache.TryGetValue(key, out var value))
            {
                Interlocked.Increment(ref _hitCount);
                _logger.LogDebug("Cache hit for key '{Key}'", key);
                return Task.FromResult((T?)value);
            }
            else
            {
                Interlocked.Increment(ref _missCount);
                _logger.LogDebug("Cache miss for key '{Key}'", key);
                return Task.FromResult<T?>(default);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _missCount);
            _logger.LogWarning(ex, "Failed to get key '{Key}'", key);
            return Task.FromResult<T?>(default);
        }
    }

    public Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var stats = new CacheStatistics
        {
            HitCount = _hitCount,
            MissCount = _missCount,
            TotalKeys = _keyRegistry.Count,
            Uptime = DateTimeOffset.UtcNow - _startTime
        };

        return Task.FromResult(stats);
    }

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 간단한 헬스체크 - 테스트 키 설정 및 조회
            var testKey = $"health_check_{Guid.NewGuid():N}";
            var testValue = "test";
            
            _memoryCache.Set(testKey, testValue, TimeSpan.FromSeconds(1));
            var retrieved = _memoryCache.TryGetValue(testKey, out var value);
            _memoryCache.Remove(testKey);
            
            var isHealthy = retrieved && testValue.Equals(value);
            return Task.FromResult(isHealthy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for MemoryCacheProvider");
            return Task.FromResult(false);
        }
    }

    public void Dispose()
    {
        _keyRegistry.Clear();
        _logger.LogInformation("MemoryCacheProvider disposed");
    }
}
