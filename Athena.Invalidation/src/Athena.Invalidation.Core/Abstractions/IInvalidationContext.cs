namespace Athena.Invalidation.Core.Abstractions;

/// <summary>
/// 무효화 실행 컨텍스트를 나타내는 인터페이스
/// 무효화 요청의 상세 정보와 실행 환경을 담고 있음
/// </summary>
public interface IInvalidationContext
{
    /// <summary>
    /// 컨텍스트 고유 식별자
    /// </summary>
    string ContextId { get; }

    /// <summary>
    /// 무효화 트리거 정보
    /// </summary>
    InvalidationTrigger Trigger { get; }

    /// <summary>
    /// 무효화 대상 테이블/패턴/키
    /// </summary>
    string Target { get; set; }

    /// <summary>
    /// 무효화 타입
    /// </summary>
    InvalidationType Type { get; set; }

    /// <summary>
    /// 트리거 발생 시각
    /// </summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>
    /// 추가 메타데이터
    /// </summary>
    Dictionary<string, object> Metadata { get; }

    /// <summary>
    /// 사용 가능한 캐시 프로바이더들
    /// </summary>
    IEnumerable<ICacheProvider> CacheProviders { get; }

    /// <summary>
    /// 실행 우선순위
    /// </summary>
    int Priority { get; set; }

    /// <summary>
    /// 최대 실행 시간 (타임아웃)
    /// </summary>
    TimeSpan? Timeout { get; set; }

    /// <summary>
    /// 재시도 횟수
    /// </summary>
    int MaxRetries { get; set; }

    /// <summary>
    /// 컨텍스트에 메타데이터 추가
    /// </summary>
    void AddMetadata(string key, object value);

    /// <summary>
    /// 컨텍스트에서 메타데이터 조회
    /// </summary>
    T? GetMetadata<T>(string key, T? defaultValue = default);

    /// <summary>
    /// 컨텍스트 복제 (하위 작업용)
    /// </summary>
    IInvalidationContext Clone();
}

/// <summary>
/// 무효화 트리거 정보
/// </summary>
public class InvalidationTrigger
{
    public string Source { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? SessionId { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
    
    public static InvalidationTrigger Manual(string source, string action, string? userId = null)
        => new() { Source = source, Action = action, UserId = userId };
    
    public static InvalidationTrigger Automatic(string source, string action)
        => new() { Source = source, Action = action };
    
    public static InvalidationTrigger System(string action)
        => new() { Source = "System", Action = action };
}

/// <summary>
/// 무효화 타입
/// </summary>
public enum InvalidationType
{
    Table,
    Pattern,
    Key,
    Batch,
    Hierarchy,
    Custom
}