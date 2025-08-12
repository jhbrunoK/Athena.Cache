namespace Invalidus.CQRS.Abstractions;

/// <summary>
/// Read Model 관리자 인터페이스
/// Read model manager interface
/// </summary>
public interface IReadModelManager
{
    /// <summary>Read Model 무효화</summary>
    Task InvalidateReadModelAsync(string readModelType, string? aggregateId = null, CancellationToken cancellationToken = default);
    
    /// <summary>관련 Read Model들 무효화</summary>
    Task InvalidateRelatedReadModelsAsync(string entityType, string entityId, IEnumerable<string>? changedProperties = null, CancellationToken cancellationToken = default);
    
    /// <summary>Read Model 갱신</summary>
    Task RefreshReadModelAsync(string readModelType, string aggregateId, CancellationToken cancellationToken = default);
    
    /// <summary>Read Model 일괄 갱신</summary>
    Task RefreshReadModelsBatchAsync(IEnumerable<ReadModelRefreshRequest> requests, CancellationToken cancellationToken = default);
    
    /// <summary>Read Model 종속성 등록</summary>
    Task RegisterReadModelDependencyAsync(string readModelType, string dependsOnEntity, IEnumerable<string>? properties = null, CancellationToken cancellationToken = default);
    
    /// <summary>Read Model 종속성 조회</summary>
    Task<IEnumerable<ReadModelDependency>> GetReadModelDependenciesAsync(string entityType, CancellationToken cancellationToken = default);
    
    /// <summary>Read Model 메타데이터 조회</summary>
    Task<ReadModelMetadata?> GetReadModelMetadataAsync(string readModelType, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read Model 갱신 요청
/// Read model refresh request
/// </summary>
public record ReadModelRefreshRequest
{
    public required string ReadModelType { get; init; }
    public required string AggregateId { get; init; }
    public RefreshMode Mode { get; init; } = RefreshMode.Incremental;
    public Dictionary<string, object> Parameters { get; init; } = new();
}

/// <summary>
/// 갱신 모드
/// Refresh mode
/// </summary>
public enum RefreshMode
{
    /// <summary>증분 갱신</summary>
    Incremental,
    /// <summary>전체 갱신</summary>
    Full,
    /// <summary>재구성</summary>
    Rebuild
}

/// <summary>
/// Read Model 종속성
/// Read model dependency
/// </summary>
public record ReadModelDependency
{
    public required string ReadModelType { get; init; }
    public required string DependsOnEntity { get; init; }
    public List<string>? Properties { get; init; }
    public InvalidationStrategy Strategy { get; init; } = InvalidationStrategy.Immediate;
    public TimeSpan? Delay { get; init; }
    public bool CascadeInvalidation { get; init; } = true;
}

/// <summary>
/// 무효화 전략
/// Invalidation strategy
/// </summary>
public enum InvalidationStrategy
{
    /// <summary>즉시 무효화</summary>
    Immediate,
    /// <summary>지연 무효화</summary>
    Delayed,
    /// <summary>배치 무효화</summary>
    Batched,
    /// <summary>조건부 무효화</summary>
    Conditional
}

/// <summary>
/// Read Model 메타데이터
/// Read model metadata
/// </summary>
public record ReadModelMetadata
{
    public required string ReadModelType { get; init; }
    public string? Description { get; init; }
    public List<string>? CacheKeys { get; init; }
    public List<ReadModelDependency>? Dependencies { get; init; }
    public Dictionary<string, object>? Configuration { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastUpdatedAt { get; init; }
    public long Version { get; init; }
}