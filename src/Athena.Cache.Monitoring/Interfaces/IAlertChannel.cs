using Athena.Cache.Monitoring.Models;

namespace Athena.Cache.Monitoring.Interfaces;

/// <summary>
/// 알림 채널 추상화 인터페이스
/// </summary>
public interface IAlertChannel
{
    string Name { get; }
    Task SendAsync(CacheAlert alert, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}