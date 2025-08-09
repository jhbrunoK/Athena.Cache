namespace Athena.Invalidation.CQRS.Abstractions;

/// <summary>
/// 도메인 이벤트 기반 캐시 무효화 처리 인터페이스
/// </summary>
public interface IEventDrivenInvalidation
{
    /// <summary>
    /// 도메인 이벤트 발생 시 캐시 무효화 처리
    /// </summary>
    Task HandleEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;

    /// <summary>
    /// 특정 이벤트 타입을 처리할 수 있는지 확인
    /// </summary>
    bool CanHandle<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent;
    
    /// <summary>
    /// 이벤트 핸들러 등록
    /// </summary>
    void RegisterEventHandler<TEvent>(IEventInvalidationHandler<TEvent> handler) 
        where TEvent : IDomainEvent;
    
    /// <summary>
    /// 이벤트 핸들러 제거
    /// </summary>
    void UnregisterEventHandler<TEvent>() where TEvent : IDomainEvent;
    
    /// <summary>
    /// 등록된 모든 이벤트 핸들러 조회
    /// </summary>
    IEnumerable<Type> GetRegisteredEventTypes();
}

/// <summary>
/// 특정 도메인 이벤트에 대한 무효화 핸들러
/// </summary>
public interface IEventInvalidationHandler<in TEvent> where TEvent : IDomainEvent
{
    /// <summary>
    /// 이벤트에서 무효화할 테이블들 결정
    /// </summary>
    Task<IEnumerable<string>> GetInvalidationTablesAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 이벤트에서 무효화할 패턴들 결정
    /// </summary>
    Task<IEnumerable<string>> GetInvalidationPatternsAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 이벤트에서 계층적 무효화 대상들 결정
    /// </summary>
    Task<IEnumerable<HierarchicalInvalidationTarget>> GetHierarchicalTargetsAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 무효화 우선순위 (높을수록 먼저 처리)
    /// </summary>
    int Priority { get; }
}

/// <summary>
/// 계층적 무효화 대상
/// </summary>
public class HierarchicalInvalidationTarget
{
    public string RootTable { get; set; } = string.Empty;
    public string[] RelatedTables { get; set; } = Array.Empty<string>();
    public int MaxDepth { get; set; } = 3;
    public Dictionary<string, object> Properties { get; set; } = new();
}