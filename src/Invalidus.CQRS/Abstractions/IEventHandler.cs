namespace Invalidus.CQRS.Abstractions;

/// <summary>
/// 이벤트 처리기 인터페이스
/// Event handler interface
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : IInvalidationEvent
{
    /// <summary>이벤트를 비동기적으로 처리</summary>
    Task HandleAsync(TEvent eventData, CancellationToken cancellationToken = default);
}

/// <summary>
/// 이벤트 발행기 인터페이스
/// Event publisher interface
/// </summary>
public interface IEventPublisher
{
    /// <summary>단일 이벤트 발행</summary>
    Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default) 
        where TEvent : IInvalidationEvent;
    
    /// <summary>복수 이벤트 배치 발행</summary>
    Task PublishBatchAsync(IEnumerable<IInvalidationEvent> events, CancellationToken cancellationToken = default);
    
    /// <summary>이벤트 스트림에 발행</summary>
    Task PublishToStreamAsync(string streamName, IInvalidationEvent eventData, CancellationToken cancellationToken = default);
}

/// <summary>
/// 도메인 이벤트 저장소 인터페이스
/// Domain event store interface
/// </summary>
public interface IEventStore
{
    /// <summary>이벤트 저장</summary>
    Task SaveEventAsync(IInvalidationEvent eventData, CancellationToken cancellationToken = default);
    
    /// <summary>이벤트 배치 저장</summary>
    Task SaveEventsAsync(IEnumerable<IInvalidationEvent> events, CancellationToken cancellationToken = default);
    
    /// <summary>특정 집계의 이벤트 조회</summary>
    Task<IEnumerable<IInvalidationEvent>> GetEventsAsync(string aggregateId, DateTime? fromTimestamp = null, CancellationToken cancellationToken = default);
    
    /// <summary>이벤트 스트림 조회</summary>
    Task<IEnumerable<IInvalidationEvent>> GetEventStreamAsync(string streamName, long fromVersion = 0, int maxEvents = 1000, CancellationToken cancellationToken = default);
    
    /// <summary>이벤트 타입별 조회</summary>
    Task<IEnumerable<TEvent>> GetEventsByTypeAsync<TEvent>(DateTime? fromTimestamp = null, int maxEvents = 1000, CancellationToken cancellationToken = default) 
        where TEvent : IInvalidationEvent;
}

/// <summary>
/// 이벤트 구독자 인터페이스
/// Event subscriber interface
/// </summary>
public interface IEventSubscriber
{
    /// <summary>특정 이벤트 타입 구독</summary>
    Task SubscribeAsync<TEvent>(Func<TEvent, CancellationToken, Task> handler, CancellationToken cancellationToken = default) 
        where TEvent : IInvalidationEvent;
    
    /// <summary>이벤트 스트림 구독</summary>
    Task SubscribeToStreamAsync(string streamName, Func<IInvalidationEvent, CancellationToken, Task> handler, CancellationToken cancellationToken = default);
    
    /// <summary>모든 이벤트 구독</summary>
    Task SubscribeAllAsync(Func<IInvalidationEvent, CancellationToken, Task> handler, CancellationToken cancellationToken = default);
    
    /// <summary>구독 해제</summary>
    Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken = default);
}