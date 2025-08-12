using Invalidus.Core.Abstractions;

namespace Invalidus.CQRS.Abstractions;

/// <summary>
/// 명령 처리기 인터페이스
/// Command handler interface
/// </summary>
public interface ICommandHandler<in TCommand> where TCommand : IInvalidationCommand
{
    /// <summary>명령을 비동기적으로 처리</summary>
    Task<CommandResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// 명령 처리 결과
/// Command handling result
/// </summary>
public record CommandResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public List<string>? InvalidatedKeys { get; init; }
    public TimeSpan ExecutionTime { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    public static CommandResult Successful(List<string>? invalidatedKeys = null, TimeSpan executionTime = default) =>
        new() { Success = true, InvalidatedKeys = invalidatedKeys ?? new(), ExecutionTime = executionTime };
        
    public static CommandResult Failed(string errorMessage, TimeSpan executionTime = default) =>
        new() { Success = false, ErrorMessage = errorMessage, ExecutionTime = executionTime };
}

/// <summary>
/// 명령 디스패처 인터페이스
/// Command dispatcher interface
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>단일 명령 전송</summary>
    Task<CommandResult> DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) 
        where TCommand : IInvalidationCommand;
    
    /// <summary>복수 명령 배치 전송</summary>
    Task<BatchCommandResult> DispatchBatchAsync(IEnumerable<IInvalidationCommand> commands, CancellationToken cancellationToken = default);
    
    /// <summary>지연 명령 예약</summary>
    Task<string> ScheduleAsync<TCommand>(TCommand command, TimeSpan delay, CancellationToken cancellationToken = default) 
        where TCommand : IInvalidationCommand;
    
    /// <summary>예약된 명령 취소</summary>
    Task<bool> CancelScheduledAsync(string scheduleId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 배치 명령 처리 결과
/// Batch command processing result
/// </summary>
public record BatchCommandResult
{
    public required int TotalCommands { get; init; }
    public required int SuccessfulCommands { get; init; }
    public required int FailedCommands { get; init; }
    public TimeSpan TotalExecutionTime { get; init; }
    public List<CommandResult> Results { get; init; } = new();
    public Dictionary<string, string> Errors { get; init; } = new();
    
    public bool AllSuccessful => FailedCommands == 0;
    public double SuccessRate => TotalCommands > 0 ? (double)SuccessfulCommands / TotalCommands : 0.0;
}