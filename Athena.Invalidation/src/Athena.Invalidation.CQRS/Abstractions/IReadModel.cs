namespace Athena.Invalidation.CQRS.Abstractions;

/// <summary>
/// CQRS 읽기 모델(Read Model)을 나타내는 인터페이스
/// </summary>
public interface IReadModel
{
    /// <summary>
    /// 읽기 모델 식별자
    /// </summary>
    string Id { get; }
    
    /// <summary>
    /// 마지막 업데이트 시각
    /// </summary>
    DateTimeOffset LastUpdated { get; }
    
    /// <summary>
    /// 읽기 모델 버전 (Optimistic Concurrency Control)
    /// </summary>
    long Version { get; }
    
    /// <summary>
    /// 이 읽기 모델과 연관된 테이블들
    /// </summary>
    IEnumerable<string> GetRelatedTables();
    
    /// <summary>
    /// 이 읽기 모델의 캐시 키들
    /// </summary>
    IEnumerable<string> GetCacheKeys();
}

/// <summary>
/// 프로젝션(Projection)을 나타내는 인터페이스
/// </summary>
public interface IProjection : IReadModel
{
    /// <summary>
    /// 프로젝션 타입/이름
    /// </summary>
    string ProjectionType { get; }
    
    /// <summary>
    /// 소스 이벤트들
    /// </summary>
    IEnumerable<string> SourceEventTypes { get; }
    
    /// <summary>
    /// 프로젝션 재구성이 필요한지 확인
    /// </summary>
    bool NeedsRebuild(IDomainEvent domainEvent);
}

/// <summary>
/// 읽기 모델 캐시 무효화 처리 인터페이스
/// </summary>
public interface IReadModelInvalidator
{
    /// <summary>
    /// 특정 읽기 모델 타입의 캐시 무효화
    /// </summary>
    Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default)
        where TReadModel : class, IReadModel;
        
    /// <summary>
    /// 프로젝션 캐시 무효화
    /// </summary>
    Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default)
        where TProjection : class, IProjection;
    
    /// <summary>
    /// 도메인 이벤트를 기반으로 관련 읽기 모델들 무효화
    /// </summary>
    Task InvalidateByEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;
    
    /// <summary>
    /// 읽기 모델 의존성 그래프에 따른 연쇄 무효화
    /// </summary>
    Task InvalidateDependentModelsAsync<TReadModel>(string modelId, CancellationToken cancellationToken = default)
        where TReadModel : class, IReadModel;
        
    /// <summary>
    /// 읽기 모델 캐시 키 추적
    /// </summary>
    Task TrackReadModelAsync<TReadModel>(TReadModel readModel, CancellationToken cancellationToken = default)
        where TReadModel : class, IReadModel;
}