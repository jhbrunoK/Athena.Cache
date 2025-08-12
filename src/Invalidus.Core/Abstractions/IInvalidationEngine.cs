namespace Invalidus.Core.Abstractions;

/// <summary>
/// 범용 캐시 무효화 엔진 - 모든 캐시 시스템의 무효화를 통합 관리
/// Universal Cache Invalidation Engine that works with any cache provider
/// </summary>
public interface IInvalidationEngine
{
    #region Basic Invalidation Operations
    
    /// <summary>
    /// 테이블 기반 무효화 - 특정 테이블과 연관된 모든 캐시 제거
    /// Table-based invalidation - removes all cache entries related to a specific table
    /// </summary>
    Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 패턴 기반 무효화 - 패턴에 맞는 캐시 키들 제거 (예: "user:*", "product:123:*")
    /// Pattern-based invalidation - removes cache keys matching the pattern
    /// </summary>
    Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// 정확한 키 무효화 - 특정 캐시 키만 제거
    /// Exact key invalidation - removes a specific cache key only
    /// </summary>
    Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default);

    #endregion

    #region Batch Operations

    /// <summary>
    /// 배치 테이블 무효화 - 여러 테이블을 한번에 무효화 (성능 최적화)
    /// Batch table invalidation - invalidates multiple tables at once for performance
    /// </summary>
    Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// 배치 패턴 무효화 - 여러 패턴을 한번에 무효화 (성능 최적화)
    /// Batch pattern invalidation - invalidates multiple patterns at once for performance
    /// </summary>
    Task InvalidateByPatternBatchAsync(IEnumerable<string> patterns, CancellationToken cancellationToken = default);

    /// <summary>
    /// 배치 키 무효화 - 여러 키를 한번에 무효화 (성능 최적화)
    /// Batch key invalidation - invalidates multiple keys at once for performance
    /// </summary>
    Task InvalidateByKeyBatchAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    #endregion

    #region Hierarchical Invalidation

    /// <summary>
    /// 계층적 무효화 - 연관된 테이블들과 함께 계층적으로 무효화
    /// Hierarchical invalidation - cascading invalidation through related tables
    /// </summary>
    Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default);

    /// <summary>
    /// 관련 테이블들과 함께 연쇄 무효화 (기존 ICacheInvalidator와 호환)
    /// Chain invalidation with related tables for backward compatibility
    /// </summary>
    Task InvalidateWithRelatedAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default);

    #endregion

    #region Cache Key Tracking

    /// <summary>
    /// 캐시 키를 테이블과 연결하여 추적 등록
    /// Track cache key by associating it with a table
    /// </summary>
    Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 키를 여러 테이블과 연결하여 추적 등록
    /// Track cache key by associating it with multiple tables
    /// </summary>
    Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 테이블과 연결된 모든 캐시 키 조회
    /// Get all cache keys associated with a table
    /// </summary>
    Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default);

    #endregion

    #region Rule-based Invalidation

    /// <summary>
    /// 무효화 규칙 등록 - 특정 조건에서 자동 무효화
    /// Register invalidation rule for automatic invalidation under specific conditions
    /// </summary>
    Task RegisterInvalidationRuleAsync(IInvalidationRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// 무효화 규칙 제거
    /// Unregister invalidation rule
    /// </summary>
    Task UnregisterInvalidationRuleAsync(string ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 등록된 모든 무효화 규칙 조회
    /// Get all registered invalidation rules
    /// </summary>
    Task<IEnumerable<IInvalidationRule>> GetInvalidationRulesAsync(CancellationToken cancellationToken = default);

    #endregion

    #region CQRS Support (Event-Driven Invalidation)

    /// <summary>
    /// 명령 실행 후 무효화 처리 - CQRS 패턴 지원
    /// Handle invalidation after command execution - supports CQRS pattern
    /// </summary>
    Task InvalidateOnCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class;

    /// <summary>
    /// 도메인 이벤트 기반 무효화 처리 - Event Sourcing 지원
    /// Handle invalidation based on domain events - supports Event Sourcing
    /// </summary>
    Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : class;

    /// <summary>
    /// 읽기 모델 무효화 - CQRS Read Model 지원
    /// Invalidate read model - supports CQRS Read Models
    /// </summary>
    Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default)
        where TReadModel : class;

    /// <summary>
    /// 프로젝션 무효화 - Event Sourcing Projection 지원
    /// Invalidate projection - supports Event Sourcing Projections
    /// </summary>
    Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default)
        where TProjection : class;

    #endregion

    #region Context and Management

    /// <summary>
    /// 무효화 실행 컨텍스트 생성
    /// Create invalidation execution context
    /// </summary>
    IInvalidationContext CreateContext(InvalidationTrigger trigger, object? metadata = null);

    /// <summary>
    /// 전체 캐시 클리어 (주의해서 사용)
    /// Clear all cache entries (use with caution)
    /// </summary>
    Task ClearAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 엔진 상태 및 통계 확인
    /// Get engine status and statistics
    /// </summary>
    Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// 무효화 규칙 인터페이스
/// Invalidation rule interface for automatic invalidation logic
/// </summary>
public interface IInvalidationRule
{
    /// <summary>규칙 고유 식별자</summary>
    string Id { get; }

    /// <summary>규칙 이름</summary>
    string Name { get; }

    /// <summary>규칙 설명</summary>
    string Description { get; }

    /// <summary>우선순위 (높을수록 먼저 실행)</summary>
    int Priority { get; }

    /// <summary>규칙 활성화 여부</summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// 규칙이 적용 가능한지 확인
    /// Check if the rule is applicable to the given context
    /// </summary>
    bool CanApply(IInvalidationContext context);

    /// <summary>
    /// 규칙 실행
    /// Execute the invalidation rule
    /// </summary>
    Task ExecuteAsync(IInvalidationContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 무효화 트리거 - 무효화를 발생시킨 원인
/// Invalidation trigger - the cause that initiated the invalidation
/// </summary>
public class InvalidationTrigger
{
    public string Source { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? UserId { get; init; }
    public string? CorrelationId { get; init; }
    
    public static InvalidationTrigger System(string eventType) => new()
    {
        Source = "System",
        EventType = eventType
    };
    
    public static InvalidationTrigger User(string userId, string eventType) => new()
    {
        Source = "User",
        EventType = eventType,
        UserId = userId
    };
    
    public static InvalidationTrigger Command<T>(T command) where T : class => new()
    {
        Source = "Command",
        EventType = typeof(T).Name
    };
    
    public static InvalidationTrigger Event<T>(T domainEvent) where T : class => new()
    {
        Source = "Event",
        EventType = typeof(T).Name
    };
}

/// <summary>
/// 무효화 엔진 상태
/// Invalidation engine status and statistics
/// </summary>
public class InvalidationEngineStatus
{
    public bool IsHealthy { get; init; }
    public int ActiveRules { get; init; }
    public int TrackedKeys { get; init; }
    public TimeSpan Uptime { get; init; }
    public Dictionary<string, object> Metrics { get; init; } = new();
    public DateTimeOffset LastActivity { get; init; } = DateTimeOffset.UtcNow;
}