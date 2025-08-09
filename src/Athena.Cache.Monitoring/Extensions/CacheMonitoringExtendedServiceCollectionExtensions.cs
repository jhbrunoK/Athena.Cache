using Athena.Cache.Monitoring.Collectors;
using Athena.Cache.Monitoring.Interfaces;
using Athena.Cache.Monitoring.Models;
using Athena.Cache.Monitoring.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Athena.Cache.Monitoring.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Athena.Cache.Monitoring.Extensions;

/// <summary>
/// 확장된 캐시 모니터링 서비스 등록
/// </summary>
public static class CacheMonitoringExtendedServiceCollectionExtensions
{
    /// <summary>
    /// Redis 기반 캐시 모니터링 등록
    /// </summary>
    public static IServiceCollection AddRedisCacheMonitoring(
        this IServiceCollection services,
        Action<CacheMonitoringOptions>? configure = null)
    {
        // 기본 모니터링 서비스 등록
        services.AddAthenaCacheMonitoring(configure);

        // Redis 전용 메트릭 수집기로 교체 (사용자가 이미 등록했다면 유지)
        services.Replace(ServiceDescriptor.Singleton<ICacheMetricsCollector, RedisCacheMetricsCollector>());

        return services;
    }

    /// <summary>
    /// SignalR 기반 실시간 모니터링 등록
    /// </summary>
    public static IServiceCollection AddRealTimeCacheMonitoring(
        this IServiceCollection services,
        Action<CacheMonitoringOptions>? configure = null)
    {
        // 기본 모니터링 서비스 등록
        services.AddAthenaCacheMonitoring(configure);

        // SignalR 서비스 등록
        services.TryAddSignalR();

        services.AddKeyedSingleton<ICacheAlertService, CacheAlertService>("base");
        services.Replace(ServiceDescriptor.Singleton<ICacheAlertService>(provider =>
        {
            var baseService = provider.GetRequiredKeyedService<ICacheAlertService>("base");
            var hubContext = provider.GetRequiredService<IHubContext<CacheMonitoringHub>>();
            var logger = provider.GetRequiredService<ILogger<SignalRCacheAlertService>>();

            return new SignalRCacheAlertService(baseService, hubContext, logger);
        }));

        return services;
    }

    /// <summary>
    /// 완전한 모니터링 스택 등록 (Redis + SignalR)
    /// </summary>
    public static IServiceCollection AddCompleteCacheMonitoring(
        this IServiceCollection services,
        Action<CacheMonitoringOptions>? configure = null)
    {
        // Redis 모니터링
        services.AddRedisCacheMonitoring(configure);

        // 실시간 모니터링
        services.AddRealTimeCacheMonitoring();

        return services;
    }
}