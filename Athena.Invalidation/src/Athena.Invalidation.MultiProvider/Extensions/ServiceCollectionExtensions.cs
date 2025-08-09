using Athena.Invalidation.MultiProvider.Abstractions;
using Athena.Invalidation.MultiProvider.Engine;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Athena.Invalidation.MultiProvider.Extensions;

/// <summary>
/// 다중 제공자 무효화 서비스 등록 확장
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 다중 제공자 무효화 엔진을 등록합니다
    /// </summary>
    public static IServiceCollection AddMultiProviderInvalidation(
        this IServiceCollection services,
        Action<MultiProviderInvalidationOptions>? configure = null)
    {
        // 옵션 등록
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<MultiProviderInvalidationOptions>(_ => { });
        }

        // 다중 제공자 무효화 엔진 등록
        services.AddSingleton<IMultiProviderInvalidationEngine, MultiProviderInvalidationEngine>();
        
        // IInvalidationEngine 인터페이스로도 접근 가능하게 함
        services.AddSingleton<IInvalidationEngine>(sp => sp.GetRequiredService<IMultiProviderInvalidationEngine>());

        return services;
    }

    /// <summary>
    /// 다중 제공자 무효화 엔진을 특정 제공자들과 함께 등록합니다
    /// </summary>
    public static IServiceCollection AddMultiProviderInvalidationWithProviders(
        this IServiceCollection services,
        Action<IServiceCollection> configureProviders,
        Action<MultiProviderInvalidationOptions>? configure = null)
    {
        // 개별 제공자들 먼저 등록
        configureProviders(services);
        
        // 다중 제공자 엔진 등록
        return services.AddMultiProviderInvalidation(configure);
    }

    /// <summary>
    /// 다중 제공자 무효화 헬스체크 추가
    /// </summary>
    public static IServiceCollection AddMultiProviderInvalidationHealthChecks(
        this IServiceCollection services,
        string? healthCheckName = null,
        TimeSpan? timeout = null)
    {
        services.AddHealthChecks()
            .AddCheck<MultiProviderInvalidationHealthCheck>(
                healthCheckName ?? "multi_provider_invalidation",
                timeout: timeout ?? TimeSpan.FromSeconds(30));

        return services;
    }

    /// <summary>
    /// 모든 캐시 제공자를 한 번에 등록하는 편의 메서드
    /// </summary>
    public static IServiceCollection AddAllCacheProviders(
        this IServiceCollection services,
        Action<AllCacheProvidersOptions>? configure = null)
    {
        var options = new AllCacheProvidersOptions();
        configure?.Invoke(options);

        // Memory Cache 추가
        if (options.EnableMemoryCache)
        {
            services.AddMemoryCache();
            // MemoryCache 제공자 등록 (실제로는 해당 프로젝트의 확장 메서드 사용)
            // services.AddMemoryCacheInvalidation(options.MemoryCacheOptions);
        }

        // Redis 추가
        if (options.EnableRedis && !string.IsNullOrEmpty(options.RedisConnectionString))
        {
            // Redis 제공자 등록 (실제로는 해당 프로젝트의 확장 메서드 사용)
            // services.AddRedisInvalidation(options.RedisConnectionString, options.RedisOptions);
        }

        // FusionCache 추가
        if (options.EnableFusionCache)
        {
            // FusionCache 제공자 등록 (실제로는 해당 프로젝트의 확장 메서드 사용)
            // services.AddFusionCacheInvalidation(options.FusionCacheOptions);
        }

        // 다중 제공자 엔진 등록
        services.AddMultiProviderInvalidation(multiOptions =>
        {
            multiOptions.DefaultFailurePolicy = options.DefaultFailurePolicy;
            multiOptions.EnableParallelExecution = options.EnableParallelExecution;
            multiOptions.MaxConcurrency = options.MaxConcurrency;
            multiOptions.AllowClearAll = options.AllowClearAll;
        });

        return services;
    }

    /// <summary>
    /// 제공자별 설정을 위한 빌더 패턴
    /// </summary>
    public static MultiProviderInvalidationBuilder CreateMultiProviderBuilder(
        this IServiceCollection services)
    {
        return new MultiProviderInvalidationBuilder(services);
    }
}

/// <summary>
/// 모든 캐시 제공자 설정 옵션
/// </summary>
public class AllCacheProvidersOptions
{
    /// <summary>Memory Cache 활성화</summary>
    public bool EnableMemoryCache { get; set; } = true;
    
    /// <summary>Redis 활성화</summary>
    public bool EnableRedis { get; set; } = false;
    
    /// <summary>FusionCache 활성화</summary>
    public bool EnableFusionCache { get; set; } = false;
    
    /// <summary>Redis 연결 문자열</summary>
    public string? RedisConnectionString { get; set; }
    
    /// <summary>기본 실패 처리 정책</summary>
    public FailureHandlingPolicy DefaultFailurePolicy { get; set; } = FailureHandlingPolicy.AtLeastOneSucceeds;
    
    /// <summary>병렬 실행 활성화</summary>
    public bool EnableParallelExecution { get; set; } = true;
    
    /// <summary>최대 동시 실행 수</summary>
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
    
    /// <summary>전체 클리어 허용</summary>
    public bool AllowClearAll { get; set; } = false;
    
    // 개별 제공자 옵션들은 실제 구현 시 추가
    // public Action<MemoryCacheInvalidationOptions>? MemoryCacheOptions { get; set; }
    // public Action<RedisInvalidationOptions>? RedisOptions { get; set; }
    // public Action<FusionCacheInvalidationOptions>? FusionCacheOptions { get; set; }
}

/// <summary>
/// 다중 제공자 빌더 클래스
/// </summary>
public class MultiProviderInvalidationBuilder
{
    private readonly IServiceCollection _services;

    public MultiProviderInvalidationBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>Memory Cache 제공자 추가</summary>
    public MultiProviderInvalidationBuilder AddMemoryCache(Action<object>? configure = null)
    {
        // 실제 구현에서는 MemoryCache 제공자 등록
        _services.AddMemoryCache();
        
        return this;
    }

    /// <summary>Redis 제공자 추가</summary>
    public MultiProviderInvalidationBuilder AddRedis(string connectionString, Action<object>? configure = null)
    {
        // 실제 구현에서는 Redis 제공자 등록
        
        return this;
    }

    /// <summary>FusionCache 제공자 추가</summary>
    public MultiProviderInvalidationBuilder AddFusionCache(Action<object>? configure = null)
    {
        // 실제 구현에서는 FusionCache 제공자 등록
        
        return this;
    }

    /// <summary>빌더 완료 및 다중 제공자 엔진 등록</summary>
    public IServiceCollection Build(Action<MultiProviderInvalidationOptions>? configure = null)
    {
        return _services.AddMultiProviderInvalidation(configure);
    }
}

/// <summary>
/// 다중 제공자 무효화 헬스체크
/// </summary>
public class MultiProviderInvalidationHealthCheck : IHealthCheck
{
    private readonly IMultiProviderInvalidationEngine _multiProviderEngine;
    private readonly ILogger<MultiProviderInvalidationHealthCheck> _logger;

    public MultiProviderInvalidationHealthCheck(
        IMultiProviderInvalidationEngine multiProviderEngine,
        ILogger<MultiProviderInvalidationHealthCheck> logger)
    {
        _multiProviderEngine = multiProviderEngine ?? throw new ArgumentNullException(nameof(multiProviderEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var overallStatus = await _multiProviderEngine.GetStatusAsync(cancellationToken);
            var providerStatuses = await _multiProviderEngine.GetProvidersStatusAsync(cancellationToken);
            
            var healthyProviders = providerStatuses.Values.Count(s => s.IsHealthy);
            var totalProviders = providerStatuses.Count;
            
            var data = new Dictionary<string, object>
            {
                ["providers_count"] = _multiProviderEngine.ProvidersCount,
                ["active_providers_count"] = _multiProviderEngine.ActiveProviders.Count(),
                ["healthy_providers_count"] = healthyProviders,
                ["total_providers_count"] = totalProviders,
                ["overall_healthy"] = overallStatus.IsHealthy,
                ["uptime_seconds"] = overallStatus.Uptime.TotalSeconds,
                ["active_providers"] = _multiProviderEngine.ActiveProviders.ToArray()
            };

            // 개별 제공자 상태도 포함
            foreach (var kvp in providerStatuses)
            {
                data[$"provider_{kvp.Key}_healthy"] = kvp.Value.IsHealthy;
                data[$"provider_{kvp.Key}_tracked_keys"] = kvp.Value.TrackedKeysCount;
            }

            if (overallStatus.IsHealthy)
            {
                return HealthCheckResult.Healthy(
                    $"Multi-provider invalidation engine is healthy ({healthyProviders}/{totalProviders} providers healthy)", 
                    data);
            }
            else
            {
                return HealthCheckResult.Degraded(
                    $"Multi-provider invalidation engine is degraded ({healthyProviders}/{totalProviders} providers healthy)", 
                    data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multi-provider invalidation health check failed");
            return HealthCheckResult.Unhealthy("Multi-provider invalidation health check failed", ex);
        }
    }
}