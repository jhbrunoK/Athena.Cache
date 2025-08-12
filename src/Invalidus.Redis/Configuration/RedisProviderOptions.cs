using System.Text.Json;

namespace Invalidus.Redis.Configuration;

/// <summary>
/// Universal Redis Provider 설정 옵션
/// Configuration options for the Universal Redis Provider
/// </summary>
public class RedisProviderOptions
{
    #region Provider Information

    /// <summary>제공자 이름</summary>
    public string ProviderName { get; set; } = "UniversalRedis";

    #endregion

    #region Redis Connection Settings

    /// <summary>Redis 데이터베이스 번호 (기본값: 0)</summary>
    public int Database { get; set; } = 0;

    /// <summary>키 접두사 (네임스페이스 분리용)</summary>
    public string? KeyPrefix { get; set; }

    #endregion

    #region Cache Settings

    /// <summary>기본 만료 시간 (기본값: 30분)</summary>
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>JSON 직렬화 옵션</summary>
    public JsonSerializerOptions JsonOptions { get; set; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    #endregion

    #region Invalidation Settings

    /// <summary>키 스캔 시 페이지 크기 (기본값: 1000)</summary>
    public int ScanPageSize { get; set; } = 1000;

    /// <summary>스캔 결과 최대 개수 (메모리 보호용)</summary>
    public int MaxScanResults { get; set; } = 10000;

    /// <summary>배치 작업에서 트랜잭션 사용 여부</summary>
    public bool UseTransaction { get; set; } = true;

    /// <summary>배치 크기 (한 번에 처리할 항목 수)</summary>
    public int BatchSize { get; set; } = 100;

    #endregion

    #region Health Check Settings

    /// <summary>헬스체크 키 접두사</summary>
    public string HealthCheckKeyPrefix { get; set; } = "__invalidus_health_check_";

    /// <summary>헬스체크 타임아웃</summary>
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

    #endregion

    #region Performance Settings

    /// <summary>연결 타임아웃</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>명령 타임아웃</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>배치 처리 간 지연 시간 (Redis 부하 감소용)</summary>
    public TimeSpan BatchDelay { get; set; } = TimeSpan.FromMilliseconds(10);

    /// <summary>파이프라인 사용 여부 (성능 최적화)</summary>
    public bool UsePipeline { get; set; } = true;

    #endregion

    #region Logging Settings

    /// <summary>로깅 설정</summary>
    public RedisLoggingOptions Logging { get; set; } = new();

    #endregion

    #region Failure Handling Settings

    /// <summary>실패 처리 설정</summary>
    public RedisFailureHandlingOptions FailureHandling { get; set; } = new();

    #endregion
}

/// <summary>
/// Redis 로깅 설정
/// Redis logging configuration
/// </summary>
public class RedisLoggingOptions
{
    /// <summary>캐시 작업 로깅 (Get/Set) 활성화</summary>
    public bool LogCacheOperations { get; set; } = false;

    /// <summary>무효화 작업 로깅 활성화</summary>
    public bool LogInvalidation { get; set; } = true;

    /// <summary>성능 메트릭 로깅 활성화</summary>
    public bool LogPerformanceMetrics { get; set; } = false;

    /// <summary>연결 상태 변경 로깅 활성화</summary>
    public bool LogConnectionEvents { get; set; } = true;

    /// <summary>에러 로깅 활성화 (항상 true 권장)</summary>
    public bool LogErrors { get; set; } = true;

    /// <summary>디버그 정보 로깅 활성화</summary>
    public bool LogDebugInfo { get; set; } = false;
}

/// <summary>
/// Redis 실패 처리 설정
/// Redis failure handling configuration
/// </summary>
public class RedisFailureHandlingOptions
{
    /// <summary>Redis 오류 시 예외 발생 여부</summary>
    public bool ThrowOnRedisError { get; set; } = false;

    /// <summary>JSON 직렬화/역직렬화 오류 시 예외 발생 여부</summary>
    public bool ThrowOnJsonError { get; set; } = false;

    /// <summary>연결 실패 시 재시도 횟수</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>재시도 간격</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Circuit Breaker 활성화 여부</summary>
    public bool EnableCircuitBreaker { get; set; } = true;

    /// <summary>Circuit Breaker 실패 임계값</summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>Circuit Breaker 복구 시간</summary>
    public TimeSpan CircuitBreakerRecoveryTime { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>사용자 정의 오류 핸들러</summary>
    public Func<Exception, Task>? CustomErrorHandler { get; set; }
}

/// <summary>
/// Redis Provider 확장 설정
/// Extended settings for Redis Provider
/// </summary>
public static class RedisProviderDefaults
{
    /// <summary>개발 환경용 기본 설정</summary>
    public static RedisProviderOptions Development => new()
    {
        ProviderName = "UniversalRedis-Dev",
        Database = 0,
        DefaultExpiration = TimeSpan.FromMinutes(5),
        ScanPageSize = 100,
        MaxScanResults = 1000,
        Logging = new RedisLoggingOptions
        {
            LogCacheOperations = true,
            LogInvalidation = true,
            LogDebugInfo = true
        },
        FailureHandling = new RedisFailureHandlingOptions
        {
            ThrowOnRedisError = true,
            ThrowOnJsonError = true
        }
    };

    /// <summary>프로덕션 환경용 기본 설정</summary>
    public static RedisProviderOptions Production => new()
    {
        ProviderName = "UniversalRedis-Prod",
        Database = 0,
        DefaultExpiration = TimeSpan.FromHours(1),
        ScanPageSize = 1000,
        MaxScanResults = 10000,
        UseTransaction = true,
        UsePipeline = true,
        Logging = new RedisLoggingOptions
        {
            LogCacheOperations = false,
            LogInvalidation = true,
            LogPerformanceMetrics = true,
            LogDebugInfo = false
        },
        FailureHandling = new RedisFailureHandlingOptions
        {
            ThrowOnRedisError = false,
            ThrowOnJsonError = false,
            EnableCircuitBreaker = true,
            MaxRetryAttempts = 3
        }
    };

    /// <summary>고성능 환경용 설정</summary>
    public static RedisProviderOptions HighPerformance => new()
    {
        ProviderName = "UniversalRedis-HighPerf",
        Database = 0,
        DefaultExpiration = TimeSpan.FromMinutes(30),
        ScanPageSize = 5000,
        MaxScanResults = 50000,
        BatchSize = 500,
        UseTransaction = false, // 성능을 위해 트랜잭션 비활성화
        UsePipeline = true,
        BatchDelay = TimeSpan.Zero, // 지연 없음
        Logging = new RedisLoggingOptions
        {
            LogCacheOperations = false,
            LogInvalidation = false, // 로깅 최소화
            LogErrors = true
        },
        FailureHandling = new RedisFailureHandlingOptions
        {
            ThrowOnRedisError = false,
            EnableCircuitBreaker = false, // Circuit Breaker 비활성화
            MaxRetryAttempts = 1 // 빠른 실패
        }
    };
}