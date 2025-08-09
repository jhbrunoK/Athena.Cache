namespace Athena.Invalidation.Core.Abstractions;

/// <summary>
/// 캐시 무효화 엔진의 핵심 인터페이스
/// 다양한 무효화 전략과 캐시 프로바이더를 조합하여 사용 가능
/// </summary>
public interface IInvalidationEngine
{
    /// <summary>
    /// 테이블 기반 무효화 - 특정 테이블과 연관된 모든 캐시 제거
    /// </summary>
    Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 패턴 기반 무효화 - 패턴에 맞는 캐시 키들 제거
    /// </summary>
    Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// 정확한 키 무효화 - 특정 캐시 키만 제거
    /// </summary>
    Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 배치 무효화 - 여러 테이블을 한번에 무효화
    /// </summary>
    Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// 계층적 무효화 - 연관된 테이블들과 함께 계층적으로 무효화
    /// </summary>
    Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 키를 테이블과 연결하여 추적 등록
    /// </summary>
    Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 키를 여러 테이블과 연결하여 추적 등록
    /// </summary>
    Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 테이블과 연결된 모든 캐시 키 조회
    /// </summary>
    Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 무효화 규칙 등록 - 특정 조건에서 자동 무효화
    /// </summary>
    Task RegisterInvalidationRuleAsync(IInvalidationRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// 무효화 규칙 제거
    /// </summary>
    Task UnregisterInvalidationRuleAsync(string ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 등록된 모든 무효화 규칙 조회
    /// </summary>
    Task<IEnumerable<IInvalidationRule>> GetInvalidationRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 무효화 실행 컨텍스트 생성
    /// </summary>
    IInvalidationContext CreateContext(InvalidationTrigger trigger, object? metadata = null);

    /// <summary>
    /// 전체 캐시 클리어 (주의해서 사용)
    /// </summary>
    Task ClearAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 엔진 상태 확인
    /// </summary>
    Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    
    // CQRS 지원 메서드들 (Phase 2에서 추가)
    
    /// <summary>
    /// 명령 실행 후 무효화 처리
    /// </summary>
    Task InvalidateOnCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class;
    
    /// <summary>
    /// 도메인 이벤트 기반 무효화 처리
    /// </summary>
    Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : class;
    
    /// <summary>
    /// 읽기 모델 무효화
    /// </summary>
    Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default)
        where TReadModel : class;
    
    /// <summary>
    /// 프로젝션 무효화
    /// </summary>
    Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default)
        where TProjection : class;
}

/// <summary>
/// 무효화 엔진 상태 정보
/// </summary>
public class InvalidationEngineStatus
{
    public bool IsHealthy { get; set; }
    public int ActiveRules { get; set; }
    public int TrackedKeys { get; set; }
    public int TrackedKeysCount { get; set; }
    public int RegisteredRulesCount { get; set; }
    public DateTimeOffset LastActivity { get; set; }
    public TimeSpan Uptime { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
}