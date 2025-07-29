namespace Athena.Cache.Monitoring.Models;

/// <summary>
/// 알림 임계값 설정
/// </summary>
public class AlertThresholds
{
    /// <summary>
    /// 캐시 히트율 최소값 (기본: 80%)
    /// </summary>
    public double MinHitRatio { get; set; } = 0.8;

    /// <summary>
    /// 최대 응답 시간 (밀리초, 기본: 100ms)
    /// </summary>
    public double MaxResponseTimeMs { get; set; } = 100;

    /// <summary>
    /// 최대 메모리 사용량 (MB, 기본: 1GB)
    /// </summary>
    public long MaxMemoryUsageMB { get; set; } = 1024;

    /// <summary>
    /// 최대 연결 수 (기본: 1000)
    /// </summary>
    public int MaxConnections { get; set; } = 1000;

    /// <summary>
    /// 에러율 최대값 (기본: 5%)
    /// </summary>
    public double MaxErrorRate { get; set; } = 0.05;
}