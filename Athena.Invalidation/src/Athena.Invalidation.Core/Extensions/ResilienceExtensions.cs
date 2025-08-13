using Athena.Invalidation.Core.Resilience;

namespace Athena.Invalidation.Core.Extensions;

/// <summary>
/// 복원력(Resilience) 관련 확장 메서드
/// </summary>
public static class ResilienceExtensions
{
    /// <summary>
    /// 재시도 패턴을 무효화 엔진에 추가
    /// </summary>
    public static IServiceCollection AddInvalidationRetry(
        this IServiceCollection services,
        Action<RetryOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // 기존 IInvalidationEngine을 재시도 데코레이터로 감싸기
        services.Decorate<IInvalidationEngine, RetryInvalidationEngine>();

        return services;
    }

    /// <summary>
    /// 서킷 브레이커 패턴을 무효화 엔진에 추가
    /// </summary>
    public static IServiceCollection AddInvalidationCircuitBreaker(
        this IServiceCollection services,
        Action<CircuitBreakerOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // 기존 IInvalidationEngine을 서킷 브레이커 데코레이터로 감싸기
        services.Decorate<IInvalidationEngine, CircuitBreakerInvalidationEngine>();

        return services;
    }

    /// <summary>
    /// 완전한 복원력 스택 추가 (재시도 + 서킷 브레이커)
    /// </summary>
    public static IServiceCollection AddInvalidationResilience(
        this IServiceCollection services,
        Action<RetryOptions>? configureRetry = null,
        Action<CircuitBreakerOptions>? configureCircuitBreaker = null)
    {
        // 순서 중요: 내부에서 외부로 (재시도 -> 서킷 브레이커)
        return services
            .AddInvalidationRetry(configureRetry)
            .AddInvalidationCircuitBreaker(configureCircuitBreaker);
    }

    /// <summary>
    /// 고가용성을 위한 사전 구성된 복원력 설정
    /// </summary>
    public static IServiceCollection AddHighAvailabilityInvalidation(this IServiceCollection services)
    {
        return services.AddInvalidationResilience(
            retry => 
            {
                retry.MaxRetryAttempts = 5;
                retry.BaseDelay = TimeSpan.FromMilliseconds(100);
                retry.MaxDelay = TimeSpan.FromSeconds(10);
                retry.DelayStrategy = RetryDelayStrategy.ExponentialWithJitter;
                retry.JitterFactor = 0.2;
            },
            circuitBreaker => 
            {
                circuitBreaker.FailureThreshold = 10;
                circuitBreaker.FailureRateThreshold = 0.3;
                circuitBreaker.OpenDuration = TimeSpan.FromMinutes(2);
                circuitBreaker.MinSuccessfulCallsInHalfOpen = 5;
                circuitBreaker.SlidingWindowSize = 200;
                circuitBreaker.MinCallsInSlidingWindow = 20;
            }
        );
    }

    /// <summary>
    /// 빠른 실패를 위한 사전 구성된 복원력 설정
    /// </summary>
    public static IServiceCollection AddFastFailInvalidation(this IServiceCollection services)
    {
        return services.AddInvalidationResilience(
            retry => 
            {
                retry.MaxRetryAttempts = 2;
                retry.BaseDelay = TimeSpan.FromMilliseconds(50);
                retry.MaxDelay = TimeSpan.FromSeconds(1);
                retry.DelayStrategy = RetryDelayStrategy.Linear;
            },
            circuitBreaker => 
            {
                circuitBreaker.FailureThreshold = 3;
                circuitBreaker.FailureRateThreshold = 0.5;
                circuitBreaker.OpenDuration = TimeSpan.FromSeconds(30);
                circuitBreaker.MinSuccessfulCallsInHalfOpen = 2;
                circuitBreaker.SlidingWindowSize = 50;
                circuitBreaker.MinCallsInSlidingWindow = 5;
            }
        );
    }

    /// <summary>
    /// 네트워크 중심 환경을 위한 사전 구성된 복원력 설정
    /// </summary>
    public static IServiceCollection AddNetworkResilientInvalidation(this IServiceCollection services)
    {
        return services.AddInvalidationResilience(
            retry => 
            {
                retry.MaxRetryAttempts = 7;
                retry.BaseDelay = TimeSpan.FromMilliseconds(500);
                retry.MaxDelay = TimeSpan.FromSeconds(30);
                retry.DelayStrategy = RetryDelayStrategy.ExponentialWithJitter;
                retry.JitterFactor = 0.3;
                
                // 네트워크 관련 예외만 재시도
                retry.RetryableExceptions.AddRange([
                    typeof(HttpRequestException),
                    typeof(SocketException),
                    typeof(TimeoutException),
                    typeof(TaskCanceledException)
                ]);
            },
            circuitBreaker => 
            {
                circuitBreaker.FailureThreshold = 15;
                circuitBreaker.FailureRateThreshold = 0.4;
                circuitBreaker.OpenDuration = TimeSpan.FromMinutes(5);
                circuitBreaker.MinSuccessfulCallsInHalfOpen = 10;
                circuitBreaker.SlidingWindowSize = 500;
                circuitBreaker.MinCallsInSlidingWindow = 50;
                circuitBreaker.HealthCheckInterval = TimeSpan.FromSeconds(15);
            }
        );
    }

    /// <summary>
    /// 특정 예외 타입에 대한 재시도 정책 추가
    /// </summary>
    public static IServiceCollection AddInvalidationRetryFor<TException>(
        this IServiceCollection services,
        Action<RetryOptions>? configureOptions = null)
        where TException : Exception
    {
        services.Configure<RetryOptions>(options =>
        {
            options.RetryableExceptions.Add(typeof(TException));
            configureOptions?.Invoke(options);
        });

        return services;
    }

    /// <summary>
    /// 특정 예외 타입을 재시도에서 제외
    /// </summary>
    public static IServiceCollection ExcludeFromInvalidationRetry<TException>(
        this IServiceCollection services)
        where TException : Exception
    {
        services.Configure<RetryOptions>(options =>
        {
            options.NonRetryableExceptions.Add(typeof(TException));
        });

        return services;
    }
}

/// <summary>
/// 복원력 통계 확장
/// </summary>
public static class ResilienceStatisticsExtensions
{
    /// <summary>
    /// 서킷 브레이커 상태 조회
    /// </summary>
    public static Dictionary<string, CircuitBreakerState>? GetCircuitBreakerStates(this IServiceProvider services)
    {
        var circuitBreakerEngine = services.GetServices<IInvalidationEngine>()
            .OfType<CircuitBreakerInvalidationEngine>()
            .FirstOrDefault();
            
        return circuitBreakerEngine?.GetCircuitBreakerStates();
    }

    /// <summary>
    /// 특정 연산 타입의 서킷 브레이커 수동 리셋
    /// </summary>
    public static bool ResetCircuitBreaker(this IServiceProvider services, string operationType)
    {
        var circuitBreakerEngine = services.GetServices<IInvalidationEngine>()
            .OfType<CircuitBreakerInvalidationEngine>()
            .FirstOrDefault();
            
        if (circuitBreakerEngine != null)
        {
            circuitBreakerEngine.ResetCircuitBreaker(operationType);
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// 복원력 상태 요약 조회
    /// </summary>
    public static ResilienceStatistics GetResilienceStatistics(this IServiceProvider services)
    {
        var statistics = new ResilienceStatistics();

        // 서킷 브레이커 상태
        var circuitStates = services.GetCircuitBreakerStates();
        if (circuitStates != null)
        {
            statistics.CircuitBreakerStates = circuitStates;
            statistics.HasCircuitBreaker = true;
            statistics.OpenCircuitCount = circuitStates.Count(kvp => kvp.Value.State == CircuitState.Open);
            statistics.HalfOpenCircuitCount = circuitStates.Count(kvp => kvp.Value.State == CircuitState.HalfOpen);
        }

        statistics.Timestamp = DateTimeOffset.UtcNow;
        
        return statistics;
    }
}

/// <summary>
/// 복원력 통계 정보
/// </summary>
public class ResilienceStatistics
{
    public DateTimeOffset Timestamp { get; set; }
    public bool HasRetry { get; set; }
    public bool HasCircuitBreaker { get; set; }
    public int OpenCircuitCount { get; set; }
    public int HalfOpenCircuitCount { get; set; }
    public Dictionary<string, CircuitBreakerState> CircuitBreakerStates { get; set; } = new();
}
