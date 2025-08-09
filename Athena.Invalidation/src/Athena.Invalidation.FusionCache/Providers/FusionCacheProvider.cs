using Athena.Invalidation.FusionCache.Abstractions;
using System.Text.RegularExpressions;

namespace Athena.Invalidation.FusionCache.Providers;

/// <summary>
/// FusionCache 기반 캐시 제공자
/// </summary>
public class FusionCacheProvider : ICacheProvider, IDisposable
{
    private readonly IFusionCache _cache;
    private readonly ILogger<FusionCacheProvider> _logger;
    private readonly FusionCacheProviderOptions _options;
    
    private volatile bool _disposed = false;

    public string Name { get; }
    public CacheProviderType Type => CacheProviderType.Hybrid;

    public FusionCacheProvider(
        IFusionCache cache,
        ILogger<FusionCacheProvider> logger,
        IOptions<FusionCacheProviderOptions> options)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new FusionCacheProviderOptions();
        
        Name = _options.ProviderName;
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
                await InvalidateByPatternWithTrackingAsync(pattern, cancellationToken);
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
            // FusionCache v1.2+ 에서 태그 기반 무효화 지원
            await _cache.ExpireByTagAsync(tag, token: cancellationToken);
            _logger.LogDebug("Invalidated cache entries by tag: {Tag}", tag);
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
            // FusionCache는 배치 제거를 지원하므로 한 번에 처리
            await _cache.RemoveAsync(keyList, token: cancellationToken);
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

    public async Task<CacheProviderStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FusionCacheProvider));
        
        try
        {
            // FusionCache는 내장 통계를 제공하지 않으므로 기본 정보만 반환
            return new CacheProviderStatistics
            {
                ProviderName = Name,
                Type = Type,
                TotalKeys = -1, // 지원되지 않음
                HitCount = -1,  // 지원되지 않음
                MissCount = -1, // 지원되지 않음
                HitRatio = -1,  // 지원되지 않음
                LastAccess = DateTimeOffset.UtcNow,
                AdditionalMetrics = new Dictionary<string, object>
                {
                    ["cache_name"] = _cache.CacheName,
                    ["default_entry_options"] = _cache.DefaultEntryOptions?.ToString() ?? "null",
                    ["supports_pattern_matching"] = _options.EnablePatternMatching,
                    ["allows_clear_all"] = _options.AllowClearAll
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get FusionCache statistics");
            throw;
        }
    }

    private async Task InvalidateByPatternWithTrackingAsync(string pattern, CancellationToken cancellationToken)
    {
        // 패턴 매칭 구현 - 실제로는 키 추적 시스템이 필요
        // 현재는 로깅만 수행
        _logger.LogInformation("Pattern matching requested for: {Pattern}", pattern);
        _logger.LogWarning("Pattern matching implementation requires key tracking system");
        
        // 향후 구현:
        // 1. 키 추적 저장소에서 패턴과 일치하는 키들 조회
        // 2. 일치하는 키들을 배치로 무효화
        
        await Task.CompletedTask;
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