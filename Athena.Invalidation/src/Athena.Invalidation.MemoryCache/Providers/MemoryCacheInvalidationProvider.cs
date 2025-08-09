using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Athena.Invalidation.MemoryCache.Abstractions;

namespace Athena.Invalidation.MemoryCache.Providers;

/// <summary>
/// Memory Cache 기반 무효화 제공자
/// </summary>
public class MemoryCacheInvalidationProvider : IMemoryCacheInvalidationProvider, IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<MemoryCacheInvalidationProvider> _logger;
    private readonly MemoryCacheInvalidationOptions _options;
    
    // 키 추적 및 태그 시스템
    private readonly ConcurrentDictionary<string, HashSet<string>> _keysByTag = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _tagsByKey = new();
    private readonly ConcurrentHashSet<string> _trackedKeys = new();
    
    // 통계 추적
    private long _hitCount = 0;
    private long _missCount = 0;
    private long _evictionCount = 0;
    private readonly DateTimeOffset _createdTime = DateTimeOffset.UtcNow;
    
    private volatile bool _disposed = false;

    public string Name { get; }
    public CacheProviderType Type => CacheProviderType.Memory;
    public int Count => _trackedKeys.Count;

    public MemoryCacheInvalidationProvider(
        IMemoryCache memoryCache,
        ILogger<MemoryCacheInvalidationProvider> logger,
        IOptions<MemoryCacheInvalidationOptions> options)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new MemoryCacheInvalidationOptions();
        
        Name = _options.ProviderName;
    }

    public Task InvalidateAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationProvider));
        
        try
        {
            _memoryCache.Remove(key);
            RemoveKeyTracking(key);
            
            _logger.LogDebug("Invalidated memory cache key: {Key}", key);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate memory cache key: {Key}", key);
            throw;
        }
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationProvider));
        
        try
        {
            var matchingKeys = await GetKeysByPatternAsync(pattern, cancellationToken);
            var keyList = matchingKeys.ToList();
            
            if (!keyList.Any())
            {
                _logger.LogDebug("No keys found matching pattern: {Pattern}", pattern);
                return;
            }

            await InvalidateBatchAsync(keyList, cancellationToken);
            _logger.LogInformation("Invalidated {Count} memory cache keys matching pattern: {Pattern}", keyList.Count, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate by pattern: {Pattern}", pattern);
            throw;
        }
    }

    public async Task InvalidateByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationProvider));
        
        try
        {
            if (!_keysByTag.TryGetValue(tag, out var keys) || !keys.Any())
            {
                _logger.LogDebug("No keys found for tag: {Tag}", tag);
                return;
            }

            var keyList = keys.ToList();
            await InvalidateBatchAsync(keyList, cancellationToken);
            
            // 태그 정보도 정리
            _keysByTag.TryRemove(tag, out _);
            
            _logger.LogInformation("Invalidated {Count} memory cache keys by tag: {Tag}", keyList.Count, tag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate by tag: {Tag}", tag);
            throw;
        }
    }

    public Task InvalidateBatchAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationProvider));
        
        var keyList = keys.ToList();
        if (!keyList.Any()) return Task.CompletedTask;

        try
        {
            foreach (var key in keyList)
            {
                _memoryCache.Remove(key);
                RemoveKeyTracking(key);
            }
            
            _logger.LogDebug("Invalidated {Count} memory cache keys in batch", keyList.Count);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate batch of {Count} keys", keyList.Count);
            throw;
        }
    }

    public Task InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationProvider));
        
        try
        {
            if (!_options.AllowClearAll)
            {
                _logger.LogWarning("Clear all operation is disabled for safety");
                throw new InvalidOperationException("Clear all operation is disabled for safety");
            }
            
            // IMemoryCache는 직접적인 Clear 메서드를 제공하지 않음
            // 추적된 키들만 삭제
            var keysToRemove = _trackedKeys.ToList();
            
            foreach (var key in keysToRemove)
            {
                _memoryCache.Remove(key);
            }
            
            // 추적 정보 초기화
            _keysByTag.Clear();
            _tagsByKey.Clear();
            _trackedKeys.Clear();
            
            // Successfully cleared tracked cache entries
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear all memory cache entries");
            throw;
        }
    }

    public Task<IEnumerable<string>> GetKeysByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationProvider));
        
        try
        {
            var regex = CreatePatternRegex(pattern);
            var matchingKeys = _trackedKeys.Where(key => regex.IsMatch(key)).ToList();
            
            return Task.FromResult<IEnumerable<string>>(matchingKeys);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get keys by pattern: {Pattern}", pattern);
            throw;
        }
    }

    public Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationProvider));
        
        try
        {
            var exists = _trackedKeys.Contains(key);
            if (exists)
            {
                Interlocked.Increment(ref _hitCount);
            }
            else
            {
                Interlocked.Increment(ref _missCount);
            }
            
            return Task.FromResult(exists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check key existence: {Key}", key);
            throw;
        }
    }

    public Task TagKeyAsync(string key, string tag, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationProvider));
        
        try
        {
            // 키를 추적 대상에 추가
            _trackedKeys.Add(key);
            
            // 태그별 키 매핑 추가
            _keysByTag.AddOrUpdate(tag, 
                new HashSet<string> { key },
                (_, existing) => 
                {
                    lock (existing)
                    {
                        existing.Add(key);
                        return existing;
                    }
                });
            
            // 키별 태그 매핑 추가
            _tagsByKey.AddOrUpdate(key,
                new HashSet<string> { tag },
                (_, existing) =>
                {
                    lock (existing)
                    {
                        existing.Add(tag);
                        return existing;
                    }
                });
            
            _logger.LogTrace("Tagged key {Key} with tag {Tag}", key, tag);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to tag key {Key} with tag {Tag}", key, tag);
            throw;
        }
    }

    public Task UntagKeyAsync(string key, string tag, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationProvider));
        
        try
        {
            // 태그별 키 매핑에서 키 제거
            if (_keysByTag.TryGetValue(tag, out var keys))
            {
                lock (keys)
                {
                    keys.Remove(key);
                    if (!keys.Any())
                    {
                        _keysByTag.TryRemove(tag, out _);
                    }
                }
            }
            
            // 키별 태그 매핑에서 태그 제거
            if (_tagsByKey.TryGetValue(key, out var tags))
            {
                lock (tags)
                {
                    tags.Remove(tag);
                    if (!tags.Any())
                    {
                        _tagsByKey.TryRemove(key, out _);
                    }
                }
            }
            
            _logger.LogTrace("Untagged key {Key} from tag {Tag}", key, tag);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to untag key {Key} from tag {Tag}", key, tag);
            throw;
        }
    }

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.FromResult(false);
        
        try
        {
            // 간단한 헬스체크를 위해 테스트 키로 set/get/remove 수행
            var testKey = $"{_options.HealthCheckKeyPrefix}{Guid.NewGuid():N}";
            var testValue = DateTimeOffset.UtcNow.ToString();
            
            _memoryCache.Set(testKey, testValue, TimeSpan.FromSeconds(10));
            var retrieved = _memoryCache.Get<string>(testKey);
            _memoryCache.Remove(testKey);
            
            return Task.FromResult(retrieved == testValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Memory cache health check failed");
            return Task.FromResult(false);
        }
    }

    public Task<MemoryCacheInvalidationStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCacheInvalidationProvider));
        
        try
        {
            var statistics = new MemoryCacheInvalidationStatistics
            {
                ProviderName = Name,
                Type = Type,
                TotalKeys = _trackedKeys.Count,
                TotalTags = _keysByTag.Count,
                HitCount = _hitCount,
                MissCount = _missCount,
                HitRatio = _hitCount + _missCount > 0 ? (double)_hitCount / (_hitCount + _missCount) : 0,
                TotalEvictions = _evictionCount,
                LastAccess = DateTimeOffset.UtcNow,
                AdditionalMetrics = new Dictionary<string, object>
                {
                    ["uptime_seconds"] = (DateTimeOffset.UtcNow - _createdTime).TotalSeconds,
                    ["allows_clear_all"] = _options.AllowClearAll,
                    ["pattern_matching_timeout"] = _options.PatternMatchingTimeout.TotalMilliseconds,
                    ["max_tracked_keys"] = _options.MaxTrackedKeys
                }
            };
            
            return Task.FromResult(statistics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get memory cache statistics");
            throw;
        }
    }

    private void RemoveKeyTracking(string key)
    {
        _trackedKeys.TryRemove(key);
        
        // 키와 연관된 태그 정보도 정리
        if (_tagsByKey.TryRemove(key, out var tags))
        {
            foreach (var tag in tags)
            {
                if (_keysByTag.TryGetValue(tag, out var keys))
                {
                    lock (keys)
                    {
                        keys.Remove(key);
                        if (!keys.Any())
                        {
                            _keysByTag.TryRemove(tag, out _);
                        }
                    }
                }
            }
        }
        
        Interlocked.Increment(ref _evictionCount);
    }

    private Regex CreatePatternRegex(string pattern)
    {
        // 와일드카드 패턴을 정규식으로 변환
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        
        return new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, _options.PatternMatchingTimeout);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _keysByTag.Clear();
            _tagsByKey.Clear();
            _trackedKeys.Clear();
            
            _disposed = true;
            _logger.LogDebug("MemoryCacheInvalidationProvider disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during MemoryCacheInvalidationProvider disposal");
        }
    }
}

/// <summary>
/// Thread-safe HashSet 구현
/// </summary>
public class ConcurrentHashSet<T> : IEnumerable<T> where T : notnull
{
    private readonly ConcurrentDictionary<T, byte> _dictionary = new();

    public bool Add(T item) => _dictionary.TryAdd(item, 0);
    public bool Contains(T item) => _dictionary.ContainsKey(item);
    public bool TryRemove(T item) => _dictionary.TryRemove(item, out _);
    public void Clear() => _dictionary.Clear();
    public int Count => _dictionary.Count;
    public IEnumerable<T> ToList() => _dictionary.Keys.ToList();

    public IEnumerator<T> GetEnumerator() => _dictionary.Keys.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Memory Cache 무효화 제공자 옵션
/// </summary>
public class MemoryCacheInvalidationOptions
{
    /// <summary>제공자 이름</summary>
    public string ProviderName { get; set; } = "MemoryCache";
    
    /// <summary>전체 클리어 허용 여부</summary>
    public bool AllowClearAll { get; set; } = false;
    
    /// <summary>헬스체크 키 접두사</summary>
    public string HealthCheckKeyPrefix { get; set; } = "__athena_health_check_";
    
    /// <summary>패턴 매칭 시간 제한</summary>
    public TimeSpan PatternMatchingTimeout { get; set; } = TimeSpan.FromSeconds(5);
    
    /// <summary>최대 추적 키 수</summary>
    public int MaxTrackedKeys { get; set; } = 100000;
    
    /// <summary>태그 기반 무효화 활성화 여부</summary>
    public bool EnableTagging { get; set; } = true;
}