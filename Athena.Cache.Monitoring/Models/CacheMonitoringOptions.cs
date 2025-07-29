namespace Athena.Cache.Monitoring.Models
{
    /// <summary>
    /// 캐시 모니터링 설정
    /// </summary>
    public class CacheMonitoringOptions
    {
        /// <summary>
        /// 메트릭 수집 간격 (기본: 30초)
        /// </summary>
        public TimeSpan MetricsCollectionInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 메트릭 보관 기간 (기본: 24시간)
        /// </summary>
        public TimeSpan MetricsRetentionPeriod { get; set; } = TimeSpan.FromHours(24);

        /// <summary>
        /// 알림 임계값 설정
        /// </summary>
        public AlertThresholds Thresholds { get; set; } = new();

        /// <summary>
        /// 상태 확인 간격 (기본: 5초)
        /// </summary>
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// SignalR를 통한 실시간 알림 활성화
        /// </summary>
        public bool EnableRealTimeNotifications { get; set; } = true;
    }
}
