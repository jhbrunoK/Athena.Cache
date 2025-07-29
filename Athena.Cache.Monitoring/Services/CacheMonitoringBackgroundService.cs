using Athena.Cache.Monitoring.Interfaces;
using Athena.Cache.Monitoring.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Athena.Cache.Monitoring.Services
{
    /// <summary>
    /// 백그라운드에서 지속적으로 모니터링을 수행하는 서비스
    /// </summary>
    public class CacheMonitoringBackgroundService(
        ICacheMetricsCollector metricsCollector,
        ICacheHealthChecker healthChecker,
        ICacheAlertService alertService,
        ILogger<CacheMonitoringBackgroundService> logger,
        IOptions<CacheMonitoringOptions> options)
        : BackgroundService
    {
        private readonly ICacheHealthChecker _healthChecker = healthChecker;
        private readonly CacheMonitoringOptions _options = options.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Cache monitoring background service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 메트릭 수집 및 기록
                    var metrics = await metricsCollector.CollectMetricsAsync();
                    await metricsCollector.RecordMetricsAsync(metrics);

                    // 알림 확인
                    await alertService.CheckAlertsAsync(metrics);

                    logger.LogDebug("Cache monitoring cycle completed. Hit ratio: {HitRatio:P1}, Response time: {ResponseTime:F1}ms",
                        metrics.HitRatio, metrics.AverageResponseTimeMs);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during cache monitoring cycle");
                }

                await Task.Delay(_options.MetricsCollectionInterval, stoppingToken);
            }

            logger.LogInformation("Cache monitoring background service stopped");
        }
    }
}
