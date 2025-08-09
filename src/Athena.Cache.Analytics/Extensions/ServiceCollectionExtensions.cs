using Athena.Cache.Analytics.Abstractions;
using Athena.Cache.Analytics.Implementations;
using Microsoft.Extensions.DependencyInjection;

namespace Athena.Cache.Analytics.Extensions;

/// <summary>
/// Athena Cache Analytics 서비스 등록 확장
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 메모리 기반 캐시 분석 시스템 등록
    /// </summary>
    public static IServiceCollection AddAthenaCacheAnalytics(
        this IServiceCollection services,
        Action<CacheAnalyticsOptions>? configure = null)
    {
        var options = new CacheAnalyticsOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<MemoryCacheEventCollector>();
        services.AddSingleton<ICacheEventCollector>(provider =>
            provider.GetRequiredService<MemoryCacheEventCollector>());
        services.AddSingleton<ICacheAnalyticsService, MemoryCacheAnalyticsService>();

        return services;
    }
}