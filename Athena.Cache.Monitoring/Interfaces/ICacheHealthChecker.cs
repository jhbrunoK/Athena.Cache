using Athena.Cache.Monitoring.Models;

namespace Athena.Cache.Monitoring.Interfaces
{
    /// <summary>
    /// 캐시 상태 확인 서비스
    /// </summary>
    public interface ICacheHealthChecker
    {
        /// <summary>
        /// 전체 캐시 상태 확인
        /// </summary>
        Task<CacheHealthStatus> CheckHealthAsync();

        /// <summary>
        /// 개별 컴포넌트 상태 확인
        /// </summary>
        Task<HealthCheckResult> CheckComponentHealthAsync(string componentName);
    }
}
