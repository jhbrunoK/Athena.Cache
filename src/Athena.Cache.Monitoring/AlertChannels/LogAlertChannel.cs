using Athena.Cache.Monitoring.Enums;
using Athena.Cache.Monitoring.Interfaces;
using Athena.Cache.Monitoring.Models;
using Microsoft.Extensions.Logging;

namespace Athena.Cache.Monitoring.AlertChannels;

/// <summary>
/// 로그 기반 알림 채널 (기본 제공)
/// </summary>
public class LogAlertChannel(ILogger<LogAlertChannel> logger) : IAlertChannel
{
    public string Name => "Log";

    public Task SendAsync(CacheAlert alert, CancellationToken cancellationToken = default)
    {
        var logLevel = alert.Level switch
        {
            AlertLevel.Critical => LogLevel.Error,
            AlertLevel.Warning => LogLevel.Warning,
            AlertLevel.Info => LogLevel.Information,
            _ => LogLevel.Information
        };

        logger.Log(logLevel, "Cache Alert: [{Level}] {Title} - {Message} (Component: {Component})",
            alert.Level, alert.Title, alert.Message, alert.Component);

        return Task.CompletedTask;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}