namespace Invalidus.CQRS.Abstractions;

/// <summary>
/// 무효화 이벤트 마커 인터페이스
/// Invalidation event marker interface
/// </summary>
public interface IInvalidationEvent
{
    /// <summary>이벤트 ID</summary>
    string EventId { get; }
    
    /// <summary>이벤트 유형</summary>
    string EventType { get; }
    
    /// <summary>타임스탬프</summary>
    DateTime Timestamp { get; }
    
    /// <summary>이벤트 버전</summary>
    int Version { get; }
    
    /// <summary>상관관계 ID</summary>
    string? CorrelationId { get; }
    
    /// <summary>원인 ID (연쇄 이벤트의 경우)</summary>
    string? CausationId { get; }
    
    /// <summary>메타데이터</summary>
    Dictionary<string, object> Metadata { get; }
}

/// <summary>
/// 기본 무효화 이벤트 구현체
/// Base invalidation event implementation
/// </summary>
public abstract record InvalidationEvent : IInvalidationEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString();
    public abstract string EventType { get; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int Version { get; init; } = 1;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// 도메인 엔티티 변경 이벤트
/// Domain entity changed event
/// </summary>
public record EntityChangedEvent : InvalidationEvent
{
    public override string EventType => "EntityChanged";
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public required string ChangeType { get; init; } // Created, Updated, Deleted
    public List<string>? ChangedProperties { get; init; }
    public Dictionary<string, object>? PreviousValues { get; init; }
    public Dictionary<string, object>? NewValues { get; init; }
}

/// <summary>
/// 집계 루트 변경 이벤트
/// Aggregate root changed event
/// </summary>
public record AggregateChangedEvent : InvalidationEvent
{
    public override string EventType => "AggregateChanged";
    public required string AggregateType { get; init; }
    public required string AggregateId { get; init; }
    public required string ChangeType { get; init; }
    public long AggregateVersion { get; init; }
    public List<string>? AffectedReadModels { get; init; }
    public List<string>? AffectedProjections { get; init; }
}

/// <summary>
/// Read Model 업데이트 이벤트
/// Read model updated event
/// </summary>
public record ReadModelUpdatedEvent : InvalidationEvent
{
    public override string EventType => "ReadModelUpdated";
    public required string ReadModelType { get; init; }
    public required string ReadModelId { get; init; }
    public string? UpdateReason { get; init; }
    public List<string>? UpdatedFields { get; init; }
    public bool RequiresCacheInvalidation { get; init; } = true;
}

/// <summary>
/// Projection 갱신 이벤트
/// Projection updated event
/// </summary>
public record ProjectionUpdatedEvent : InvalidationEvent
{
    public override string EventType => "ProjectionUpdated";
    public required string ProjectionName { get; init; }
    public string? PartitionKey { get; init; }
    public required string UpdateType { get; init; } // Incremental, Full, Rebuild
    public DateTime? LastProjectedEventTimestamp { get; init; }
    public long ProcessedEventCount { get; init; }
}

/// <summary>
/// 무효화 완료 이벤트
/// Invalidation completed event
/// </summary>
public record InvalidationCompletedEvent : InvalidationEvent
{
    public override string EventType => "InvalidationCompleted";
    public required string InvalidationId { get; init; }
    public required string InvalidationType { get; init; }
    public required List<string> InvalidatedKeys { get; init; }
    public int TotalKeysInvalidated { get; init; }
    public TimeSpan Duration { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 집계 무효화 이벤트
/// Batch invalidation event
/// </summary>
public record BatchInvalidationEvent : InvalidationEvent
{
    public override string EventType => "BatchInvalidation";
    public required List<IInvalidationCommand> Commands { get; init; }
    public int TotalCommands { get; init; }
    public int SuccessfulCommands { get; init; }
    public int FailedCommands { get; init; }
    public TimeSpan BatchDuration { get; init; }
    public Dictionary<string, string>? Errors { get; init; }
}