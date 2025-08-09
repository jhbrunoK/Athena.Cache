namespace Athena.Invalidation.Distributed.Abstractions;

/// <summary>
/// 분산 이벤트 버스 - 클러스터 노드 간 무효화 이벤트 전파
/// </summary>
public interface IDistributedEventBus
{
    /// <summary>
    /// 무효화 이벤트 발행
    /// </summary>
    Task PublishInvalidationEventAsync<T>(T invalidationEvent, CancellationToken cancellationToken = default)
        where T : class, IDistributedInvalidationEvent;

    /// <summary>
    /// 무효화 이벤트 구독 시작
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 무효화 이벤트 구독 중지
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 이벤트 핸들러 등록
    /// </summary>
    void Subscribe<T>(IDistributedInvalidationEventHandler<T> handler)
        where T : class, IDistributedInvalidationEvent;

    /// <summary>
    /// 이벤트 핸들러 등록 해제
    /// </summary>
    void Unsubscribe<T>(IDistributedInvalidationEventHandler<T> handler)
        where T : class, IDistributedInvalidationEvent;

    /// <summary>
    /// 연결 상태 확인
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 이벤트 버스 통계 정보
    /// </summary>
    Task<EventBusStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 분산 무효화 이벤트 기본 인터페이스
/// </summary>
public interface IDistributedInvalidationEvent
{
    /// <summary>이벤트 고유 식별자</summary>
    string EventId { get; }
    
    /// <summary>이벤트 타입</summary>
    string EventType { get; }
    
    /// <summary>발생 노드 식별자</summary>
    string SourceNodeId { get; set; }
    
    /// <summary>발생 시간</summary>
    DateTimeOffset Timestamp { get; }
    
    /// <summary>무효화 대상</summary>
    string Target { get; }
    
    /// <summary>무효화 타입</summary>
    InvalidationType Type { get; }
    
    /// <summary>추가 메타데이터</summary>
    Dictionary<string, object> Metadata { get; }
}

/// <summary>
/// 분산 무효화 이벤트 핸들러
/// </summary>
public interface IDistributedInvalidationEventHandler<in T>
    where T : class, IDistributedInvalidationEvent
{
    /// <summary>처리 우선순위</summary>
    int Priority { get; }
    
    /// <summary>이벤트 처리</summary>
    Task HandleAsync(T invalidationEvent, CancellationToken cancellationToken = default);
    
    /// <summary>처리 가능 여부 확인</summary>
    bool CanHandle(T invalidationEvent);
}

/// <summary>
/// 이벤트 버스 통계 정보
/// </summary>
public class EventBusStatistics
{
    public long PublishedEvents { get; set; }
    public long ConsumedEvents { get; set; }
    public long FailedEvents { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }
    public bool IsConnected { get; set; }
    public Dictionary<string, object> ProviderSpecificMetrics { get; set; } = new();
}

/// <summary>
/// 클러스터 노드 정보
/// </summary>
public class ClusterNode
{
    public string NodeId { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string EndPoint { get; set; } = string.Empty;
    public DateTimeOffset LastHeartbeat { get; set; }
    public NodeRole Role { get; set; }
    public NodeStatus Status { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}

/// <summary>
/// 노드 역할
/// </summary>
public enum NodeRole
{
    Primary,
    Secondary,
    Observer
}

/// <summary>
/// 노드 상태
/// </summary>
public enum NodeStatus
{
    Online,
    Offline,
    Degraded,
    Unknown
}