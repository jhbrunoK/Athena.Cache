using Invalidus.Core.Abstractions;

namespace Invalidus.Monitoring.Abstractions;

/// <summary>
/// 통합 무효화 모니터링 인터페이스 - 캐시와 무효화 작업을 모두 모니터링
/// Unified invalidation monitoring interface for both caching and invalidation operations
/// </summary>
public interface IInvalidationMonitor
{
    #region Health Monitoring

    /// <summary>
    /// 전체 시스템 상태 확인 (모든 프로바이더 포함)
    /// Check overall system health including all providers
    /// </summary>
    Task<InvalidationSystemHealth> CheckSystemHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 특정 프로바이더의 상태 확인
    /// Check health of a specific provider
    /// </summary>
    Task<ProviderHealthStatus> CheckProviderHealthAsync(string providerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 무효화 엔진 상태 확인
    /// Check invalidation engine health
    /// </summary>
    Task<InvalidationEngineHealth> CheckEngineHealthAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Metrics Collection

    /// <summary>
    /// 현재 통합 메트릭 수집 (캐시 + 무효화)
    /// Collect current unified metrics (cache + invalidation)
    /// </summary>
    Task<InvalidationMetrics> CollectMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 프로바이더별 상세 메트릭 수집
    /// Collect detailed metrics per provider
    /// </summary>
    Task<Dictionary<string, ProviderMetrics>> CollectProviderMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 시계열 메트릭 데이터 조회
    /// Get time-series metrics data
    /// </summary>
    Task<IEnumerable<InvalidationMetrics>> GetMetricsHistoryAsync(
        DateTime startTime, 
        DateTime endTime, 
        TimeSpan interval,
        CancellationToken cancellationToken = default);

    #endregion

    #region Performance Monitoring

    /// <summary>
    /// 무효화 성능 통계 조회
    /// Get invalidation performance statistics
    /// </summary>
    Task<InvalidationPerformanceStats> GetPerformanceStatsAsync(
        TimeSpan period, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hot Key 분석 (가장 자주 무효화되는 키들)
    /// Analyze hot keys (most frequently invalidated keys)
    /// </summary>
    Task<IEnumerable<HotKeyAnalysis>> GetHotKeysAnalysisAsync(
        TimeSpan period, 
        int topCount = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 무효화 패턴 분석
    /// Analyze invalidation patterns
    /// </summary>
    Task<InvalidationPatternAnalysis> AnalyzeInvalidationPatternsAsync(
        TimeSpan period,
        CancellationToken cancellationToken = default);

    #endregion

    #region Event Recording

    /// <summary>
    /// 무효화 이벤트 기록
    /// Record invalidation event
    /// </summary>
    Task RecordInvalidationEventAsync(InvalidationEvent invalidationEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 작업 이벤트 기록
    /// Record cache operation event
    /// </summary>
    Task RecordCacheEventAsync(CacheOperationEvent cacheEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// 성능 메트릭 이벤트 기록
    /// Record performance metric event
    /// </summary>
    Task RecordPerformanceEventAsync(PerformanceEvent performanceEvent, CancellationToken cancellationToken = default);

    #endregion

    #region Alerting

    /// <summary>
    /// 알림 임계값 설정
    /// Configure alert thresholds
    /// </summary>
    Task ConfigureAlertsAsync(AlertConfiguration alertConfig, CancellationToken cancellationToken = default);

    /// <summary>
    /// 현재 활성 알림 조회
    /// Get currently active alerts
    /// </summary>
    Task<IEnumerable<ActiveAlert>> GetActiveAlertsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 알림 이벤트 구독
    /// Subscribe to alert events
    /// </summary>
    event EventHandler<AlertTriggeredEventArgs> AlertTriggered;

    #endregion
}

/// <summary>
/// 시스템 전체 상태 정보
/// Overall system health information
/// </summary>
public class InvalidationSystemHealth
{
    public bool IsHealthy { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan ResponseTime { get; init; }
    public int TotalProviders { get; init; }
    public int HealthyProviders { get; init; }
    public int UnhealthyProviders { get; init; }
    public Dictionary<string, ProviderHealthStatus> ProviderStatuses { get; init; } = new();
    public InvalidationEngineHealth EngineHealth { get; init; } = new();
    public List<string> Issues { get; init; } = new();
}

/// <summary>
/// 프로바이더 상태 정보
/// Provider health status information
/// </summary>
public class ProviderHealthStatus
{
    public string ProviderName { get; init; } = string.Empty;
    public CacheProviderType ProviderType { get; init; }
    public bool IsHealthy { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan ResponseTime { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public Dictionary<string, object> Details { get; init; } = new();
}

/// <summary>
/// 무효화 엔진 상태 정보
/// Invalidation engine health information
/// </summary>
public class InvalidationEngineHealth
{
    public bool IsHealthy { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan Uptime { get; init; }
    public int ActiveStrategies { get; init; }
    public int ActiveRules { get; init; }
    public long TrackedKeys { get; init; }
    public long TotalInvalidations { get; init; }
    public long FailedInvalidations { get; init; }
    public double SuccessRate { get; init; }
    public List<string> Issues { get; init; } = new();
}

/// <summary>
/// 통합 무효화 메트릭
/// Unified invalidation metrics
/// </summary>
public class InvalidationMetrics
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    
    // Cache metrics
    public long TotalCacheHits { get; init; }
    public long TotalCacheMisses { get; init; }
    public double CacheHitRatio { get; init; }
    public long TotalCacheOperations { get; init; }
    
    // Invalidation metrics
    public long TotalInvalidations { get; init; }
    public long SuccessfulInvalidations { get; init; }
    public long FailedInvalidations { get; init; }
    public double InvalidationSuccessRate { get; init; }
    
    // Performance metrics
    public TimeSpan AverageCacheResponseTime { get; init; }
    public TimeSpan AverageInvalidationTime { get; init; }
    public long TotalMemoryUsage { get; init; }
    public long TotalKeys { get; init; }
    
    // Provider-specific metrics
    public Dictionary<string, ProviderMetrics> ProviderMetrics { get; init; } = new();
    
    // Additional metrics
    public Dictionary<string, object> AdditionalMetrics { get; init; } = new();
}

/// <summary>
/// 프로바이더별 메트릭
/// Provider-specific metrics
/// </summary>
public class ProviderMetrics
{
    public string ProviderName { get; init; } = string.Empty;
    public CacheProviderType ProviderType { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    
    // Basic metrics
    public long Hits { get; init; }
    public long Misses { get; init; }
    public double HitRatio { get; init; }
    public long TotalKeys { get; init; }
    public long MemoryUsage { get; init; }
    
    // Invalidation metrics
    public long InvalidationsPerformed { get; init; }
    public long InvalidationsFailed { get; init; }
    public TimeSpan AverageInvalidationTime { get; init; }
    
    // Connection metrics
    public bool IsConnected { get; init; }
    public TimeSpan ConnectionUptime { get; init; }
    public long ConnectionFailures { get; init; }
    
    // Custom metrics
    public Dictionary<string, object> CustomMetrics { get; init; } = new();
}

/// <summary>
/// 무효화 성능 통계
/// Invalidation performance statistics
/// </summary>
public class InvalidationPerformanceStats
{
    public TimeSpan Period { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    
    // Throughput metrics
    public long TotalInvalidations { get; init; }
    public double InvalidationsPerSecond { get; init; }
    public double PeakInvalidationsPerSecond { get; init; }
    
    // Latency metrics
    public TimeSpan AverageLatency { get; init; }
    public TimeSpan MedianLatency { get; init; }
    public TimeSpan P95Latency { get; init; }
    public TimeSpan P99Latency { get; init; }
    public TimeSpan MaxLatency { get; init; }
    
    // Error metrics
    public long TotalErrors { get; init; }
    public double ErrorRate { get; init; }
    public Dictionary<string, long> ErrorsByType { get; init; } = new();
    
    // Pattern metrics
    public Dictionary<string, long> InvalidationsByType { get; init; } = new();
    public Dictionary<string, long> InvalidationsByPattern { get; init; } = new();
    public Dictionary<string, TimeSpan> AverageLatencyByProvider { get; init; } = new();
}

/// <summary>
/// Hot Key 분석 결과
/// Hot key analysis result
/// </summary>
public class HotKeyAnalysis
{
    public string Key { get; init; } = string.Empty;
    public long InvalidationCount { get; init; }
    public double InvalidationsPerHour { get; init; }
    public TimeSpan FirstSeen { get; init; }
    public TimeSpan LastSeen { get; init; }
    public string Pattern { get; init; } = string.Empty;
    public List<string> RelatedKeys { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// 무효화 패턴 분석 결과
/// Invalidation pattern analysis result
/// </summary>
public class InvalidationPatternAnalysis
{
    public TimeSpan AnalysisPeriod { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    
    // Pattern frequency analysis
    public Dictionary<string, long> PatternFrequency { get; init; } = new();
    public Dictionary<string, TimeSpan> PatternAverageLatency { get; init; } = new();
    public Dictionary<string, double> PatternSuccessRate { get; init; } = new();
    
    // Time-based patterns
    public Dictionary<int, long> InvalidationsByHour { get; init; } = new(); // Hour of day -> count
    public Dictionary<DayOfWeek, long> InvalidationsByDayOfWeek { get; init; } = new();
    
    // Cascade analysis
    public Dictionary<string, List<string>> CascadePatterns { get; init; } = new(); // Table -> Related tables
    public Dictionary<string, double> CascadeEfficiency { get; init; } = new(); // Pattern -> efficiency score
    
    // Recommendations
    public List<string> Recommendations { get; init; } = new();
}

/// <summary>
/// 무효화 이벤트 정보
/// Invalidation event information
/// </summary>
public class InvalidationEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public InvalidationType Type { get; init; }
    public string Target { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public bool Success { get; init; }
    public long AffectedKeys { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// 캐시 작업 이벤트 정보
/// Cache operation event information  
/// </summary>
public class CacheOperationEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public CacheOperationType OperationType { get; init; }
    public string Key { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public bool Success { get; init; }
    public long? DataSize { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// 캐시 작업 타입
/// Cache operation type
/// </summary>
public enum CacheOperationType
{
    Get,
    Set,
    Remove,
    Exists,
    GetMany,
    SetMany,
    RemoveMany,
    Scan
}

/// <summary>
/// 성능 이벤트 정보
/// Performance event information
/// </summary>
public class PerformanceEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string Category { get; init; } = string.Empty; // "Cache", "Invalidation", "Connection", etc.
    public string MetricName { get; init; } = string.Empty;
    public double Value { get; init; }
    public string Unit { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public Dictionary<string, object> Tags { get; init; } = new();
}

/// <summary>
/// 알림 설정
/// Alert configuration
/// </summary>
public class AlertConfiguration
{
    public Dictionary<string, AlertThreshold> Thresholds { get; init; } = new();
    public TimeSpan EvaluationInterval { get; init; } = TimeSpan.FromMinutes(1);
    public List<string> NotificationChannels { get; init; } = new();
}

/// <summary>
/// 알림 임계값
/// Alert threshold
/// </summary>
public class AlertThreshold
{
    public string MetricName { get; init; } = string.Empty;
    public double WarningThreshold { get; init; }
    public double CriticalThreshold { get; init; }
    public AlertComparisonType ComparisonType { get; init; } = AlertComparisonType.GreaterThan;
    public TimeSpan EvaluationWindow { get; init; } = TimeSpan.FromMinutes(5);
    public int ConsecutiveFailures { get; init; } = 1;
}

/// <summary>
/// 알림 비교 타입
/// Alert comparison type
/// </summary>
public enum AlertComparisonType
{
    GreaterThan,
    LessThan,
    Equals,
    NotEquals
}

/// <summary>
/// 활성 알림
/// Active alert
/// </summary>
public class ActiveAlert
{
    public string AlertId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string MetricName { get; init; } = string.Empty;
    public AlertSeverity Severity { get; init; }
    public DateTime TriggeredAt { get; init; } = DateTime.UtcNow;
    public string Message { get; init; } = string.Empty;
    public double CurrentValue { get; init; }
    public double ThresholdValue { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public Dictionary<string, object> Details { get; init; } = new();
}

/// <summary>
/// 알림 심각도
/// Alert severity
/// </summary>
public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// 알림 발생 이벤트 인자
/// Alert triggered event arguments
/// </summary>
public class AlertTriggeredEventArgs : EventArgs
{
    public ActiveAlert Alert { get; init; } = new();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}