namespace Athena.Invalidation.Core.Models;

/// <summary>
/// 무효화 엔진 전체 설정 옵션
/// </summary>
public class InvalidationOptions
{
    /// <summary>
    /// 엔진 활성화 여부
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 기본 무효화 전략
    /// </summary>
    public string DefaultStrategy { get; set; } = "Basic";

    /// <summary>
    /// 최대 동시 무효화 작업 수
    /// </summary>
    public int MaxConcurrentInvalidations { get; set; } = Environment.ProcessorCount * 2;

    /// <summary>
    /// 기본 타임아웃
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 기본 재시도 횟수
    /// </summary>
    public int DefaultMaxRetries { get; set; } = 3;

    /// <summary>
    /// 추적 키 만료 시간 (기본 1일)
    /// </summary>
    public TimeSpan TrackingKeyExpiration { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// 성능 설정
    /// </summary>
    public PerformanceOptions Performance { get; set; } = new();

    /// <summary>
    /// 로깅 설정
    /// </summary>
    public LoggingOptions Logging { get; set; } = new();

    /// <summary>
    /// 모니터링 설정
    /// </summary>
    public MonitoringOptions Monitoring { get; set; } = new();

    /// <summary>
    /// 오류 처리 설정
    /// </summary>
    public ErrorHandlingOptions ErrorHandling { get; set; } = new();
}

/// <summary>
/// 성능 최적화 설정
/// </summary>
public class PerformanceOptions
{
    /// <summary>
    /// 배치 처리 크기
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// 메모리 풀링 사용 여부
    /// </summary>
    public bool UseMemoryPooling { get; set; } = true;

    /// <summary>
    /// 제로 할당 모드 사용 여부
    /// </summary>
    public bool EnableZeroAllocation { get; set; } = true;

    /// <summary>
    /// 적응형 배치 크기 사용 여부
    /// </summary>
    public bool AdaptiveBatchSize { get; set; } = true;

    /// <summary>
    /// 백그라운드 처리 사용 여부
    /// </summary>
    public bool UseBackgroundProcessing { get; set; } = true;
}

/// <summary>
/// 로깅 설정
/// </summary>
public class LoggingOptions
{
    /// <summary>
    /// 무효화 이벤트 로깅 여부
    /// </summary>
    public bool LogInvalidationEvents { get; set; } = true;

    /// <summary>
    /// 성능 메트릭 로깅 여부
    /// </summary>
    public bool LogPerformanceMetrics { get; set; } = false;

    /// <summary>
    /// 디버그 정보 로깅 여부
    /// </summary>
    public bool LogDebugInfo { get; set; } = false;

    /// <summary>
    /// 최소 로그 레벨
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
}

/// <summary>
/// 모니터링 설정
/// </summary>
public class MonitoringOptions
{
    /// <summary>
    /// 실시간 메트릭 수집 여부
    /// </summary>
    public bool EnableRealTimeMetrics { get; set; } = true;

    /// <summary>
    /// 상태 체크 간격
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 메트릭 보존 기간
    /// </summary>
    public TimeSpan MetricsRetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// 알림 임계값
    /// </summary>
    public AlertThresholds Thresholds { get; set; } = new();
}

/// <summary>
/// 알림 임계값 설정
/// </summary>
public class AlertThresholds
{
    /// <summary>
    /// 실패율 임계값 (백분율)
    /// </summary>
    public double FailureRateThreshold { get; set; } = 5.0;

    /// <summary>
    /// 응답 시간 임계값
    /// </summary>
    public TimeSpan ResponseTimeThreshold { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 대기열 크기 임계값
    /// </summary>
    public int QueueSizeThreshold { get; set; } = 1000;
}

/// <summary>
/// 오류 처리 설정
/// </summary>
public class ErrorHandlingOptions
{
    /// <summary>
    /// 자동 재시도 여부
    /// </summary>
    public bool EnableAutoRetry { get; set; } = true;

    /// <summary>
    /// Circuit Breaker 사용 여부
    /// </summary>
    public bool UseCircuitBreaker { get; set; } = true;

    /// <summary>
    /// Circuit Breaker 임계값
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>
    /// Fallback 처리 사용 여부
    /// </summary>
    public bool EnableFallback { get; set; } = true;

    /// <summary>
    /// 커스텀 오류 핸들러
    /// </summary>
    public Func<Exception, Task>? CustomErrorHandler { get; set; }
}

/// <summary>
/// 로그 레벨 (Microsoft.Extensions.Logging과 호환)
/// </summary>
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    None = 6
}