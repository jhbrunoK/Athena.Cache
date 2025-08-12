namespace Invalidus.Core.Abstractions;

/// <summary>
/// 무효화 전략 인터페이스 - 다양한 무효화 로직을 플러그인 방식으로 구현
/// Invalidation strategy interface for implementing various invalidation logic as plugins
/// </summary>
public interface IInvalidationStrategy
{
    #region Strategy Information

    /// <summary>
    /// 전략 이름 (예: "Basic", "Smart", "Conditional", "Delayed", "Hierarchical")
    /// Strategy name
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// 전략 우선순위 (높을수록 먼저 실행)
    /// Strategy priority (higher values execute first)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 전략 설명
    /// Strategy description
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 전략 버전
    /// Strategy version
    /// </summary>
    string Version { get; }

    #endregion

    #region Strategy Logic

    /// <summary>
    /// 이 전략이 특정 무효화 컨텍스트를 처리할 수 있는지 확인
    /// Check if this strategy can handle a specific invalidation context
    /// </summary>
    bool CanHandle(IInvalidationContext context);

    /// <summary>
    /// 무효화 전략 실행
    /// Execute the invalidation strategy
    /// </summary>
    Task<InvalidationResult> ExecuteAsync(IInvalidationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 전략 초기화 (DI 컨테이너에서 등록될 때 호출)
    /// Initialize the strategy (called when registered in DI container)
    /// </summary>
    Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);

    /// <summary>
    /// 전략 정리 작업 (애플리케이션 종료 시 호출)
    /// Cleanup strategy resources (called on application shutdown)
    /// </summary>
    Task DisposeAsync();

    #endregion

    #region Configuration and Validation

    /// <summary>
    /// 전략 설정 검증
    /// Validate strategy configuration
    /// </summary>
    Task<ValidationResult> ValidateConfigurationAsync(object? configuration = null);

    /// <summary>
    /// 전략 설정 업데이트 (런타임 중 설정 변경 지원)
    /// Update strategy configuration (supports runtime configuration changes)
    /// </summary>
    Task UpdateConfigurationAsync(object configuration);

    #endregion
}

/// <summary>
/// 무효화 실행 결과
/// Invalidation execution result
/// </summary>
public class InvalidationResult
{
    /// <summary>실행 성공 여부</summary>
    public bool Success { get; init; }

    /// <summary>무효화된 키/항목 개수</summary>
    public long InvalidatedCount { get; init; }

    /// <summary>실행 시간</summary>
    public TimeSpan ExecutionTime { get; init; }

    /// <summary>오류 메시지 (실패 시)</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>상세 오류 정보 (실패 시)</summary>
    public Exception? Exception { get; init; }

    /// <summary>추가 메타데이터</summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>영향받은 캐시 프로바이더 목록</summary>
    public string[] AffectedProviders { get; init; } = Array.Empty<string>();

    /// <summary>성공 결과 생성</summary>
    public static InvalidationResult CreateSuccess(long count, TimeSpan executionTime, params string[] affectedProviders) =>
        new()
        {
            Success = true,
            InvalidatedCount = count,
            ExecutionTime = executionTime,
            AffectedProviders = affectedProviders
        };

    /// <summary>실패 결과 생성</summary>
    public static InvalidationResult CreateFailure(string errorMessage, Exception? exception = null) =>
        new()
        {
            Success = false,
            ErrorMessage = errorMessage,
            Exception = exception,
            InvalidatedCount = 0,
            ExecutionTime = TimeSpan.Zero
        };
}

/// <summary>
/// 설정 검증 결과
/// Configuration validation result
/// </summary>
public class ValidationResult
{
    /// <summary>검증 성공 여부</summary>
    public bool IsValid { get; init; }

    /// <summary>검증 오류 메시지 목록</summary>
    public string[] Errors { get; init; } = Array.Empty<string>();

    /// <summary>검증 경고 메시지 목록</summary>
    public string[] Warnings { get; init; } = Array.Empty<string>();

    /// <summary>성공 검증 결과 생성</summary>
    public static ValidationResult Valid() => new() { IsValid = true };

    /// <summary>실패 검증 결과 생성</summary>
    public static ValidationResult Invalid(params string[] errors) =>
        new() { IsValid = false, Errors = errors };

    /// <summary>경고가 있는 검증 결과 생성</summary>
    public static ValidationResult ValidWithWarnings(params string[] warnings) =>
        new() { IsValid = true, Warnings = warnings };
}

/// <summary>
/// 기본 무효화 전략 추상 클래스 - 공통 기능 구현
/// Base invalidation strategy abstract class providing common functionality
/// </summary>
public abstract class InvalidationStrategyBase : IInvalidationStrategy
{
    #region Properties

    public abstract string StrategyName { get; }
    public abstract int Priority { get; }
    public virtual string Description => $"{StrategyName} invalidation strategy";
    public virtual string Version => "1.0.0";

    protected IServiceProvider? ServiceProvider { get; private set; }
    protected ILogger? Logger { get; private set; }

    #endregion

    #region Abstract Methods

    public abstract bool CanHandle(IInvalidationContext context);
    public abstract Task<InvalidationResult> ExecuteAsync(IInvalidationContext context, CancellationToken cancellationToken = default);

    #endregion

    #region Virtual Methods

    public virtual Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        ServiceProvider = serviceProvider;
        Logger = serviceProvider.GetService<ILogger<InvalidationStrategyBase>>();
        return Task.CompletedTask;
    }

    public virtual Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    public virtual Task<ValidationResult> ValidateConfigurationAsync(object? configuration = null)
    {
        return Task.FromResult(ValidationResult.Valid());
    }

    public virtual Task UpdateConfigurationAsync(object configuration)
    {
        return Task.CompletedTask;
    }

    #endregion

    #region Protected Helper Methods

    protected void LogDebug(string message, params object[] args)
    {
        Logger?.LogDebug($"[{StrategyName}] {message}", args);
    }

    protected void LogInformation(string message, params object[] args)
    {
        Logger?.LogInformation($"[{StrategyName}] {message}", args);
    }

    protected void LogWarning(string message, params object[] args)
    {
        Logger?.LogWarning($"[{StrategyName}] {message}", args);
    }

    protected void LogError(Exception exception, string message, params object[] args)
    {
        Logger?.LogError(exception, $"[{StrategyName}] {message}", args);
    }

    protected T? GetService<T>() where T : class
    {
        return ServiceProvider?.GetService<T>();
    }

    protected T GetRequiredService<T>() where T : notnull
    {
        if (ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider is not initialized. Call InitializeAsync first.");
        
        return ServiceProvider.GetRequiredService<T>();
    }

    #endregion
}