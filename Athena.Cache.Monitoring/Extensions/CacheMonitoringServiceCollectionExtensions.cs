using Athena.Cache.Monitoring.Interfaces;
using Athena.Cache.Monitoring.Models;
using Athena.Cache.Monitoring.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Athena.Cache.Monitoring.Extensions
{
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
                services.Configure<CacheMonitoringOptions>(_ => { });
            }

            // 모니터링 서비스들 등록
            services.AddSingleton<ICacheMetricsCollector, MemoryCacheMetricsCollector>();
            services.AddSingleton<ICacheHealthChecker, CacheHealthChecker>();
            services.AddSingleton<ICacheAlertService, CacheAlertService>();

            // 백그라운드 서비스 등록
            services.AddHostedService<CacheMonitoringBackgroundService>();

            return services;
        }
    }
}
