using Athena.Cache.Monitoring.Enums;
using Athena.Cache.Monitoring.Interfaces;
using Athena.Cache.Monitoring.Models;

namespace Athena.Cache.Monitoring.AlertChannels;

/// <summary>
/// 콘솔 알림 채널 (개발용)
/// </summary>
public class ConsoleAlertChannel : IAlertChannel
{
    public string Name => "Console";

    public Task SendAsync(CacheAlert alert, CancellationToken cancellationToken = default)
    {
        var color = alert.Level switch
        {
            AlertLevel.Critical => ConsoleColor.Red,
            AlertLevel.Warning => ConsoleColor.Yellow,
            AlertLevel.Info => ConsoleColor.Green,
            _ => ConsoleColor.White
        };

        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"🚨 [{alert.Level}] {alert.Title}");
        Console.WriteLine($"   {alert.Message}");
        Console.WriteLine($"   Component: {alert.Component} | Time: {alert.Timestamp:HH:mm:ss}");
        Console.ForegroundColor = originalColor;

        return Task.CompletedTask;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}