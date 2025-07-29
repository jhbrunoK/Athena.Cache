using Athena.Cache.Monitoring.Enums;
using Athena.Cache.Monitoring.Interfaces;
using Athena.Cache.Monitoring.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Athena.Cache.Monitoring.Services
{
    /// <summary>
    /// 캐시 알림 서비스
    /// </summary>
    public class CacheAlertService(
        ILogger<CacheAlertService> logger,
        IOptions<CacheMonitoringOptions> options)
        : ICacheAlertService
    {
        private readonly CacheMonitoringOptions _options = options.Value;
        private readonly ConcurrentDictionary<string, DateTime> _lastAlertTimes = new();
        private readonly TimeSpan _alertCooldown = TimeSpan.FromMinutes(5);

        public event EventHandler<CacheAlert>? AlertRaised;

        public async Task SendAlertAsync(CacheAlert alert)
        {
            try
            {
                // 쿨다운 체크
                var alertKey = $"{alert.Component}_{alert.Level}";
                if (_lastAlertTimes.TryGetValue(alertKey, out var lastTime))
                {
                    if (DateTime.UtcNow - lastTime < _alertCooldown)
                    {
                        return; // 쿨다운 중이므로 알림 스킵
                    }
                }

                _lastAlertTimes[alertKey] = DateTime.UtcNow;

                logger.LogWarning("Cache Alert: [{Level}] {Title} - {Message}",
                    alert.Level, alert.Title, alert.Message);

                // 이벤트 발생
                AlertRaised?.Invoke(this, alert);

                // 실제 구현에서는 여기서 다양한 알림 채널로 전송
                // - 이메일, Slack, Teams, SMS 등
                await NotifyChannelsAsync(alert);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send alert: {Alert}", JsonSerializer.Serialize(alert));
            }
        }

        public async Task CheckAlertsAsync(CacheMetrics metrics)
        {
            var alerts = new List<CacheAlert>();

            // 히트율 체크
            if (metrics.HitRatio < _options.Thresholds.MinHitRatio)
            {
                var level = metrics.HitRatio < _options.Thresholds.MinHitRatio * 0.7
                    ? AlertLevel.Critical
                    : AlertLevel.Warning;

                alerts.Add(new CacheAlert
                {
                    Level = level,
                    Title = "Low Cache Hit Ratio",
                    Message = $"Cache hit ratio is {metrics.HitRatio:P1}, below threshold of {_options.Thresholds.MinHitRatio:P1}",
                    Component = "Performance",
                    Data = new Dictionary<string, object>
                    {
                        ["HitRatio"] = metrics.HitRatio,
                        ["Threshold"] = _options.Thresholds.MinHitRatio
                    }
                });
            }

            // 응답 시간 체크
            if (metrics.AverageResponseTimeMs > _options.Thresholds.MaxResponseTimeMs)
            {
                var level = metrics.AverageResponseTimeMs > _options.Thresholds.MaxResponseTimeMs * 2
                    ? AlertLevel.Critical
                    : AlertLevel.Warning;

                alerts.Add(new CacheAlert
                {
                    Level = level,
                    Title = "High Response Time",
                    Message = $"Average response time is {metrics.AverageResponseTimeMs:F1}ms, above threshold of {_options.Thresholds.MaxResponseTimeMs}ms",
                    Component = "Performance",
                    Data = new Dictionary<string, object>
                    {
                        ["ResponseTimeMs"] = metrics.AverageResponseTimeMs,
                        ["Threshold"] = _options.Thresholds.MaxResponseTimeMs
                    }
                });
            }

            // 메모리 사용량 체크
            if (metrics.MemoryUsageMB > _options.Thresholds.MaxMemoryUsageMB)
            {
                var level = metrics.MemoryUsageMB > _options.Thresholds.MaxMemoryUsageMB * 1.2
                    ? AlertLevel.Critical
                    : AlertLevel.Warning;

                alerts.Add(new CacheAlert
                {
                    Level = level,
                    Title = "High Memory Usage",
                    Message = $"Memory usage is {metrics.MemoryUsageMB}MB, above threshold of {_options.Thresholds.MaxMemoryUsageMB}MB",
                    Component = "Memory",
                    Data = new Dictionary<string, object>
                    {
                        ["MemoryUsageMB"] = metrics.MemoryUsageMB,
                        ["Threshold"] = _options.Thresholds.MaxMemoryUsageMB
                    }
                });
            }

            // 에러율 체크
            if (metrics.ErrorRate > _options.Thresholds.MaxErrorRate)
            {
                var level = metrics.ErrorRate > _options.Thresholds.MaxErrorRate * 2
                    ? AlertLevel.Critical
                    : AlertLevel.Warning;

                alerts.Add(new CacheAlert
                {
                    Level = level,
                    Title = "High Error Rate",
                    Message = $"Error rate is {metrics.ErrorRate:P1}, above threshold of {_options.Thresholds.MaxErrorRate:P1}",
                    Component = "Reliability",
                    Data = new Dictionary<string, object>
                    {
                        ["ErrorRate"] = metrics.ErrorRate,
                        ["Threshold"] = _options.Thresholds.MaxErrorRate
                    }
                });
            }

            // 알림 발송
            foreach (var alert in alerts)
            {
                await SendAlertAsync(alert);
            }
        }

        private async Task NotifyChannelsAsync(CacheAlert alert)
        {
            // 실제 구현에서는 설정된 알림 채널로 전송
            // 예: Slack, Email, SMS, Teams 등
            await Task.Delay(10); // 시뮬레이션

            logger.LogInformation("Alert sent to notification channels: {AlertId}", alert.Id);
        }
    }
}
