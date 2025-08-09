using Athena.Cache.Core.HealthChecks;
using MsHealthCheck = Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Athena.Cache.Core.Extensions;

/// <summary>
/// Athena Cache Health Check를 위한 서비스 컬렉션 확장 메서드
/// </summary>
public static class HealthCheckServiceCollectionExtensions
{
    /// <summary>
    /// Athena Cache Health Check를 추가합니다
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="name">Health Check 이름</param>
    /// <param name="configureOptions">Health Check 옵션 설정</param>
    /// <param name="failureStatus">실패 시 상태</param>
    /// <param name="tags">태그</param>
    /// <returns>Health Check Builder</returns>
    public static IServiceCollection AddAthenaCacheHealthCheck(
        this IServiceCollection services,
        string name = "athena_cache",
        Action<AthenaCacheHealthCheckOptions>? configureOptions = null,
        MsHealthCheck.HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        // Health Check 옵션 등록
        var options = new AthenaCacheHealthCheckOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        // Health Check 등록
        services.AddHealthChecks()
            .AddCheck<AthenaCacheHealthCheck>(
                name: name,
                failureStatus: failureStatus ?? MsHealthCheck.HealthStatus.Degraded,
                tags: tags);

        return services;
    }

    /// <summary>
    /// 포괄적인 Athena Cache Health Check를 추가합니다 (모든 구성 요소 포함)
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">Health Check 옵션 설정</param>
    /// <returns>Health Check Builder</returns>
    public static IServiceCollection AddComprehensiveAthenaCacheHealthCheck(
        this IServiceCollection services,
        Action<AthenaCacheHealthCheckOptions>? configureOptions = null)
    {
        // 기본 Health Check 추가
        services.AddAthenaCacheHealthCheck("athena_cache_comprehensive", configureOptions, tags: new[] { "cache", "athena", "comprehensive" });

        // 개별 구성 요소별 Health Check 추가
        services.AddAthenaCacheHealthCheck("athena_cache_basic", options =>
        {
            configureOptions?.Invoke(options);
            // 기본 캐시 작업만 체크
        }, tags: new[] { "cache", "athena", "basic" });

        services.AddAthenaCacheHealthCheck("athena_cache_performance", options =>
        {
            configureOptions?.Invoke(options);
            // 성능 메트릭 중심 체크 (더 엄격한 임계값)
            options.MinHitRatio = 0.8;
            options.MaxResponseTime = TimeSpan.FromMilliseconds(50);
            options.MaxErrorRate = 0.005; // 0.5%
        }, tags: new[] { "cache", "athena", "performance" });

        services.AddAthenaCacheHealthCheck("athena_cache_memory", options =>
        {
            configureOptions?.Invoke(options);
            // 메모리 상태 중심 체크
            options.MaxMemoryUsageMB = 256;
            options.CriticalMemoryUsageMB = 512;
            options.MaxGen2GcFrequency = 0.05; // 20초에 1번
        }, tags: new[] { "cache", "athena", "memory" });

        services.AddAthenaCacheHealthCheck("athena_cache_security", options =>
        {
            configureOptions?.Invoke(options);
            // 보안 상태 중심 체크
            options.MaxSecurityBlockRate = 2.0; // 2%
            options.MaxSensitiveDataAttempts = 50;
        }, tags: new[] { "cache", "athena", "security" });

        return services;
    }

    /// <summary>
    /// 개발 환경용 관대한 Health Check를 추가합니다
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>Health Check Builder</returns>
    public static IServiceCollection AddDevelopmentAthenaCacheHealthCheck(this IServiceCollection services)
    {
        return services.AddAthenaCacheHealthCheck("athena_cache_dev", options =>
        {
            // 개발 환경용 관대한 설정
            options.MinHitRatio = 0.3; // 30%
            options.CriticalHitRatio = 0.1; // 10%
            options.MaxResponseTime = TimeSpan.FromMilliseconds(500);
            options.CriticalResponseTime = TimeSpan.FromSeconds(2);
            options.MaxErrorRate = 0.1; // 10%
            options.CriticalErrorRate = 0.3; // 30%
            options.MaxMemoryUsageMB = 1024;
            options.CriticalMemoryUsageMB = 2048;
            options.MaxGen2GcFrequency = 1.0; // 1초에 1번
            options.MaxSecurityBlockRate = 20.0; // 20%
            options.MaxSensitiveDataAttempts = 1000;
        }, tags: new[] { "cache", "athena", "development" });
    }

    /// <summary>
    /// 프로덕션 환경용 엄격한 Health Check를 추가합니다
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>Health Check Builder</returns>
    public static IServiceCollection AddProductionAthenaCacheHealthCheck(this IServiceCollection services)
    {
        return services.AddAthenaCacheHealthCheck("athena_cache_prod", options =>
        {
            // 프로덕션 환경용 엄격한 설정
            options.MinHitRatio = 0.85; // 85%
            options.CriticalHitRatio = 0.7; // 70%
            options.MaxResponseTime = TimeSpan.FromMilliseconds(50);
            options.CriticalResponseTime = TimeSpan.FromMilliseconds(200);
            options.MaxErrorRate = 0.001; // 0.1%
            options.CriticalErrorRate = 0.01; // 1%
            options.MaxMemoryUsageMB = 256;
            options.CriticalMemoryUsageMB = 512;
            options.MaxGen2GcFrequency = 0.02; // 50초에 1번
            options.MaxSecurityBlockRate = 1.0; // 1%
            options.MaxSensitiveDataAttempts = 10;
        }, tags: new[] { "cache", "athena", "production" });
    }

    /// <summary>
    /// Health Check에 Athena Cache 관련 의존성을 자동으로 추가합니다
    /// </summary>
    /// <param name="builder">Health Check Builder</param>
    /// <returns>Health Check Builder</returns>
    public static IHealthChecksBuilder AddAthenaCacheDependencies(this IHealthChecksBuilder builder)
    {
        var services = builder.Services;

        // AthenaCacheHealthCheck 등록 (아직 등록되지 않은 경우)
        services.AddTransient<AthenaCacheHealthCheck>();

        return builder;
    }

    /// <summary>
    /// 빠른 Health Check를 추가합니다 (기본 연결만 확인)
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>Health Check Builder</returns>
    public static IServiceCollection AddQuickAthenaCacheHealthCheck(this IServiceCollection services)
    {
        return services.AddAthenaCacheHealthCheck("athena_cache_quick", options =>
        {
            // 빠른 체크용 설정 (매우 관대함)
            options.MinHitRatio = 0.1; // 10%
            options.CriticalHitRatio = 0.0; // 0%
            options.MaxResponseTime = TimeSpan.FromSeconds(5);
            options.CriticalResponseTime = TimeSpan.FromSeconds(10);
            options.MaxErrorRate = 0.5; // 50%
            options.CriticalErrorRate = 0.9; // 90%
            options.MaxMemoryUsageMB = 4096;
            options.CriticalMemoryUsageMB = 8192;
            options.MaxGen2GcFrequency = 10.0; // 매우 빈번해도 허용
            options.MaxSecurityBlockRate = 50.0; // 50%
            options.MaxSensitiveDataAttempts = 10000;
        }, tags: new[] { "cache", "athena", "quick" });
    }

    /// <summary>
    /// 상세한 Health Check 리포팅을 추가합니다
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="endpoint">Health Check 엔드포인트 경로</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddAthenaCacheHealthCheckReporting(
        this IServiceCollection services, 
        string endpoint = "/health/athena-cache")
    {
        services.AddHealthChecks();

        // Health Check UI 추가 (필요한 경우)
        // services.AddHealthChecksUI();

        return services;
    }
}

/// <summary>
/// Health Check 확장 유틸리티
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Health Check 결과를 문자열로 포맷합니다
    /// </summary>
    /// <param name="result">Health Check 결과</param>
    /// <returns>포맷된 문자열</returns>
    public static string ToDetailedString(this MsHealthCheck.HealthCheckResult result)
    {
        var status = result.Status.ToString().ToUpperInvariant();
        var description = result.Description ?? "No description";
        
        if (result.Data?.Count > 0)
        {
            var dataEntries = result.Data.Select(kvp => $"{kvp.Key}: {kvp.Value}");
            var dataString = string.Join(", ", dataEntries);
            return $"[{status}] {description} | Data: {dataString}";
        }

        return $"[{status}] {description}";
    }

    /// <summary>
    /// Health Check 결과가 정상인지 확인합니다
    /// </summary>
    /// <param name="result">Health Check 결과</param>
    /// <returns>정상 여부</returns>
    public static bool IsHealthy(this MsHealthCheck.HealthCheckResult result)
    {
        return result.Status == MsHealthCheck.HealthStatus.Healthy;
    }

    /// <summary>
    /// Health Check 결과가 심각한 상태인지 확인합니다
    /// </summary>
    /// <param name="result">Health Check 결과</param>
    /// <returns>심각한 상태 여부</returns>
    public static bool IsCritical(this MsHealthCheck.HealthCheckResult result)
    {
        return result.Status == MsHealthCheck.HealthStatus.Unhealthy;
    }
}
