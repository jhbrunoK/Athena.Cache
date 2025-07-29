using Athena.Cache.Monitoring.AlertChannels;
using Athena.Cache.Monitoring.Interfaces;
using Athena.Cache.Monitoring.Managers;
using Athena.Cache.Monitoring.Models;
using Athena.Cache.Monitoring.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Athena.Cache.Monitoring.Extensions;

/// <summary>
/// 캐시 모니터링 서비스 등록 확장 메서드
/// </summary>
public static class CacheMonitoringServiceCollectionExtensions
{
    /// <summary>
    /// 캐시 모니터링 서비스 등록
    /// </summary>
    public static IServiceCollection AddAthenaCacheMonitoring(
        this IServiceCollection services,
        Action<CacheMonitoringOptions>? configure = null)
    {
        // 설정 등록
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.TryAddSingleton<CacheMonitoringOptions>();
        }

        // 기본 알림 채널들 등록
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAlertChannel, LogAlertChannel>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAlertChannel, ConsoleAlertChannel>());

        // 알림 채널 관리자
        services.TryAddSingleton<AlertChannelManager>();

        // 모니터링 서비스들 등록
        services.TryAddSingleton<ICacheMetricsCollector, MemoryCacheMetricsCollector>();
        services.TryAddSingleton<ICacheHealthChecker, CacheHealthChecker>();
        services.TryAddSingleton<ICacheAlertService, CacheAlertService>();

        // 백그라운드 서비스 등록
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CacheMonitoringBackgroundService>());

        return services;
    }
}