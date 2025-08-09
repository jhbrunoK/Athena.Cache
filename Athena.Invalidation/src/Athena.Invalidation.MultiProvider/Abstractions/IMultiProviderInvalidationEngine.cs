namespace Athena.Invalidation.MultiProvider.Abstractions;

/// <summary>
/// 다중 캐시 제공자 무효화 엔진 인터페이스
/// </summary>
public interface IMultiProviderInvalidationEngine : IInvalidationEngine
{
    /// <summary>등록된 제공자 개수</summary>
    int ProvidersCount { get; }
    
    /// <summary>활성 제공자 목록</summary>
    IEnumerable<string> ActiveProviders { get; }
    
    /// <summary>제공자별 상태 조회</summary>
    Task<Dictionary<string, InvalidationEngineStatus>> GetProvidersStatusAsync(CancellationToken cancellationToken = default);
    
    /// <summary>특정 제공자에서만 무효화 실행</summary>
    Task InvalidateByTableOnProviderAsync(string tableName, string providerName, CancellationToken cancellationToken = default);
    
    /// <summary>특정 제공자들에서만 무효화 실행</summary>
    Task InvalidateByTableOnProvidersAsync(string tableName, IEnumerable<string> providerNames, CancellationToken cancellationToken = default);
    
    /// <summary>제공자별 병렬 실행 설정</summary>
    Task SetParallelExecutionAsync(bool enabled, int? maxConcurrency = null, CancellationToken cancellationToken = default);
    
    /// <summary>제공자별 우선순위 설정</summary>
    Task SetProviderPriorityAsync(string providerName, int priority, CancellationToken cancellationToken = default);
    
    /// <summary>제공자 활성화/비활성화</summary>
    Task SetProviderEnabledAsync(string providerName, bool enabled, CancellationToken cancellationToken = default);
    
    /// <summary>실패 처리 정책 설정</summary>
    Task SetFailureHandlingPolicyAsync(FailureHandlingPolicy policy, CancellationToken cancellationToken = default);
}

/// <summary>
/// 실패 처리 정책
/// </summary>
public enum FailureHandlingPolicy
{
    /// <summary>모든 제공자가 성공해야 함</summary>
    AllMustSucceed,
    
    /// <summary>하나라도 성공하면 됨</summary>
    AtLeastOneSucceeds,
    
    /// <summary>과반수가 성공하면 됨</summary>
    MajoritySucceeds,
    
    /// <summary>오류 무시하고 계속 진행</summary>
    IgnoreErrors
}

/// <summary>
/// 제공자 실행 모드
/// </summary>
public enum ProviderExecutionMode
{
    /// <summary>순차 실행</summary>
    Sequential,
    
    /// <summary>병렬 실행</summary>
    Parallel,
    
    /// <summary>우선순위 기반 실행</summary>
    Priority
}