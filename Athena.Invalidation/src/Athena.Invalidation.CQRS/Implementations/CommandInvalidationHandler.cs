using Athena.Invalidation.CQRS.Abstractions;

namespace Athena.Invalidation.CQRS.Implementations;

/// <summary>
/// 명령 기반 캐시 무효화를 처리하는 핸들러
/// </summary>
public class CommandInvalidationHandler : ICommandInvalidationHandler
{
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly ILogger<CommandInvalidationHandler> _logger;
    private readonly Dictionary<Type, ICommandInvalidationHandler> _specificHandlers = new();

    public CommandInvalidationHandler(
        IInvalidationEngine invalidationEngine,
        ILogger<CommandInvalidationHandler> logger)
    {
        _invalidationEngine = invalidationEngine ?? throw new ArgumentNullException(nameof(invalidationEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvalidateBeforeAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) 
        where TCommand : ICommand
    {
        if (!CanHandle(command)) return;

        try
        {
            _logger.LogDebug("Pre-invalidation for command {CommandType}:{CommandId}", 
                typeof(TCommand).Name, command.CommandId);

            // 특정 핸들러가 있는 경우 사용
            if (_specificHandlers.TryGetValue(typeof(TCommand), out var specificHandler) 
                && specificHandler is ICommandInvalidationHandler<TCommand> typedHandler)
            {
                await typedHandler.InvalidateBeforeAsync(command, cancellationToken);
                return;
            }

            // 기본 사전 무효화 (일반적으로는 하지 않음)
            _logger.LogDebug("No pre-invalidation defined for command {CommandType}", typeof(TCommand).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate before command {CommandType}:{CommandId}", 
                typeof(TCommand).Name, command.CommandId);
            throw;
        }
    }

    public async Task InvalidateAfterAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) 
        where TCommand : ICommand
    {
        if (!CanHandle(command)) return;

        try
        {
            _logger.LogDebug("Post-invalidation for command {CommandType}:{CommandId}", 
                typeof(TCommand).Name, command.CommandId);

            // 특정 핸들러가 있는 경우 사용
            if (_specificHandlers.TryGetValue(typeof(TCommand), out var specificHandler) 
                && specificHandler is ICommandInvalidationHandler<TCommand> typedHandler)
            {
                await typedHandler.InvalidateAfterAsync(command, cancellationToken);
                return;
            }

            // 기본 무효화 로직
            await PerformDefaultInvalidationAsync(command, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate after command {CommandType}:{CommandId}", 
                typeof(TCommand).Name, command.CommandId);
            throw;
        }
    }

    public async Task InvalidateAfterAsync<TCommand, TResult>(TCommand command, TResult result, CancellationToken cancellationToken = default) 
        where TCommand : ICommand<TResult>
    {
        if (!CanHandle(command)) return;

        try
        {
            _logger.LogDebug("Post-invalidation with result for command {CommandType}:{CommandId}", 
                typeof(TCommand).Name, command.CommandId);

            // 결과를 고려한 무효화 (결과에 따라 다른 무효화 전략 적용 가능)
            await InvalidateAfterAsync(command as ICommand, cancellationToken);
            
            // 결과 기반 추가 무효화 로직
            await PerformResultBasedInvalidationAsync(command, result, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate after command with result {CommandType}:{CommandId}", 
                typeof(TCommand).Name, command.CommandId);
            throw;
        }
    }

    public bool CanHandle<TCommand>(TCommand command) where TCommand : ICommand
    {
        if (command == null) return false;
        
        // 기본적으로 모든 명령을 처리할 수 있지만, 특정 핸들러가 있거나 명령에서 무효화 정보를 제공하는 경우만
        return _specificHandlers.ContainsKey(typeof(TCommand)) || 
               HasInvalidationMetadata(command);
    }

    public async Task<IEnumerable<string>> GetInvalidationTablesAsync<TCommand>(TCommand command) 
        where TCommand : ICommand
    {
        var tables = new List<string>();

        try
        {
            // 명령 메타데이터에서 테이블 정보 추출
            if (command.Metadata.TryGetValue("InvalidationTables", out var tablesObj) && 
                tablesObj is IEnumerable<string> tableNames)
            {
                tables.AddRange(tableNames);
            }

            // 명령 타입 이름에서 추론 (예: CreateUserCommand -> Users)
            var commandTypeName = typeof(TCommand).Name;
            if (commandTypeName.EndsWith("Command"))
            {
                var entityName = ExtractEntityNameFromCommand(commandTypeName);
                if (!string.IsNullOrEmpty(entityName))
                {
                    tables.Add(entityName);
                }
            }

            // 특정 핸들러가 있는 경우 해당 핸들러에서 추가 테이블 조회
            if (_specificHandlers.TryGetValue(typeof(TCommand), out var specificHandler))
            {
                var additionalTables = await specificHandler.GetInvalidationTablesAsync(command);
                tables.AddRange(additionalTables);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get invalidation tables for command {CommandType}", typeof(TCommand).Name);
        }

        return tables.Distinct();
    }

    public async Task<IEnumerable<string>> GetInvalidationPatternsAsync<TCommand>(TCommand command) 
        where TCommand : ICommand
    {
        var patterns = new List<string>();

        try
        {
            // 명령 메타데이터에서 패턴 정보 추출
            if (command.Metadata.TryGetValue("InvalidationPatterns", out var patternsObj) && 
                patternsObj is IEnumerable<string> patternNames)
            {
                patterns.AddRange(patternNames);
            }

            // 특정 핸들러가 있는 경우 해당 핸들러에서 패턴 조회
            if (_specificHandlers.TryGetValue(typeof(TCommand), out var specificHandler))
            {
                var additionalPatterns = await specificHandler.GetInvalidationPatternsAsync(command);
                patterns.AddRange(additionalPatterns);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get invalidation patterns for command {CommandType}", typeof(TCommand).Name);
        }

        return patterns.Distinct();
    }

    /// <summary>
    /// 특정 명령 타입을 위한 핸들러 등록
    /// </summary>
    public void RegisterHandler<TCommand>(ICommandInvalidationHandler<TCommand> handler) where TCommand : ICommand
    {
        _specificHandlers[typeof(TCommand)] = handler;
        _logger.LogInformation("Registered specific invalidation handler for command type {CommandType}", typeof(TCommand).Name);
    }

    /// <summary>
    /// 특정 명령 타입 핸들러 제거
    /// </summary>
    public void UnregisterHandler<TCommand>() where TCommand : ICommand
    {
        if (_specificHandlers.Remove(typeof(TCommand)))
        {
            _logger.LogInformation("Unregistered invalidation handler for command type {CommandType}", typeof(TCommand).Name);
        }
    }

    private async Task PerformDefaultInvalidationAsync<TCommand>(TCommand command, CancellationToken cancellationToken) 
        where TCommand : ICommand
    {
        // 테이블 기반 무효화
        var tables = await GetInvalidationTablesAsync(command);
        foreach (var table in tables)
        {
            await _invalidationEngine.InvalidateByTableAsync(table, cancellationToken);
        }

        // 패턴 기반 무효화
        var patterns = await GetInvalidationPatternsAsync(command);
        foreach (var pattern in patterns)
        {
            await _invalidationEngine.InvalidateByPatternAsync(pattern, cancellationToken);
        }

        _logger.LogDebug("Performed default invalidation for command {CommandType} - Tables: [{Tables}], Patterns: [{Patterns}]",
            typeof(TCommand).Name, string.Join(", ", tables), string.Join(", ", patterns));
    }

    private async Task PerformResultBasedInvalidationAsync<TCommand, TResult>(
        TCommand command, TResult result, CancellationToken cancellationToken) 
        where TCommand : ICommand<TResult>
    {
        // 결과가 실패인 경우 무효화하지 않을 수 있음
        if (result == null) return;

        try
        {
            // 결과에서 추가 무효화 정보 추출
            if (result is IHasInvalidationInfo invalidationInfo)
            {
                foreach (var table in invalidationInfo.GetInvalidationTables())
                {
                    await _invalidationEngine.InvalidateByTableAsync(table, cancellationToken);
                }

                foreach (var pattern in invalidationInfo.GetInvalidationPatterns())
                {
                    await _invalidationEngine.InvalidateByPatternAsync(pattern, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to perform result-based invalidation for command {CommandType}", typeof(TCommand).Name);
        }
    }

    private bool HasInvalidationMetadata<TCommand>(TCommand command) where TCommand : ICommand
    {
        return command.Metadata.ContainsKey("InvalidationTables") || 
               command.Metadata.ContainsKey("InvalidationPatterns") ||
               typeof(TCommand).Name.EndsWith("Command");
    }

    private string ExtractEntityNameFromCommand(string commandTypeName)
    {
        // CreateUserCommand -> Users
        // UpdateOrderCommand -> Orders
        // DeleteProductCommand -> Products

        var commandName = commandTypeName.Replace("Command", "");
        
        if (commandName.StartsWith("Create") || 
            commandName.StartsWith("Update") || 
            commandName.StartsWith("Delete"))
        {
            var entityName = commandName.Substring(6); // Remove "Create", "Update", "Delete"
            return entityName.EndsWith("s") ? entityName : entityName + "s"; // Pluralize
        }

        return commandName + "s";
    }
}

/// <summary>
/// 무효화 정보를 제공하는 인터페이스
/// </summary>
public interface IHasInvalidationInfo
{
    IEnumerable<string> GetInvalidationTables();
    IEnumerable<string> GetInvalidationPatterns();
}