using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Athena.Cache.FusionCache.Implementations;

/// <summary>
/// FusionCache를 사용한 IAthenaCache 구현체
/// FusionCache의 고급 기능(Fail-Safe, Circuit Breaker 등)을 Athena.Cache 인터페이스로 제공
/// </summary>
public class FusionCacheProvider : IAthenaCache
{
    private readonly IFusionCache _fusionCache;
    private readonly ILogger<FusionCacheProvider> _logger;
    private readonly CacheStatistics _statistics = new();
    private readonly DateTime _startTime = DateTime.UtcNow;

    // 통계 및 키 추적을 위한 필드
    private long _hitCount = 0;
    private long _missCount = 0;
    private readonly ConcurrentDictionary<string, DateTime> _keyRegistry = new();

    public FusionCacheProvider(IFusionCache fusionCache, ILogger<FusionCacheProvider> logger)
    {
        _fusionCache = fusionCache ?? throw new ArgumentNullException(nameof(fusionCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 캐시에서 값 조회
    /// </summary>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _fusionCache.GetOrDefaultAsync<T>(key);
            
            if (result != null && !EqualityComparer<T>.Default.Equals(result, default(T)))
            {
                Interlocked.Increment(ref _hitCount);
                _logger.LogDebug("Cache HIT for key: {CacheKey}", key);
                return result;
            }
            else
            {
                Interlocked.Increment(ref _missCount);
                _logger.LogDebug("Cache MISS for key: {CacheKey}", key);
                return default(T);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting value from FusionCache for key: {CacheKey}", key);
            Interlocked.Increment(ref _missCount);
            return default(T);
        }
    }

    /// <summary>
    /// 캐시에 값 저장
    /// </summary>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (value == null)
            {
                _logger.LogWarning("Attempted to cache null value for key: {CacheKey}", key);
                return;
            }

            // FusionCache 옵션 구성
            FusionCacheEntryOptions? options = null;
            if (expiration.HasValue)
            {
                options = new FusionCacheEntryOptions { Duration = expiration.Value };
            }

            if (options != null)
            {
                await _fusionCache.SetAsync(key, value, options);
            }
            else
            {
                await _fusionCache.SetAsync(key, value, TimeSpan.FromMinutes(30));
            }
            
            // 키 레지스트리에 등록 (패턴 삭제를 위해)
            _keyRegistry[key] = DateTime.UtcNow;
            
            _logger.LogDebug("Cache SET for key: {CacheKey}, Expiration: {Expiration}", key, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting value in FusionCache for key: {CacheKey}", key);
            throw;
        }
    }

    /// <summary>
    /// 캐시에서 특정 키 삭제
    /// </summary>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _fusionCache.RemoveAsync(key);
            _keyRegistry.TryRemove(key, out _);
            _logger.LogDebug("Cache key removed: {CacheKey}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache key from FusionCache: {CacheKey}", key);
            throw;
        }
    }

    /// <summary>
    /// 패턴에 맞는 캐시 키들 삭제
    /// FusionCache는 패턴 삭제를 직접 지원하지 않으므로 키 레지스트리를 활용
    /// </summary>
    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            var regexPattern = ConvertWildcardToRegex(pattern);
            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var keysToRemove = _keyRegistry.Keys
                .Where(key => regex.IsMatch(key))
                .ToList();

            // 배치로 삭제 처리
            var removeTasks = keysToRemove.Select(key => RemoveAsync(key, cancellationToken));
            await Task.WhenAll(removeTasks);

            _logger.LogInformation("Removed {Count} cache keys matching pattern: {Pattern}", 
                keysToRemove.Count, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache keys by pattern from FusionCache: {Pattern}", pattern);
            throw;
        }
    }

    /// <summary>
    /// 캐시 키 존재 여부 확인
    /// </summary>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            // FusionCache에서는 GetOrDefault로 존재 여부 확인
            var result = await _fusionCache.GetOrDefaultAsync<object>(key);
            return result != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if cache key exists in FusionCache: {CacheKey}", key);
            return false;
        }
    }

    /// <summary>
    /// 여러 키를 배치로 조회
    /// FusionCache는 배치 조회를 직접 지원하지 않으므로 병렬로 처리
    /// </summary>
    public async Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var keyList = keys.ToList();
        var tasks = keyList.Select(async key => new KeyValuePair<string, T?>(key, await GetAsync<T>(key, cancellationToken)));
        
        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
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
        var pattern = wildcard
            .Replace(".", "\\.")
            .Replace("*", ".*")
            .Replace("?", ".");

        return $"^{pattern}$";
    }
}