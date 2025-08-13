namespace Athena.Invalidation.Monitoring.Abstractions;

/// <summary>
/// 무효화 엔진 헬스체크 인터페이스
/// </summary>
public interface IInvalidationHealthChecker
{
    /// <summary>전체 헬스 상태 확인</summary>
    Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default);
    
    /// <summary>무효화 엔진 상태 확인</summary>
    Task<HealthCheckResult> CheckInvalidationEngineAsync(CancellationToken cancellationToken = default);
    
    /// <summary>분산 이벤트 버스 상태 확인</summary>
    Task<HealthCheckResult> CheckEventBusAsync(CancellationToken cancellationToken = default);
    
    /// <summary>캐시 제공자 상태 확인</summary>
    Task<HealthCheckResult> CheckCacheProvidersAsync(CancellationToken cancellationToken = default);
    
    /// <summary>데이터베이스 연결 상태 확인</summary>
    Task<HealthCheckResult> CheckDatabaseConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 헬스체크 확장 정보
/// </summary>
public static class HealthCheckExtensions
{
    public static HealthCheckResult CreateResult(HealthStatus status, string description, Exception? exception = null, IReadOnlyDictionary<string, object>? data = null)
    {
        return new HealthCheckResult(status, description, exception, data);
    }
    
    public static HealthCheckResult Healthy(string description, IReadOnlyDictionary<string, object>? data = null)
    {
        return CreateResult(HealthStatus.Healthy, description, null, data);
    }
    
    public static HealthCheckResult Degraded(string description, Exception? exception = null, IReadOnlyDictionary<string, object>? data = null)
    {
        return CreateResult(HealthStatus.Degraded, description, exception, data);
    }
    
    public static HealthCheckResult Unhealthy(string description, Exception? exception = null, IReadOnlyDictionary<string, object>? data = null)
    {
        return CreateResult(HealthStatus.Unhealthy, description, exception, data);
    }
}
