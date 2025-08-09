using Microsoft.Extensions.Logging;

namespace Athena.Invalidation.Core.Abstractions;

/// <summary>
/// 무효화 전략을 정의하는 인터페이스
/// 플러그인 방식으로 다양한 무효화 로직을 구현할 수 있음
/// </summary>
public interface IInvalidationStrategy
{
    /// <summary>
    /// 전략 이름 (예: "Basic", "Smart", "Conditional", "Delayed")
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// 전략 우선순위 (높을수록 먼저 실행)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 이 전략이 특정 무효화 컨텍스트를 처리할 수 있는지 확인
    /// </summary>
    bool CanHandle(IInvalidationContext context);

    /// <summary>
    /// 무효화 전략 실행
    /// </summary>
    Task<InvalidationResult> ExecuteAsync(IInvalidationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 전략 초기화
    /// </summary>
    Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);

    /// <summary>
    /// 전략 정리
    /// </summary>
    Task DisposeAsync();
}

/// <summary>
/// 무효화 결과
/// </summary>
public class InvalidationResult
{
    public bool Success { get; set; }
    public int InvalidatedCount { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();

    public static InvalidationResult Successful(int invalidatedCount, TimeSpan executionTime, Dictionary<string, object>? metadata = null)
        => new()
        {
            Success = true,
            InvalidatedCount = invalidatedCount,
            ExecutionTime = executionTime,
            Metadata = metadata ?? new()
        };

    public static InvalidationResult Failed(string errorMessage, Exception? exception = null, Dictionary<string, object>? metadata = null)
        => new()
        {
            Success = false,
            ErrorMessage = errorMessage,
            Exception = exception,
            Metadata = metadata ?? new()
        };
}