namespace Invalidus.Core.Abstractions;

/// <summary>
/// 무효화 실행 컨텍스트 - 무효화 작업의 상황 정보를 담고 있는 컨텍스트
/// Invalidation execution context containing situational information about the invalidation operation
/// </summary>
public interface IInvalidationContext
{
    /// <summary>
    /// 컨텍스트 고유 식별자
    /// Unique identifier for the context
    /// </summary>
    string ContextId { get; }

    /// <summary>
    /// 무효화 트리거 정보
    /// Information about what triggered the invalidation
    /// </summary>
    InvalidationTrigger Trigger { get; }

    /// <summary>
    /// 무효화 대상 (테이블/패턴/키)
    /// Target of invalidation (table/pattern/key)
    /// </summary>
    string Target { get; set; }

    /// <summary>
    /// 무효화 타입
    /// Type of invalidation
    /// </summary>
    InvalidationType Type { get; set; }

    /// <summary>
    /// 트리거 발생 시각
    /// When the trigger occurred
    /// </summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>
    /// 추가 메타데이터
    /// Additional metadata
    /// </summary>
    Dictionary<string, object> Metadata { get; }

    /// <summary>
    /// 사용 가능한 캐시 프로바이더들
    /// Available cache providers for this operation
    /// </summary>
    IEnumerable<ICacheProvider> CacheProviders { get; set; }

    /// <summary>
    /// 무효화 우선순위 (높을수록 먼저 실행)
    /// Invalidation priority (higher values execute first)
    /// </summary>
    int Priority { get; set; }

    /// <summary>
    /// 타임아웃 설정
    /// Timeout configuration
    /// </summary>
    TimeSpan Timeout { get; set; }

    /// <summary>
    /// 최대 재시도 횟수
    /// Maximum retry attempts
    /// </summary>
    int MaxRetries { get; set; }

    /// <summary>
    /// 분산 환경에서의 전파 여부
    /// Whether to propagate in distributed environments
    /// </summary>
    bool ShouldPropagate { get; set; }

    /// <summary>
    /// 메타데이터 추가
    /// Add metadata to the context
    /// </summary>
    void AddMetadata(string key, object value);

    /// <summary>
    /// 메타데이터 조회
    /// Get metadata from the context
    /// </summary>
    T? GetMetadata<T>(string key);

    /// <summary>
    /// 컨텍스트 복사본 생성
    /// Create a copy of the context
    /// </summary>
    IInvalidationContext Clone();
}

/// <summary>
/// 무효화 타입 열거형
/// Types of invalidation operations
/// </summary>
public enum InvalidationType
{
    /// <summary>테이블 기반 무효화</summary>
    Table,
    
    /// <summary>패턴 기반 무효화</summary>
    Pattern,
    
    /// <summary>정확한 키 무효화</summary>
    Key,
    
    /// <summary>배치 무효화</summary>
    Batch,
    
    /// <summary>계층적 무효화</summary>
    Hierarchy,
    
    /// <summary>이벤트 기반 무효화</summary>
    Event,
    
    /// <summary>명령 기반 무효화</summary>
    Command,
    
    /// <summary>사용자 정의 무효화</summary>
    Custom,
    
    /// <summary>전체 클리어</summary>
    ClearAll
}

/// <summary>
/// 기본 무효화 컨텍스트 구현
/// Default invalidation context implementation
/// </summary>
public class InvalidationContext : IInvalidationContext
{
    public string ContextId { get; } = Guid.NewGuid().ToString("N")[..12];
    public InvalidationTrigger Trigger { get; init; }
    public string Target { get; set; } = string.Empty;
    public InvalidationType Type { get; set; } = InvalidationType.Custom;
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
    public Dictionary<string, object> Metadata { get; } = new();
    public IEnumerable<ICacheProvider> CacheProviders { get; set; } = Enumerable.Empty<ICacheProvider>();
    public int Priority { get; set; } = 0;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; set; } = 3;
    public bool ShouldPropagate { get; set; } = true;

    public InvalidationContext(InvalidationTrigger trigger)
    {
        Trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
    }

    public void AddMetadata(string key, object value)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));
        Metadata[key] = value;
    }

    public T? GetMetadata<T>(string key)
    {
        if (string.IsNullOrEmpty(key)) return default;
        
        return Metadata.TryGetValue(key, out var value) && value is T typedValue 
            ? typedValue 
            : default;
    }

    public IInvalidationContext Clone()
    {
        var cloned = new InvalidationContext(Trigger)
        {
            Target = Target,
            Type = Type,
            CacheProviders = CacheProviders,
            Priority = Priority,
            Timeout = Timeout,
            MaxRetries = MaxRetries,
            ShouldPropagate = ShouldPropagate
        };

        foreach (var kvp in Metadata)
        {
            cloned.Metadata[kvp.Key] = kvp.Value;
        }

        return cloned;
    }

    public override string ToString()
    {
        return $"InvalidationContext[{ContextId}]: {Type} - {Target} (Trigger: {Trigger.Source}/{Trigger.EventType})";
    }
}