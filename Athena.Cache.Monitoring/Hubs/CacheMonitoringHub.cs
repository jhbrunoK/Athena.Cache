using Athena.Cache.Monitoring.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;

namespace Athena.Cache.Monitoring.Hubs
{
    /// <summary>
    /// 실시간 캐시 모니터링 SignalR 허브
    /// </summary>
    public class CacheMonitoringHub(
        ICacheMetricsCollector metricsCollector,
        ICacheHealthChecker healthChecker,
        ILogger<CacheMonitoringHub> logger)
        : Hub
    {
        public async Task JoinMonitoringGroup()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Monitoring");
            logger.LogInformation("Client {ConnectionId} joined monitoring group", Context.ConnectionId);
        }

        public async Task LeaveMonitoringGroup()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Monitoring");
            logger.LogInformation("Client {ConnectionId} left monitoring group", Context.ConnectionId);
        }

        public async Task GetCurrentMetrics()
        {
            try
            {
                var metrics = await metricsCollector.CollectMetricsAsync();
                await Clients.Caller.SendAsync("MetricsUpdate", metrics);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send current metrics to client {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Failed to retrieve metrics");
            }
        }

        public async Task GetHealthStatus()
        {
            try
            {
                var health = await healthChecker.CheckHealthAsync();
                await Clients.Caller.SendAsync("HealthUpdate", health);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send health status to client {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Failed to retrieve health status");
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            logger.LogInformation("Client {ConnectionId} disconnected", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
