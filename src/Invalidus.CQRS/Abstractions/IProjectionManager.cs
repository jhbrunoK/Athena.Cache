namespace Invalidus.CQRS.Abstractions;

/// <summary>
/// Projection 관리자 인터페이스
/// Projection manager interface
/// </summary>
public interface IProjectionManager
{
    /// <summary>Projection 무효화</summary>
    Task InvalidateProjectionAsync(string projectionName, string? partitionKey = null, CancellationToken cancellationToken = default);
    
    /// <summary>Projection 갱신</summary>
    Task UpdateProjectionAsync(string projectionName, IInvalidationEvent eventData, CancellationToken cancellationToken = default);
    
    /// <summary>Projection 재구성</summary>
    Task RebuildProjectionAsync(string projectionName, DateTime? fromTimestamp = null, CancellationToken cancellationToken = default);
    
    /// <summary>Projection 일괄 갱신</summary>
    Task UpdateProjectionsBatchAsync(string projectionName, IEnumerable<IInvalidationEvent> events, CancellationToken cancellationToken = default);
    
    /// <summary>Projection 상태 조회</summary>
    Task<ProjectionStatus> GetProjectionStatusAsync(string projectionName, CancellationToken cancellationToken = default);
    
    /// <summary>모든 Projection 상태 조회</summary>
    Task<IEnumerable<ProjectionStatus>> GetAllProjectionStatusAsync(CancellationToken cancellationToken = default);
    
    /// <summary>Projection 등록</summary>
    Task RegisterProjectionAsync(ProjectionDefinition definition, CancellationToken cancellationToken = default);
    
    /// <summary>Projection 등록 해제</summary>
    Task UnregisterProjectionAsync(string projectionName, CancellationToken cancellationToken = default);
    
    /// <summary>Projection 일시 중지</summary>
    Task PauseProjectionAsync(string projectionName, CancellationToken cancellationToken = default);
    
    /// <summary>Projection 재시작</summary>
    Task ResumeProjectionAsync(string projectionName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Projection 정의
/// Projection definition
/// </summary>
public record ProjectionDefinition
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required List<string> EventTypes { get; init; }
    public string? PartitionKey { get; init; }
    public ProjectionMode Mode { get; init; } = ProjectionMode.Continuous;
    public Dictionary<string, object>? Configuration { get; init; }
    public List<string>? CachePatterns { get; init; }
    public bool AutoInvalidateCache { get; init; } = true;
    public TimeSpan? InvalidationDelay { get; init; }
}

/// <summary>
/// Projection 모드
/// Projection mode
/// </summary>
public enum ProjectionMode
{
    /// <summary>연속 처리</summary>
    Continuous,
    /// <summary>배치 처리</summary>
    Batch,
    /// <summary>온디맨드 처리</summary>
    OnDemand
}

/// <summary>
/// Projection 상태
/// Projection status
/// </summary>
public record ProjectionStatus
{
    public required string Name { get; init; }
    public ProjectionState State { get; init; } = ProjectionState.Stopped;
    public DateTime? LastProcessedEventTimestamp { get; init; }
    public long LastProcessedEventPosition { get; init; }
    public long TotalEventsProcessed { get; init; }
    public DateTime? LastUpdateTime { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, object>? Statistics { get; init; }
    public List<string>? PendingInvalidations { get; init; }
}

/// <summary>
/// Projection 상태
/// Projection state
/// </summary>
public enum ProjectionState
{
    /// <summary>중지됨</summary>
    Stopped,
    /// <summary>시작 중</summary>
    Starting,
    /// <summary>실행 중</summary>
    Running,
    /// <summary>일시 중지됨</summary>
    Paused,
    /// <summary>재구성 중</summary>
    Rebuilding,
    /// <summary>오류</summary>
    Faulted
}

/// <summary>
/// Projection 처리기 인터페이스
/// Projection processor interface
/// </summary>
public interface IProjectionProcessor
{
    /// <summary>지원하는 이벤트 타입들</summary>
    IEnumerable<Type> SupportedEventTypes { get; }
    
    /// <summary>이벤트 처리</summary>
    Task<ProjectionUpdateResult> ProcessEventAsync(IInvalidationEvent eventData, CancellationToken cancellationToken = default);
    
    /// <summary>Projection 재구성</summary>
    Task<ProjectionRebuildResult> RebuildAsync(DateTime? fromTimestamp = null, CancellationToken cancellationToken = default);
    
    /// <summary>Projection 상태 조회</summary>
    Task<ProjectionStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Projection 갱신 결과
/// Projection update result
/// </summary>
public record ProjectionUpdateResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public List<string>? InvalidatedCacheKeys { get; init; }
    public TimeSpan ProcessingTime { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Projection 재구성 결과
/// Projection rebuild result
/// </summary>
public record ProjectionRebuildResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public long ProcessedEvents { get; init; }
    public TimeSpan RebuildTime { get; init; }
    public List<string>? InvalidatedCacheKeys { get; init; }
    public Dictionary<string, object>? Statistics { get; init; }
}