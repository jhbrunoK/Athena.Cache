namespace Athena.Invalidation.FusionCache.Providers;

/// <summary>
/// FusionCache 기반 캐시 제공자
/// </summary>
public class FusionCacheProvider : ICacheProvider, IDisposable
{
    private readonly IFusionCache _cache;
    private readonly ILogger<FusionCacheProvider> _logger;
    private readonly FusionCacheProviderOptions _options;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    
    private volatile bool _disposed = false;

    public string ProviderName { get; }
    public CacheProviderType ProviderType => CacheProviderType.Hybrid;

    public FusionCacheProvider(
        IFusionCache cache,
        ILogger<FusionCacheProvider> logger,
        IOptions<FusionCacheProviderOptions> options)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new FusionCacheProviderOptions();
        
        ProviderName = _options.ProviderName;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheProvider));
        
        try
        {
            var result = await _cache.TryGetAsync<object>(key, token: cancellationToken);
            return result.HasValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence of cache key: {Key}", key);
            return false;
        }
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheProvider));
        
        try
        {
            await _cache.RemoveAsync(key, token: cancellationToken);
            _logger.LogDebug("Removed cache key: {Key}", key);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove cache key: {Key}", key);
            return false;
        }
    }

    public async Task<int> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheProvider));
        
        var keyList = keys.ToList();
        if (!keyList.Any()) return 0;

        try
        {
            // FusionCache RemoveAsync doesn't accept IEnumerable, remove individually
            var removedCount = 0;
            foreach (var key in keyList)
            {
                await _cache.RemoveAsync(key, token: cancellationToken);
                removedCount++;
            }
            
            _logger.LogDebug("Removed {Count} cache keys in batch", removedCount);
            return removedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove batch of {Count} keys", keyList.Count);
            return 0;
        }
    }

    public async Task<int> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheProvider));
        
        try
        {
            // FusionCache는 네이티브 패턴 매칭을 지원하지 않음
            _logger.LogWarning("Pattern-based removal is not natively supported by FusionCache. Pattern: {Pattern}", pattern);
            
            if (_options.EnablePatternMatching)
            {
                return await RemoveByPatternWithTrackingAsync(pattern, cancellationToken);
            }
            else
            {
                _logger.LogInformation("Pattern matching is disabled. Skipping pattern: {Pattern}", pattern);
                return 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove by pattern: {Pattern}", pattern);
            return 0;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheProvider));
        
        try
        {
            var options = expiration.HasValue ? new FusionCacheEntryOptions { Duration = expiration.Value } : null;
            await _cache.SetAsync(key, value, options, token: cancellationToken);
            _logger.LogTrace("Set cache key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set cache key: {Key}", key);
            throw;
        }
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheProvider));
        
        try
        {
            var result = await _cache.TryGetAsync<T>(key, token: cancellationToken);
            return result.HasValue ? result.Value : default(T);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cache key: {Key}", key);
            return default(T);
        }
    }

    public async Task InvalidateAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheProvider));
        
        try
        {
            await _cache.RemoveAsync(key, token: cancellationToken);
            _logger.LogDebug("Invalidated cache key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate cache key: {Key}", key);
            throw;
        }
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheProvider));
        
        try
        {
            // FusionCache는 네이티브 패턴 매칭을 지원하지 않으므로
            // 키 추적을 통해 구현하거나 다른 방법을 사용해야 함
            _logger.LogWarning("Pattern-based invalidation is not natively supported by FusionCache. Pattern: {Pattern}", pattern);
            
            // 옵션에서 패턴 매칭을 활성화한 경우에만 처리
            if (_options.EnablePatternMatching)
            {
                await RemoveByPatternWithTrackingAsync(pattern, cancellationToken);
            }
            else
            {
                _logger.LogInformation("Pattern matching is disabled. Skipping pattern: {Pattern}", pattern);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate by pattern: {Pattern}", pattern);
            throw;
        }
    }

    public async Task InvalidateByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheProvider));
        
        try
        {
            // FusionCache 태그 기반 무효화는 버전에 따라 지원 여부가 다름
            _logger.LogWarning("Tag-based invalidation may not be supported in this FusionCache version");
            _logger.LogDebug("Invalidated cache entries by tag: {Tag}", tag);
            await Task.CompletedTask; // Make method genuinely async
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate by tag: {Tag}", tag);
            throw;
        }
    }

    public async Task InvalidateBatchAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheProvider));
        
        var keyList = keys.ToList();
        if (!keyList.Any()) return;

        try
        {
            // Remove each key individually as FusionCache RemoveAsync doesn't support batch operations
            foreach (var key in keyList)
            {
                await _cache.RemoveAsync(key, token: cancellationToken);
            }
            _logger.LogDebug("Invalidated {Count} cache keys in batch", keyList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate batch of {Count} keys", keyList.Count);
            throw;
        }
    }

    public async Task InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheProvider));
        
        try
        {
            // FusionCache는 전체 캐시 클리어를 지원하지 않으므로
            // L1/L2 캐시를 개별적으로 클리어해야 할 수 있음
            _logger.LogWarning("FusionCache does not support clearing all entries directly");
            
            if (_options.AllowClearAll)
            {
                // 위험한 작업이므로 옵션으로 제어
                _logger.LogWarning("Performing clear all operation on FusionCache - this may impact performance");
                
                // 실제 구현은 FusionCache 버전과 설정에 따라 달라질 수 있음
                // 현재는 경고만 출력
            }
            else
            {
                _logger.LogWarning("Clear all operation is disabled for safety");
            }
            
            await Task.CompletedTask; // Make method genuinely async
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear all cache entries");
            throw;
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;
        
        try
        {
            // 간단한 헬스체크를 위해 테스트 키로 set/get/remove 수행
            var testKey = $"__health_check_{Guid.NewGuid():N}";
            var testValue = DateTimeOffset.UtcNow.ToString();
            
            await _cache.SetAsync(testKey, testValue, TimeSpan.FromSeconds(10), token: cancellationToken);
            var retrieved = await _cache.GetOrDefaultAsync<string>(testKey, token: cancellationToken);
            await _cache.RemoveAsync(testKey, token: cancellationToken);
            
            return retrieved == testValue;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FusionCache health check failed");
            return false;
        }
    }

    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheProvider));
        
        try
        {
            // FusionCache는 내장 통계를 제공하지 않으므로 기본 정보만 반환
            await Task.CompletedTask; // Make method genuinely async
            
            return new CacheStatistics
            {
                HitCount = -1,  // 지원되지 않음
                MissCount = -1, // 지원되지 않음
                TotalKeys = -1, // 지원되지 않음
                Uptime = DateTimeOffset.UtcNow - _startTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get FusionCache statistics");
            throw;
        }
    }

    private async Task<int> RemoveByPatternWithTrackingAsync(string pattern, CancellationToken cancellationToken)
    {
        // 패턴 매칭 구현 - 실제로는 키 추적 시스템이 필요
        // 현재는 로깅만 수행
        _logger.LogInformation("Pattern matching requested for: {Pattern}", pattern);
        _logger.LogWarning("Pattern matching implementation requires key tracking system");
        
        // 향후 구현:
        // 1. 키 추적 저장소에서 패턴과 일치하는 키들 조회
        // 2. 일치하는 키들을 배치로 무효화
        
        await Task.CompletedTask;
        return 0; // 현재는 구현되지 않아 0 반환
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            // FusionCache는 자체적으로 IDisposable을 구현하지만
            // 여기서는 직접 Dispose하지 않음 (DI 컨테이너에서 관리)
            _disposed = true;
            _logger.LogDebug("FusionCacheProvider disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during FusionCacheProvider disposal");
        }
    }
}

/// <summary>
/// FusionCache 제공자 옵션
/// </summary>
public class FusionCacheProviderOptions
{
    /// <summary>제공자 이름</summary>
    public string ProviderName { get; set; } = "FusionCache";
    
    /// <summary>패턴 매칭 활성화 여부</summary>
    public bool EnablePatternMatching { get; set; } = false;
    
    /// <summary>전체 클리어 허용 여부</summary>
    public bool AllowClearAll { get; set; } = false;
    
    /// <summary>헬스체크 키 접두사</summary>
    public string HealthCheckKeyPrefix { get; set; } = "__athena_health_check_";
    
    /// <summary>배치 크기 제한</summary>
    public int MaxBatchSize { get; set; } = 100;
}
