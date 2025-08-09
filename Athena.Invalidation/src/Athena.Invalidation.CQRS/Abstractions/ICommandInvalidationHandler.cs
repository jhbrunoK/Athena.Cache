namespace Athena.Invalidation.CQRS.Abstractions;

/// <summary>
/// 명령 실행 후 캐시 무효화를 처리하는 핸들러 인터페이스
/// </summary>
public interface ICommandInvalidationHandler
{
    /// <summary>
    /// 명령 실행 전 무효화 (선택사항)
    /// </summary>
    Task InvalidateBeforeAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    /// <summary>
    /// 명령 실행 후 무효화 (필수)
    /// </summary>
    Task InvalidateAfterAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand;
        
    /// <summary>
    /// 명령 실행 후 무효화 (결과 포함)
    /// </summary>
    Task InvalidateAfterAsync<TCommand, TResult>(TCommand command, TResult result, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>;

    /// <summary>
    /// 특정 명령 타입을 처리할 수 있는지 확인
    /// </summary>
    bool CanHandle<TCommand>(TCommand command) where TCommand : ICommand;
    
    /// <summary>
    /// 명령에서 무효화할 테이블들 추출
    /// </summary>
    Task<IEnumerable<string>> GetInvalidationTablesAsync<TCommand>(TCommand command) where TCommand : ICommand;
    
    /// <summary>
    /// 명령에서 무효화할 패턴들 추출
    /// </summary>
    Task<IEnumerable<string>> GetInvalidationPatternsAsync<TCommand>(TCommand command) where TCommand : ICommand;
}

/// <summary>
/// 특정 명령 타입에 대한 무효화 핸들러
/// </summary>
public interface ICommandInvalidationHandler<in TCommand> : ICommandInvalidationHandler 
    where TCommand : ICommand
{
    /// <summary>
    /// 특정 명령 타입에 대한 무효화 전 처리
    /// </summary>
    Task InvalidateBeforeAsync(TCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 특정 명령 타입에 대한 무효화 후 처리
    /// </summary>
    Task InvalidateAfterAsync(TCommand command, CancellationToken cancellationToken = default);
}