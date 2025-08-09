using Athena.Cache.Monitoring.Interfaces;
using Athena.Cache.Monitoring.Models;
using Microsoft.Extensions.Logging;

namespace Athena.Cache.Monitoring.Managers;

/// <summary>
/// 알림 채널 관리자
/// </summary>
public class AlertChannelManager(
    IEnumerable<IAlertChannel> channels,
    ILogger<AlertChannelManager> logger)
{
    public async Task SendToAllChannelsAsync(CacheAlert alert)
    {
        var tasks = channels.Select(async channel =>
        {
            try
            {
                if (await channel.IsAvailableAsync())
                {
                    await channel.SendAsync(alert);
                    logger.LogDebug("Alert sent successfully to {ChannelName}: {AlertId}",
                        channel.Name, alert.Id);
                }
                else
                {
                    logger.LogWarning("Channel {ChannelName} is not available for alert: {AlertId}",
                        channel.Name, alert.Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send alert to {ChannelName}: {AlertId}",
                    channel.Name, alert.Id);
            }
        });

        await Task.WhenAll(tasks);
    }
}