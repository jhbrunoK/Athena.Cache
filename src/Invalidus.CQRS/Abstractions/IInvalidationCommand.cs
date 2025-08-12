namespace Invalidus.CQRS.Abstractions;

/// <summary>
/// 무효화 명령 마커 인터페이스
/// Invalidation command marker interface
/// </summary>
public interface IInvalidationCommand
{
    /// <summary>명령 ID</summary>
    string CommandId { get; }
    
    /// <summary>타임스탬프</summary>
    DateTime Timestamp { get; }
    
    /// <summary>명령 유형</summary>
    string CommandType { get; }
    
    /// <summary>메타데이터</summary>
    Dictionary<string, object> Metadata { get; }
}

/// <summary>
/// 기본 무효화 명령 구현체
/// Base invalidation command implementation
/// </summary>
public abstract record InvalidationCommand : IInvalidationCommand
{
    public string CommandId { get; init; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public abstract string CommandType { get; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// 테이블 무효화 명령
/// Table invalidation command
/// </summary>
public record InvalidateTableCommand : InvalidationCommand
{
    public override string CommandType => "InvalidateTable";
    public required string TableName { get; init; }
    public string? Schema { get; init; }
    public List<string>? Columns { get; init; }
    public TimeSpan? Delay { get; init; }
}

/// <summary>
/// 패턴 무효화 명령
/// Pattern invalidation command
/// </summary>
public record InvalidatePatternCommand : InvalidationCommand
{
    public override string CommandType => "InvalidatePattern";
    public required string Pattern { get; init; }
    public string? ProviderName { get; init; }
    public bool CascadeInvalidation { get; init; } = true;
}

/// <summary>
/// 계층적 무효화 명령
/// Hierarchical invalidation command
/// </summary>
public record InvalidateHierarchyCommand : InvalidationCommand
{
    public override string CommandType => "InvalidateHierarchy";
    public required string RootKey { get; init; }
    public int MaxDepth { get; init; } = 10;
    public List<string>? ExcludePatterns { get; init; }
}

/// <summary>
/// Read Model 무효화 명령
/// Read model invalidation command
/// </summary>
public record InvalidateReadModelCommand : InvalidationCommand
{
    public override string CommandType => "InvalidateReadModel";
    public required string ReadModelType { get; init; }
    public string? AggregateId { get; init; }
    public List<string>? RelatedEntities { get; init; }
    public bool InvalidateProjections { get; init; } = true;
}

/// <summary>
/// Projection 무효화 명령
/// Projection invalidation command
/// </summary>
public record InvalidateProjectionCommand : InvalidationCommand
{
    public override string CommandType => "InvalidateProjection";
    public required string ProjectionName { get; init; }
    public string? PartitionKey { get; init; }
    public Dictionary<string, object>? FilterCriteria { get; init; }
    public bool RebuildProjection { get; init; } = false;
}