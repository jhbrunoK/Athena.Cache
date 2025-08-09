using Athena.Invalidation.MemoryCache.Abstractions;
using Athena.Invalidation.MemoryCache.Engine;
using Athena.Invalidation.MemoryCache.Providers;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Athena.Invalidation.MemoryCache.Extensions;

/// <summary>
/// Memory Cache 무효화 서비스 등록 확장
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Memory Cache 무효화 서비스를 등록합니다
    /// </summary>
    public static IServiceCollection AddMemoryCacheInvalidation(
        this IServiceCollection services,
        Action<MemoryCacheInvalidationOptions>? configureProvider = null,
        Action<MemoryCacheInvalidationEngineOptions>? configureEngine = null)
    {
        // IMemoryCache가 등록되어 있지 않다면 기본 구현 등록
        services.AddMemoryCache();

        // 옵션 등록
        if (configureProvider != null)
        {
            services.Configure(configureProvider);
        }
        else
        {
            services.Configure<MemoryCacheInvalidationOptions>(_ => { });
        }

        if (configureEngine != null)
        {
            services.Configure(configureEngine);
        }
        else
        {
            services.Configure<MemoryCacheInvalidationEngineOptions>(_ => { });
        }

        // Memory Cache 무효화 서비스 등록
        services.AddSingleton<IMemoryCacheInvalidationProvider, MemoryCacheInvalidationProvider>();
        services.AddSingleton<MemoryCacheInvalidationEngine>();
        services.AddSingleton<IInvalidationEngine>(sp => sp.GetRequiredService<MemoryCacheInvalidationEngine>());

        return services;
    }

    /// <summary>
    /// Memory Cache 무효화 서비스를 등록합니다 (기존 IMemoryCache 사용)
    /// </summary>
    public static IServiceCollection AddMemoryCacheInvalidation(
        this IServiceCollection services,
        IMemoryCache memoryCache,
        Action<MemoryCacheInvalidationOptions>? configureProvider = null,
        Action<MemoryCacheInvalidationEngineOptions>? configureEngine = null)
    {
        // 제공된 IMemoryCache 인스턴스 등록
        services.AddSingleton(memoryCache);

        return services.AddMemoryCacheInvalidation(configureProvider, configureEngine);
    }

    /// <summary>
    /// Memory Cache 무효화 헬스체크 추가
    /// </summary>
    public static IServiceCollection AddMemoryCacheInvalidationHealthChecks(
        this IServiceCollection services,
        string? healthCheckName = null,
        TimeSpan? timeout = null)
    {
        services.AddHealthChecks()
            .AddCheck<MemoryCacheInvalidationHealthCheck>(
                healthCheckName ?? "memory_cache_invalidation",
                timeout: timeout ?? TimeSpan.FromSeconds(5));

        return services;
    }

    /// <summary>
    /// Memory Cache 설정 생성 도우미
    /// </summary>
    public static MemoryCacheOptions CreateMemoryCacheOptions(
        long? sizeLimit = null,
        TimeSpan? compactionPercentage = null,
        TimeSpan? expirationScanFrequency = null)
    {
        var options = new MemoryCacheOptions();
        
        if (sizeLimit.HasValue)
        {
            options.SizeLimit = sizeLimit.Value;
        }
        
        if (compactionPercentage.HasValue)
        {
            // compactionPercentage는 MemoryCacheOptions에서 직접 지원하지 않음
            // 실제 구현에서는 다른 방법으로 설정해야 함
        }
        
        if (expirationScanFrequency.HasValue)
        {
            options.ExpirationScanFrequency = expirationScanFrequency.Value;
        }
        
        return options;
    }
}

/// <summary>
/// Memory Cache 무효화 헬스체크
/// </summary>
public class MemoryCacheInvalidationHealthCheck : IHealthCheck
{
    private readonly IMemoryCacheInvalidationProvider _memoryCacheProvider;
    private readonly ILogger<MemoryCacheInvalidationHealthCheck> _logger;

    public MemoryCacheInvalidationHealthCheck(
        IMemoryCacheInvalidationProvider memoryCacheProvider,
        ILogger<MemoryCacheInvalidationHealthCheck> logger)
    {
        _memoryCacheProvider = memoryCacheProvider ?? throw new ArgumentNullException(nameof(memoryCacheProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await _memoryCacheProvider.IsHealthyAsync(cancellationToken);
            
            if (isHealthy)
            {
                var stats = await _memoryCacheProvider.GetStatisticsAsync(cancellationToken);
                var data = new Dictionary<string, object>
                {
                    ["provider_name"] = _memoryCacheProvider.Name,
                    ["provider_type"] = _memoryCacheProvider.Type.ToString(),
                    ["total_keys"] = stats.TotalKeys,
                    ["total_tags"] = stats.TotalTags,
                    ["hit_ratio"] = stats.HitRatio,
                    ["total_evictions"] = stats.TotalEvictions,
                    ["cache_count"] = _memoryCacheProvider.Count
                };

                return HealthCheckResult.Healthy("Memory cache invalidation provider is healthy", data);
            }
            else
            {
                return HealthCheckResult.Unhealthy("Memory cache invalidation provider is not healthy");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory cache invalidation health check failed");
            return HealthCheckResult.Unhealthy("Memory cache invalidation health check failed", ex);
        }
    }
}