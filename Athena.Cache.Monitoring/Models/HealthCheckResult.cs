using Athena.Cache.Monitoring.Enums;

namespace Athena.Cache.Monitoring.Models
{
    /// <summary>
    /// 개별 컴포넌트 상태 확인 결과
    /// </summary>
    public class HealthCheckResult
    {
        public HealthStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public TimeSpan ResponseTime { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
    }
}
