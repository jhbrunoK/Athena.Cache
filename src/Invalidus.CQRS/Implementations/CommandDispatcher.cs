using Invalidus.Core.Abstractions;
using Invalidus.CQRS.Abstractions;
using System.Diagnostics;

namespace Invalidus.CQRS.Implementations;

/// <summary>
/// 명령 디스패처 구현체
/// Command dispatcher implementation
/// </summary>
public class CommandDispatcher : ICommandDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ILogger<CommandDispatcher> _logger;
    private readonly ConcurrentDictionary<string, ScheduledCommand> _scheduledCommands = new();
    private readonly Timer _scheduledCommandTimer;

    public CommandDispatcher(
        IServiceProvider serviceProvider,
        IInvalidationEngine invalidationEngine,
        ILogger<CommandDispatcher> logger,
        IEventPublisher? eventPublisher = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _invalidationEngine = invalidationEngine ?? throw new ArgumentNullException(nameof(invalidationEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventPublisher = eventPublisher;
        
        // Setup timer for scheduled commands
        _scheduledCommandTimer = new Timer(ProcessScheduledCommands, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        
        _logger.LogInformation("CommandDispatcher initialized");
    }

    public async Task<CommandResult> DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) 
        where TCommand : IInvalidationCommand
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("Dispatching command {CommandType} with ID {CommandId}", 
                command.CommandType, command.CommandId);

            // Try to get specific handler first
            var handler = _serviceProvider.GetService<ICommandHandler<TCommand>>();
            if (handler != null)
            {
                var result = await handler.HandleAsync(command, cancellationToken);
                stopwatch.Stop();
                
                _logger.LogDebug("Command {CommandId} handled in {ElapsedMs}ms with result: {Success}", 
                    command.CommandId, stopwatch.ElapsedMilliseconds, result.Success);
                
                // Publish completion event if successful
                if (result.Success && _eventPublisher != null)
                {
                    await PublishCompletionEventAsync(command, result, stopwatch.Elapsed);
                }
                
                return result with { ExecutionTime = stopwatch.Elapsed };
            }

            // Fallback to direct engine handling for standard commands
            var engineResult = await HandleWithEngineAsync(command, cancellationToken);
            stopwatch.Stop();
            
            return engineResult with { ExecutionTime = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error dispatching command {CommandType} with ID {CommandId}", 
                command.CommandType, command.CommandId);
            
            return CommandResult.Failed($"Command dispatch failed: {ex.Message}", stopwatch.Elapsed);
        }
    }

    public async Task<BatchCommandResult> DispatchBatchAsync(IEnumerable<IInvalidationCommand> commands, CancellationToken cancellationToken = default)
    {
        if (commands == null) throw new ArgumentNullException(nameof(commands));
        
        var commandList = commands.ToList();
        var stopwatch = Stopwatch.StartNew();
        var results = new List<CommandResult>();
        var errors = new Dictionary<string, string>();
        
        _logger.LogInformation("Dispatching batch of {CommandCount} commands", commandList.Count);
        
        // Process commands in parallel for better performance
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
        var tasks = commandList.Select(async command =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await DispatchAsync(command, cancellationToken);
            }
            catch (Exception ex)
            {
                var error = $"Command {command.CommandId} failed: {ex.Message}";
                errors[command.CommandId] = error;
                return CommandResult.Failed(error);
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        results.AddRange(await Task.WhenAll(tasks));
        stopwatch.Stop();
        
        var successfulCommands = results.Count(r => r.Success);
        var failedCommands = results.Count - successfulCommands;
        
        var batchResult = new BatchCommandResult
        {
            TotalCommands = commandList.Count,
            SuccessfulCommands = successfulCommands,
            FailedCommands = failedCommands,
            TotalExecutionTime = stopwatch.Elapsed,
            Results = results,
            Errors = errors
        };
        
        _logger.LogInformation("Batch completed: {SuccessfulCount}/{TotalCount} successful in {ElapsedMs}ms", 
            successfulCommands, commandList.Count, stopwatch.ElapsedMilliseconds);
        
        // Publish batch completion event
        if (_eventPublisher != null)
        {
            await PublishBatchEventAsync(commandList, batchResult);
        }
        
        return batchResult;
    }

    public Task<string> ScheduleAsync<TCommand>(TCommand command, TimeSpan delay, CancellationToken cancellationToken = default) 
        where TCommand : IInvalidationCommand
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        
        var scheduleId = Guid.NewGuid().ToString();
        var executeAt = DateTime.UtcNow.Add(delay);
        
        var scheduledCommand = new ScheduledCommand
        {
            Id = scheduleId,
            Command = command,
            ExecuteAt = executeAt,
            CommandType = typeof(TCommand)
        };
        
        _scheduledCommands[scheduleId] = scheduledCommand;
        
        _logger.LogInformation("Command {CommandId} scheduled for execution at {ExecuteAt} (delay: {Delay})", 
            command.CommandId, executeAt, delay);
        
        return Task.FromResult(scheduleId);
    }

    public async Task<bool> CancelScheduledAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        await Task.Yield(); // Make method truly async
        
        var cancelled = _scheduledCommands.TryRemove(scheduleId, out var scheduledCommand);
        
        if (cancelled)
        {
            _logger.LogInformation("Scheduled command {ScheduleId} cancelled", scheduleId);
        }
        else
        {
            _logger.LogWarning("Failed to cancel scheduled command {ScheduleId} - not found", scheduleId);
        }
        
        return cancelled;
    }

    private async Task<CommandResult> HandleWithEngineAsync(IInvalidationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            // Handle common command types with the invalidation engine
            
            switch (command)
            {
                case InvalidateTableCommand tableCmd:
                    await _invalidationEngine.InvalidateByTableAsync(tableCmd.TableName, cancellationToken);
                    return CommandResult.Successful(new List<string> { $"table:{tableCmd.TableName}" });
                
                case InvalidatePatternCommand patternCmd:
                    await _invalidationEngine.InvalidateByPatternAsync(patternCmd.Pattern, cancellationToken);
                    return CommandResult.Successful(new List<string> { $"pattern:{patternCmd.Pattern}" });
                
                case InvalidateHierarchyCommand hierarchyCmd:
                    await _invalidationEngine.InvalidateHierarchyAsync(hierarchyCmd.RootKey, new string[0], hierarchyCmd.MaxDepth, cancellationToken);
                    return CommandResult.Successful(new List<string> { $"hierarchy:{hierarchyCmd.RootKey}" });
                
                case InvalidateReadModelCommand readModelCmd:
                    // Use generic method for read model invalidation
                    var readModelType = Type.GetType(readModelCmd.ReadModelType);
                    if (readModelType != null)
                    {
                        var method = typeof(IInvalidationEngine).GetMethod("InvalidateReadModelAsync")?.MakeGenericMethod(readModelType);
                        var task = (Task?)method?.Invoke(_invalidationEngine, new object?[] { readModelCmd.AggregateId, cancellationToken });
                        if (task != null)
                        {
                            await task;
                        }
                    }
                    return CommandResult.Successful(new List<string> { $"readmodel:{readModelCmd.ReadModelType}" });
                
                case InvalidateProjectionCommand projectionCmd:
                    // Use generic method for projection invalidation
                    var projectionType = Type.GetType(projectionCmd.ProjectionName);
                    if (projectionType != null)
                    {
                        var method = typeof(IInvalidationEngine).GetMethod("InvalidateProjectionAsync")?.MakeGenericMethod(projectionType);
                        var task = (Task?)method?.Invoke(_invalidationEngine, new object?[] { projectionCmd.PartitionKey, cancellationToken });
                        if (task != null)
                        {
                            await task;
                        }
                    }
                    return CommandResult.Successful(new List<string> { $"projection:{projectionCmd.ProjectionName}" });
                
                default:
                    return CommandResult.Failed($"Unknown command type: {command.CommandType}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command {CommandType} with engine", command.CommandType);
            return CommandResult.Failed($"Engine handling failed: {ex.Message}");
        }
    }

    private async Task PublishCompletionEventAsync(IInvalidationCommand command, CommandResult result, TimeSpan duration)
    {
        try
        {
            var completionEvent = new InvalidationCompletedEvent
            {
                InvalidationId = command.CommandId,
                InvalidationType = command.CommandType,
                InvalidatedKeys = result.InvalidatedKeys ?? new List<string>(),
                TotalKeysInvalidated = result.InvalidatedKeys?.Count ?? 0,
                Duration = duration,
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
                CorrelationId = command.Metadata.GetValueOrDefault("CorrelationId")?.ToString()
            };
            
            await _eventPublisher!.PublishAsync(completionEvent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish completion event for command {CommandId}", command.CommandId);
        }
    }

    private async Task PublishBatchEventAsync(IEnumerable<IInvalidationCommand> commands, BatchCommandResult result)
    {
        try
        {
            var batchEvent = new BatchInvalidationEvent
            {
                Commands = commands.ToList(),
                TotalCommands = result.TotalCommands,
                SuccessfulCommands = result.SuccessfulCommands,
                FailedCommands = result.FailedCommands,
                BatchDuration = result.TotalExecutionTime,
                Errors = result.Errors
            };
            
            await _eventPublisher!.PublishAsync(batchEvent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish batch completion event");
        }
    }

    private void ProcessScheduledCommands(object? state)
    {
        _ = Task.Run(async () =>
        {
            var now = DateTime.UtcNow;
            var commandsToExecute = _scheduledCommands.Values
                .Where(sc => sc.ExecuteAt <= now)
                .ToList();
            
            foreach (var scheduledCommand in commandsToExecute)
            {
                try
                {
                    _scheduledCommands.TryRemove(scheduledCommand.Id, out _);
                    
                    // Use reflection to call the generic DispatchAsync method
                    var method = GetType().GetMethod(nameof(DispatchAsync))!.MakeGenericMethod(scheduledCommand.CommandType);
                    var task = (Task<CommandResult>)method.Invoke(this, new object[] { scheduledCommand.Command, CancellationToken.None })!;
                    var result = await task;
                    
                    _logger.LogInformation("Executed scheduled command {CommandId} with result: {Success}", 
                        scheduledCommand.Command.CommandId, result.Success);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing scheduled command {CommandId}", scheduledCommand.Command.CommandId);
                }
            }
        });
    }

    private record ScheduledCommand
    {
        public required string Id { get; init; }
        public required IInvalidationCommand Command { get; init; }
        public required DateTime ExecuteAt { get; init; }
        public required Type CommandType { get; init; }
    }
}