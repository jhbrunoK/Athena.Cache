using Athena.Invalidation.Distributed.Abstractions;
using Athena.Invalidation.Distributed.Models;
using Athena.Invalidation.Core.Abstractions;

namespace Athena.Invalidation.Distributed.Handlers;

/// <summary>
/// 기본 분산 무효화 이벤트 핸들러 - 로컬 무효화 엔진에 위임
/// </summary>
public class DistributedInvalidationEventHandler : 
    IDistributedInvalidationEventHandler<TableInvalidationEvent>,
    IDistributedInvalidationEventHandler<PatternInvalidationEvent>,
    IDistributedInvalidationEventHandler<KeyInvalidationEvent>,
    IDistributedInvalidationEventHandler<BatchInvalidationEvent>,
    IDistributedInvalidationEventHandler<HierarchicalInvalidationEvent>,
    IDistributedInvalidationEventHandler<CommandInvalidationEvent>,
    IDistributedInvalidationEventHandler<DomainEventInvalidationEvent>
{
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly ILogger<DistributedInvalidationEventHandler> _logger;

    public int Priority => 100; // 기본 우선순위

    public DistributedInvalidationEventHandler(
        IInvalidationEngine invalidationEngine,
        ILogger<DistributedInvalidationEventHandler> logger)
    {
        _invalidationEngine = invalidationEngine ?? throw new ArgumentNullException(nameof(invalidationEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task HandleAsync(TableInvalidationEvent invalidationEvent, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(invalidationEvent)) return;

        try
        {
            _logger.LogDebug("Processing distributed table invalidation: {TableName} from node {SourceNodeId}",
                invalidationEvent.TableName, invalidationEvent.SourceNodeId);

            await _invalidationEngine.InvalidateByTableAsync(invalidationEvent.TableName, cancellationToken);

            _logger.LogInformation("Successfully processed distributed table invalidation: {TableName}",
                invalidationEvent.TableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process distributed table invalidation: {TableName} from node {SourceNodeId}",
                invalidationEvent.TableName, invalidationEvent.SourceNodeId);
            throw;
        }
    }

    public async Task HandleAsync(PatternInvalidationEvent invalidationEvent, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(invalidationEvent)) return;

        try
        {
            _logger.LogDebug("Processing distributed pattern invalidation: {Pattern} from node {SourceNodeId}",
                invalidationEvent.Pattern, invalidationEvent.SourceNodeId);

            await _invalidationEngine.InvalidateByPatternAsync(invalidationEvent.Pattern, cancellationToken);

            _logger.LogInformation("Successfully processed distributed pattern invalidation: {Pattern}",
                invalidationEvent.Pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process distributed pattern invalidation: {Pattern} from node {SourceNodeId}",
                invalidationEvent.Pattern, invalidationEvent.SourceNodeId);
            throw;
        }
    }

    public async Task HandleAsync(KeyInvalidationEvent invalidationEvent, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(invalidationEvent)) return;

        try
        {
            _logger.LogDebug("Processing distributed key invalidation: {Key} from node {SourceNodeId}",
                invalidationEvent.Key, invalidationEvent.SourceNodeId);

            await _invalidationEngine.InvalidateByKeyAsync(invalidationEvent.Key, cancellationToken);

            _logger.LogInformation("Successfully processed distributed key invalidation: {Key}",
                invalidationEvent.Key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process distributed key invalidation: {Key} from node {SourceNodeId}",
                invalidationEvent.Key, invalidationEvent.SourceNodeId);
            throw;
        }
    }

    public async Task HandleAsync(BatchInvalidationEvent invalidationEvent, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(invalidationEvent)) return;

        try
        {
            _logger.LogDebug("Processing distributed batch invalidation: {TableCount} tables from node {SourceNodeId}",
                invalidationEvent.TableNames.Length, invalidationEvent.SourceNodeId);

            await _invalidationEngine.InvalidateBatchAsync(invalidationEvent.TableNames, cancellationToken);

            _logger.LogInformation("Successfully processed distributed batch invalidation: {TableNames}",
                string.Join(", ", invalidationEvent.TableNames));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process distributed batch invalidation from node {SourceNodeId}: {TableNames}",
                invalidationEvent.SourceNodeId, string.Join(", ", invalidationEvent.TableNames));
            throw;
        }
    }

    public async Task HandleAsync(HierarchicalInvalidationEvent invalidationEvent, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(invalidationEvent)) return;

        try
        {
            _logger.LogDebug("Processing distributed hierarchical invalidation: {RootTable} from node {SourceNodeId}",
                invalidationEvent.RootTable, invalidationEvent.SourceNodeId);

            await _invalidationEngine.InvalidateHierarchyAsync(
                invalidationEvent.RootTable,
                invalidationEvent.RelatedTables,
                invalidationEvent.MaxDepth,
                cancellationToken);

            _logger.LogInformation("Successfully processed distributed hierarchical invalidation: {RootTable}",
                invalidationEvent.RootTable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process distributed hierarchical invalidation: {RootTable} from node {SourceNodeId}",
                invalidationEvent.RootTable, invalidationEvent.SourceNodeId);
            throw;
        }
    }

    public async Task HandleAsync(CommandInvalidationEvent invalidationEvent, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(invalidationEvent)) return;

        try
        {
            _logger.LogDebug("Processing distributed command invalidation: {CommandType} from node {SourceNodeId}",
                invalidationEvent.CommandType, invalidationEvent.SourceNodeId);

            // 명령 데이터를 역직렬화하여 로컬 엔진에 전달
            if (!string.IsNullOrEmpty(invalidationEvent.CommandData))
            {
                // 실제 구현에서는 CommandType을 기반으로 적절한 타입으로 역직렬화해야 함
                // 여기서는 간단히 타입 추론을 사용
                var tableName = InferTableFromCommandType(invalidationEvent.CommandType);
                if (!string.IsNullOrEmpty(tableName))
                {
                    await _invalidationEngine.InvalidateByTableAsync(tableName, cancellationToken);
                }
            }

            _logger.LogInformation("Successfully processed distributed command invalidation: {CommandType}",
                invalidationEvent.CommandType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process distributed command invalidation: {CommandType} from node {SourceNodeId}",
                invalidationEvent.CommandType, invalidationEvent.SourceNodeId);
            throw;
        }
    }

    public async Task HandleAsync(DomainEventInvalidationEvent invalidationEvent, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(invalidationEvent)) return;

        try
        {
            _logger.LogDebug("Processing distributed domain event invalidation: {DomainEventType} from node {SourceNodeId}",
                invalidationEvent.DomainEventType, invalidationEvent.SourceNodeId);

            // 도메인 이벤트 데이터를 역직렬화하여 로컬 엔진에 전달
            if (!string.IsNullOrEmpty(invalidationEvent.DomainEventData))
            {
                // 실제 구현에서는 DomainEventType을 기반으로 적절한 타입으로 역직렬화해야 함
                // 여기서는 간단히 타입 추론을 사용
                var tableName = InferTableFromEventType(invalidationEvent.DomainEventType);
                if (!string.IsNullOrEmpty(tableName))
                {
                    if (!string.IsNullOrEmpty(invalidationEvent.AggregateId))
                    {
                        // 집계 ID가 있는 경우 패턴 기반 무효화
                        var pattern = $"{tableName.ToLower()}:{invalidationEvent.AggregateId}:*";
                        await _invalidationEngine.InvalidateByPatternAsync(pattern, cancellationToken);
                    }
                    else
                    {
                        // 전체 테이블 무효화
                        await _invalidationEngine.InvalidateByTableAsync(tableName, cancellationToken);
                    }
                }
            }

            _logger.LogInformation("Successfully processed distributed domain event invalidation: {DomainEventType}",
                invalidationEvent.DomainEventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process distributed domain event invalidation: {DomainEventType} from node {SourceNodeId}",
                invalidationEvent.DomainEventType, invalidationEvent.SourceNodeId);
            throw;
        }
    }

    public bool CanHandle(TableInvalidationEvent invalidationEvent) => 
        !string.IsNullOrEmpty(invalidationEvent.TableName);

    public bool CanHandle(PatternInvalidationEvent invalidationEvent) => 
        !string.IsNullOrEmpty(invalidationEvent.Pattern);

    public bool CanHandle(KeyInvalidationEvent invalidationEvent) => 
        !string.IsNullOrEmpty(invalidationEvent.Key);

    public bool CanHandle(BatchInvalidationEvent invalidationEvent) => 
        invalidationEvent.TableNames?.Any() == true;

    public bool CanHandle(HierarchicalInvalidationEvent invalidationEvent) => 
        !string.IsNullOrEmpty(invalidationEvent.RootTable);

    public bool CanHandle(CommandInvalidationEvent invalidationEvent) => 
        !string.IsNullOrEmpty(invalidationEvent.CommandType);

    public bool CanHandle(DomainEventInvalidationEvent invalidationEvent) => 
        !string.IsNullOrEmpty(invalidationEvent.DomainEventType);

    private string InferTableFromCommandType(string commandType)
    {
        // CreateUserCommand -> Users
        if (commandType.EndsWith("Command"))
        {
            var commandName = commandType.Replace("Command", "");
            var prefixesToRemove = new[] { "Create", "Update", "Delete", "Modify" };

            foreach (var prefix in prefixesToRemove)
            {
                if (commandName.StartsWith(prefix))
                {
                    commandName = commandName.Substring(prefix.Length);
                    break;
                }
            }

            return string.IsNullOrEmpty(commandName) ? string.Empty :
                   (commandName.EndsWith("s") ? commandName : commandName + "s");
        }

        return string.Empty;
    }

    private string InferTableFromEventType(string eventType)
    {
        // UserCreatedEvent -> Users
        if (eventType.EndsWith("Event"))
        {
            var eventName = eventType.Replace("Event", "");
            var suffixesToRemove = new[] { "Created", "Updated", "Deleted", "Changed", "Modified" };

            foreach (var suffix in suffixesToRemove)
            {
                if (eventName.EndsWith(suffix))
                {
                    eventName = eventName.Substring(0, eventName.Length - suffix.Length);
                    break;
                }
            }

            return string.IsNullOrEmpty(eventName) ? string.Empty :
                   (eventName.EndsWith("s") ? eventName : eventName + "s");
        }

        return string.Empty;
    }
}