using Athena.Cache.Monitoring.Models;

namespace Athena.Cache.Monitoring.Interfaces;

/// <summary>
/// 알림 서비스
/// </summary>
public interface ICacheAlertService
{
    /// <summary>
    /// 알림 발송
    /// </summary>
    Task SendAlertAsync(CacheAlert alert);

    /// <summary>
    /// 메트릭 기반 자동 알림 확인
    /// </summary>
    Task CheckAlertsAsync(CacheMetrics metrics);

    /// <summary>
    /// 알림 구독
    /// </summary>
    event EventHandler<CacheAlert>? AlertRaised;
}