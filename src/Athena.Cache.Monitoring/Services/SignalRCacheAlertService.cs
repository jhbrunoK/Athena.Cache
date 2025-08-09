using Athena.Cache.Monitoring.Hubs;
using Athena.Cache.Monitoring.Interfaces;
using Athena.Cache.Monitoring.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Athena.Cache.Monitoring.Services;

/// <summary>
/// SignalR를 통한 실시간 알림 서비스
/// </summary>
public class SignalRCacheAlertService(
    ICacheAlertService baseAlertService,
    IHubContext<CacheMonitoringHub> hubContext,
    ILogger<SignalRCacheAlertService> logger)
    : ICacheAlertService
{
    public event EventHandler<CacheAlert>? AlertRaised
    {
        add => baseAlertService.AlertRaised += value;
        remove => baseAlertService.AlertRaised -= value;
    }

    public async Task SendAlertAsync(CacheAlert alert)
    {
        // 기본 알림 서비스 호출
        await baseAlertService.SendAlertAsync(alert);

        // SignalR를 통한 실시간 알림
        try
        {
            await hubContext.Clients.Group("Monitoring").SendAsync("AlertReceived", alert);
            logger.LogDebug("Alert sent via SignalR: {AlertId}", alert.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send alert via SignalR: {AlertId}", alert.Id);
        }
    }

    public Task CheckAlertsAsync(CacheMetrics metrics)
    {
        return baseAlertService.CheckAlertsAsync(metrics);
    }
}