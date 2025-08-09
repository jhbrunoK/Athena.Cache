namespace Athena.Invalidation.CQRS.Abstractions;

/// <summary>
/// 도메인 이벤트를 나타내는 인터페이스
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// 이벤트 고유 식별자
    /// </summary>
    string EventId { get; }
    
    /// <summary>
    /// 이벤트 발생 시각
    /// </summary>
    DateTimeOffset OccurredAt { get; }
    
    /// <summary>
    /// 이벤트 타입 (클래스명 기반)
    /// </summary>
    string EventType { get; }
    
    /// <summary>
    /// 이벤트 버전
    /// </summary>
    int Version { get; }
    
    /// <summary>
    /// 집계 루트 ID
    /// </summary>
    string? AggregateId { get; }
    
    /// <summary>
    /// 이벤트 메타데이터
    /// </summary>
    Dictionary<string, object> Metadata { get; }
}

/// <summary>
/// 기본 도메인 이벤트 구현체
/// </summary>
public abstract class BaseDomainEvent : IDomainEvent
{
    protected BaseDomainEvent()
    {
        EventId = Guid.NewGuid().ToString("N")[..12];
        OccurredAt = DateTimeOffset.UtcNow;
        EventType = GetType().Name;
        Version = 1;
        Metadata = new Dictionary<string, object>();
    }

    public string EventId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string EventType { get; init; }
    public int Version { get; set; }
    public string? AggregateId { get; set; }
    public Dictionary<string, object> Metadata { get; init; }
}

/// <summary>
/// 집계 루트와 연관된 도메인 이벤트
/// </summary>
public abstract class AggregateEvent : BaseDomainEvent
{
    protected AggregateEvent(string aggregateId) : base()
    {
        AggregateId = aggregateId ?? throw new ArgumentNullException(nameof(aggregateId));
    }
}