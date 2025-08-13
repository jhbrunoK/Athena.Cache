using Athena.Invalidation.Distributed.Abstractions;
using Athena.Invalidation.Distributed.Models;

namespace Athena.Invalidation.Distributed.Core;

/// <summary>
/// 분산 캐시 무효화 엔진 - 로컬 무효화 + 분산 이벤트 발행
/// </summary>
public class DistributedInvalidationEngine(
    IInvalidationEngine localEngine,
    IDistributedEventBus eventBus,
    ILogger<DistributedInvalidationEngine> logger,
    IOptions<DistributedInvalidationOptions> options)
    : IInvalidationEngine, IAsyncDisposable
{
    private readonly IInvalidationEngine _localEngine = localEngine ?? throw new ArgumentNullException(nameof(localEngine));
    private readonly IDistributedEventBus _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    private readonly ILogger<DistributedInvalidationEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly DistributedInvalidationOptions _options = options.Value ?? new DistributedInvalidationOptions();
    
    private volatile bool _disposed = false;

    public async Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DistributedInvalidationEngine));

        try
        {
            // 로컬 무효화 먼저 실행
            await _localEngine.InvalidateByTableAsync(tableName, cancellationToken);

            // 분산 이벤트 발행 (옵션에 따라)
            if (_options.PublishEvents)
            {
                var distributedEvent = new TableInvalidationEvent(tableName, _options.NodeId);
                await _eventBus.PublishInvalidationEventAsync(distributedEvent, cancellationToken);
                
                _logger.LogDebug("Published distributed table invalidation event for {TableName}", tableName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform distributed table invalidation for {TableName}", tableName);
            throw;
        }
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DistributedInvalidationEngine));

        try
        {
            // 로컬 무효화 먼저 실행
            await _localEngine.InvalidateByPatternAsync(pattern, cancellationToken);

            // 분산 이벤트 발행
            if (_options.PublishEvents)
            {
                var distributedEvent = new PatternInvalidationEvent(pattern, _options.NodeId);
                await _eventBus.PublishInvalidationEventAsync(distributedEvent, cancellationToken);
                
                _logger.LogDebug("Published distributed pattern invalidation event for {Pattern}", pattern);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform distributed pattern invalidation for {Pattern}", pattern);
            throw;
        }
    }

    public async Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DistributedInvalidationEngine));

        try
        {
            // 로컬 무효화 먼저 실행
            await _localEngine.InvalidateByKeyAsync(key, cancellationToken);

            // 분산 이벤트 발행
            if (_options.PublishEvents)
            {
                var distributedEvent = new KeyInvalidationEvent(key, _options.NodeId);
                await _eventBus.PublishInvalidationEventAsync(distributedEvent, cancellationToken);
                
                _logger.LogDebug("Published distributed key invalidation event for {Key}", key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform distributed key invalidation for {Key}", key);
            throw;
        }
    }

    public async Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DistributedInvalidationEngine));

        var tableArray = tableNames.ToArray();
        
        try
        {
            // 로컬 무효화 먼저 실행
            await _localEngine.InvalidateBatchAsync(tableArray, cancellationToken);

            // 분산 이벤트 발행
            if (_options.PublishEvents)
            {
                var distributedEvent = new BatchInvalidationEvent(tableArray, _options.NodeId);
                await _eventBus.PublishInvalidationEventAsync(distributedEvent, cancellationToken);
                
                _logger.LogDebug("Published distributed batch invalidation event for {TableCount} tables", tableArray.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform distributed batch invalidation for {Tables}", 
                string.Join(", ", tableArray));
            throw;
        }
    }

    public async Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DistributedInvalidationEngine));

        try
        {
            // 로컬 무효화 먼저 실행
            await _localEngine.InvalidateHierarchyAsync(tableName, relatedTables, maxDepth, cancellationToken);

            // 분산 이벤트 발행
            if (_options.PublishEvents)
            {
                var distributedEvent = new HierarchicalInvalidationEvent(tableName, relatedTables, maxDepth, _options.NodeId);
                await _eventBus.PublishInvalidationEventAsync(distributedEvent, cancellationToken);
                
                _logger.LogDebug("Published distributed hierarchical invalidation event for {TableName}", tableName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform distributed hierarchical invalidation for {TableName}", tableName);
            throw;
        }
    }

    // CQRS 메서드들도 분산 이벤트와 함께 처리
    public async Task InvalidateOnCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) 
        where TCommand : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DistributedInvalidationEngine));

        try
        {
            // 로컬 무효화 먼저 실행
            await _localEngine.InvalidateOnCommandAsync(command, cancellationToken);

            // 분산 이벤트 발행
            if (_options.PublishEvents && _options.PublishCQRSEvents)
            {
                var commandData = System.Text.Json.JsonSerializer.Serialize(command);
                var distributedEvent = new CommandInvalidationEvent(typeof(TCommand).Name, commandData, _options.NodeId);
                await _eventBus.PublishInvalidationEventAsync(distributedEvent, cancellationToken);
                
                _logger.LogDebug("Published distributed command invalidation event for {CommandType}", typeof(TCommand).Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform distributed command invalidation for {CommandType}", typeof(TCommand).Name);
            throw;
        }
    }

    public async Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) 
        where TEvent : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DistributedInvalidationEngine));

        try
        {
            // 로컬 무효화 먼저 실행
            await _localEngine.InvalidateOnEventAsync(domainEvent, cancellationToken);

            // 분산 이벤트 발행
            if (_options.PublishEvents && _options.PublishCQRSEvents)
            {
                var eventData = System.Text.Json.JsonSerializer.Serialize(domainEvent);
                
                // AggregateId 추출 시도 (리플렉션 사용)
                string? aggregateId = null;
                var aggregateIdProperty = typeof(TEvent).GetProperty("AggregateId");
                if (aggregateIdProperty != null)
                {
                    aggregateId = aggregateIdProperty.GetValue(domainEvent)?.ToString();
                }

                var distributedEvent = new DomainEventInvalidationEvent(typeof(TEvent).Name, eventData, aggregateId, _options.NodeId);
                await _eventBus.PublishInvalidationEventAsync(distributedEvent, cancellationToken);
                
                _logger.LogDebug("Published distributed domain event invalidation event for {EventType}", typeof(TEvent).Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform distributed domain event invalidation for {EventType}", typeof(TEvent).Name);
            throw;
        }
    }

    // 다른 메서드들은 로컬 엔진에 위임
    public Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default) 
        where TReadModel : class
    {
        return _localEngine.InvalidateReadModelAsync<TReadModel>(modelId, cancellationToken);
    }

    public Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default) 
        where TProjection : class
    {
        return _localEngine.InvalidateProjectionAsync<TProjection>(projectionId, cancellationToken);
    }

    public Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default)
    {
        return _localEngine.TrackCacheKeyAsync(tableName, cacheKey, cancellationToken);
    }

    public Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default)
    {
        return _localEngine.TrackCacheKeyAsync(tableNames, cacheKey, cancellationToken);
    }

    public Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default)
    {
        return _localEngine.GetTrackedKeysAsync(tableName, cancellationToken);
    }

    public Task RegisterInvalidationRuleAsync(IInvalidationRule rule, CancellationToken cancellationToken = default)
    {
        return _localEngine.RegisterInvalidationRuleAsync(rule, cancellationToken);
    }

    public Task UnregisterInvalidationRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        return _localEngine.UnregisterInvalidationRuleAsync(ruleId, cancellationToken);
    }

    public Task<IEnumerable<IInvalidationRule>> GetInvalidationRulesAsync(CancellationToken cancellationToken = default)
    {
        return _localEngine.GetInvalidationRulesAsync(cancellationToken);
    }

    public IInvalidationContext CreateContext(InvalidationTrigger trigger, object? metadata = null)
    {
        return _localEngine.CreateContext(trigger, metadata);
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DistributedInvalidationEngine));

        try
        {
            // 로컬 무효화 먼저 실행
            await _localEngine.ClearAllAsync(cancellationToken);

            // 분산 환경에서는 ClearAll을 조심스럽게 처리
            if (_options.PublishEvents && _options.PublishClearAllEvents)
            {
                _logger.LogWarning("Publishing distributed ClearAll event - this will clear ALL caches across the cluster!");
                
                var distributedEvent = new DistributedInvalidationEvent
                {
                    EventType = "ClearAllEvent",
                    SourceNodeId = _options.NodeId,
                    Target = "*",
                    Type = InvalidationType.Custom
                };
                distributedEvent.Metadata["operation"] = "clear_all";
                
                await _eventBus.PublishInvalidationEventAsync(distributedEvent, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform distributed clear all operation");
            throw;
        }
    }

    public async Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var localStatus = await _localEngine.GetStatusAsync(cancellationToken);
        
        // 분산 관련 메트릭 추가
        var eventBusStats = await _eventBus.GetStatisticsAsync(cancellationToken);
        localStatus.Metrics["DistributedEventBus_IsConnected"] = eventBusStats.IsConnected;
        localStatus.Metrics["DistributedEventBus_PublishedEvents"] = eventBusStats.PublishedEvents;
        localStatus.Metrics["DistributedEventBus_ConsumedEvents"] = eventBusStats.ConsumedEvents;
        localStatus.Metrics["DistributedEventBus_FailedEvents"] = eventBusStats.FailedEvents;
        localStatus.Metrics["DistributedEventBus_AverageProcessingTime"] = eventBusStats.AverageProcessingTime.TotalMilliseconds;

        // 이벤트 버스 연결 상태에 따라 전체 헬스 상태 결정
        if (!eventBusStats.IsConnected)
        {
            localStatus.IsHealthy = false;
            localStatus.Metrics["DistributedEventBus_Error"] = "Event bus is not connected";
        }

        return localStatus;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        try
        {
            if (_eventBus is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }

            if (_localEngine is IAsyncDisposable localAsyncDisposable)
            {
                await localAsyncDisposable.DisposeAsync();
            }
            else if (_localEngine is IDisposable localDisposable)
            {
                localDisposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during DistributedInvalidationEngine disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// 분산 무효화 엔진 구성 옵션
/// </summary>
public class DistributedInvalidationOptions
{
    /// <summary>현재 노드 식별자</summary>
    public string NodeId { get; set; } = Environment.MachineName;
    
    /// <summary>분산 이벤트 발행 여부</summary>
    public bool PublishEvents { get; set; } = true;
    
    /// <summary>CQRS 이벤트 발행 여부</summary>
    public bool PublishCQRSEvents { get; set; } = true;
    
    /// <summary>ClearAll 이벤트 발행 여부 (위험)</summary>
    public bool PublishClearAllEvents { get; set; } = false;
    
    /// <summary>로컬 먼저 실행 여부</summary>
    public bool LocalFirst { get; set; } = true;
    
    /// <summary>이벤트 발행 실패 시 로컬 무효화 롤백 여부</summary>
    public bool RollbackOnEventPublishFailure { get; set; } = false;
}
