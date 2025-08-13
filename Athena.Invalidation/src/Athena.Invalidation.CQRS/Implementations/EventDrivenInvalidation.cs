using Athena.Invalidation.CQRS.Abstractions;

namespace Athena.Invalidation.CQRS.Implementations;

/// <summary>
/// 도메인 이벤트 기반 캐시 무효화 구현
/// </summary>
public class EventDrivenInvalidation(
    IInvalidationEngine invalidationEngine,
    ILogger<EventDrivenInvalidation> logger)
    : IEventDrivenInvalidation
{
    private readonly IInvalidationEngine _invalidationEngine = invalidationEngine ?? throw new ArgumentNullException(nameof(invalidationEngine));
    private readonly ILogger<EventDrivenInvalidation> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<Type, object> _eventHandlers = new();

    public async Task HandleEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) 
        where TEvent : IDomainEvent
    {
        if (!CanHandle(domainEvent))
        {
            _logger.LogDebug("No handler registered for event {EventType}:{EventId}", 
                domainEvent.EventType, domainEvent.EventId);
            return;
        }

        try
        {
            _logger.LogDebug("Handling invalidation for event {EventType}:{EventId}", 
                domainEvent.EventType, domainEvent.EventId);

            var startTime = DateTimeOffset.UtcNow;

            // 등록된 특정 핸들러 사용
            if (_eventHandlers.TryGetValue(typeof(TEvent), out var handlerObj) &&
                handlerObj is IEventInvalidationHandler<TEvent> handler)
            {
                await ProcessWithSpecificHandler(domainEvent, handler, cancellationToken);
            }
            else
            {
                // 기본 이벤트 처리 로직
                await ProcessWithDefaultHandler(domainEvent, cancellationToken);
            }

            var duration = DateTimeOffset.UtcNow - startTime;
            _logger.LogInformation("Completed event-driven invalidation for {EventType}:{EventId} in {Duration}ms",
                domainEvent.EventType, domainEvent.EventId, duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle event-driven invalidation for {EventType}:{EventId}",
                domainEvent.EventType, domainEvent.EventId);
            throw;
        }
    }

    public bool CanHandle<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent
    {
        if (domainEvent == null) return false;

        // 특정 핸들러가 등록되어 있거나, 이벤트에서 무효화 정보를 제공하는 경우
        return _eventHandlers.ContainsKey(typeof(TEvent)) || 
               HasInvalidationMetadata(domainEvent);
    }

    public void RegisterEventHandler<TEvent>(IEventInvalidationHandler<TEvent> handler) 
        where TEvent : IDomainEvent
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        _eventHandlers[typeof(TEvent)] = handler;
        _logger.LogInformation("Registered event invalidation handler for event type {EventType} with priority {Priority}",
            typeof(TEvent).Name, handler.Priority);
    }

    public void UnregisterEventHandler<TEvent>() where TEvent : IDomainEvent
    {
        if (_eventHandlers.TryRemove(typeof(TEvent), out _))
        {
            _logger.LogInformation("Unregistered event invalidation handler for event type {EventType}", 
                typeof(TEvent).Name);
        }
    }

    public IEnumerable<Type> GetRegisteredEventTypes()
    {
        return _eventHandlers.Keys.ToList();
    }

    private async Task ProcessWithSpecificHandler<TEvent>(
        TEvent domainEvent, 
        IEventInvalidationHandler<TEvent> handler, 
        CancellationToken cancellationToken) where TEvent : IDomainEvent
    {
        // 테이블 기반 무효화
        var tables = await handler.GetInvalidationTablesAsync(domainEvent, cancellationToken);
        if (tables.Any())
        {
            foreach (var table in tables)
            {
                await _invalidationEngine.InvalidateByTableAsync(table, cancellationToken);
            }
            _logger.LogDebug("Invalidated tables [{Tables}] for event {EventType}", 
                string.Join(", ", tables), domainEvent.EventType);
        }

        // 패턴 기반 무효화
        var patterns = await handler.GetInvalidationPatternsAsync(domainEvent, cancellationToken);
        if (patterns.Any())
        {
            foreach (var pattern in patterns)
            {
                await _invalidationEngine.InvalidateByPatternAsync(pattern, cancellationToken);
            }
            _logger.LogDebug("Invalidated patterns [{Patterns}] for event {EventType}", 
                string.Join(", ", patterns), domainEvent.EventType);
        }

        // 계층적 무효화
        var hierarchicalTargets = await handler.GetHierarchicalTargetsAsync(domainEvent, cancellationToken);
        if (hierarchicalTargets.Any())
        {
            foreach (var target in hierarchicalTargets)
            {
                await _invalidationEngine.InvalidateHierarchyAsync(
                    target.RootTable, 
                    target.RelatedTables, 
                    target.MaxDepth, 
                    cancellationToken);
            }
            _logger.LogDebug("Performed hierarchical invalidation for {Count} targets for event {EventType}", 
                hierarchicalTargets.Count(), domainEvent.EventType);
        }
    }

    private async Task ProcessWithDefaultHandler<TEvent>(TEvent domainEvent, CancellationToken cancellationToken) 
        where TEvent : IDomainEvent
    {
        // 이벤트 메타데이터에서 무효화 정보 추출
        var tables = GetInvalidationTablesFromEvent(domainEvent);
        var patterns = GetInvalidationPatternsFromEvent(domainEvent);

        // 이벤트 타입에서 테이블 이름 추론
        var inferredTable = InferTableFromEventType(domainEvent.EventType);
        if (!string.IsNullOrEmpty(inferredTable))
        {
            tables.Add(inferredTable);
        }

        // 집계 루트 ID를 기반으로 패턴 생성
        if (!string.IsNullOrEmpty(domainEvent.AggregateId) && !string.IsNullOrEmpty(inferredTable))
        {
            var aggregatePattern = $"{inferredTable.ToLower()}:{domainEvent.AggregateId}:*";
            patterns.Add(aggregatePattern);
        }

        // 무효화 실행
        if (tables.Any())
        {
            await _invalidationEngine.InvalidateBatchAsync(tables, cancellationToken);
        }

        if (patterns.Any())
        {
            foreach (var pattern in patterns)
            {
                await _invalidationEngine.InvalidateByPatternAsync(pattern, cancellationToken);
            }
        }

        _logger.LogDebug("Default event handling for {EventType} - Tables: [{Tables}], Patterns: [{Patterns}]",
            domainEvent.EventType, string.Join(", ", tables), string.Join(", ", patterns));
    }

    private bool HasInvalidationMetadata<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent
    {
        return domainEvent.Metadata.ContainsKey("InvalidationTables") ||
               domainEvent.Metadata.ContainsKey("InvalidationPatterns") ||
               domainEvent.Metadata.ContainsKey("HierarchicalTargets") ||
               !string.IsNullOrEmpty(domainEvent.AggregateId);
    }

    private List<string> GetInvalidationTablesFromEvent<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent
    {
        var tables = new List<string>();

        if (domainEvent.Metadata.TryGetValue("InvalidationTables", out var tablesObj) &&
            tablesObj is IEnumerable<string> tableNames)
        {
            tables.AddRange(tableNames);
        }

        return tables;
    }

    private List<string> GetInvalidationPatternsFromEvent<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent
    {
        var patterns = new List<string>();

        if (domainEvent.Metadata.TryGetValue("InvalidationPatterns", out var patternsObj) &&
            patternsObj is IEnumerable<string> patternNames)
        {
            patterns.AddRange(patternNames);
        }

        return patterns;
    }

    private string InferTableFromEventType(string eventType)
    {
        // UserCreatedEvent -> Users
        // OrderUpdatedEvent -> Orders
        // ProductDeletedEvent -> Products
        
        if (eventType.EndsWith("Event"))
        {
            var eventName = eventType.Replace("Event", "");
            
            // Remove common suffixes
            var prefixesToRemove = new[] { "Created", "Updated", "Deleted", "Changed", "Modified" };
            foreach (var prefix in prefixesToRemove)
            {
                if (eventName.EndsWith(prefix))
                {
                    eventName = eventName.Substring(0, eventName.Length - prefix.Length);
                    break;
                }
            }

            // Pluralize
            return string.IsNullOrEmpty(eventName) ? string.Empty : 
                   (eventName.EndsWith("s") ? eventName : eventName + "s");
        }

        return string.Empty;
    }
}
