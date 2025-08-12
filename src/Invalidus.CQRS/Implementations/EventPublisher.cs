using Invalidus.CQRS.Abstractions;

namespace Invalidus.CQRS.Implementations;

/// <summary>
/// 인메모리 이벤트 발행기 구현체
/// In-memory event publisher implementation
/// </summary>
public class InMemoryEventPublisher : IEventPublisher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventStore? _eventStore;
    private readonly ILogger<InMemoryEventPublisher> _logger;
    private readonly ConcurrentDictionary<Type, List<object>> _handlers = new();
    private readonly ConcurrentDictionary<string, List<Func<IInvalidationEvent, CancellationToken, Task>>> _streamHandlers = new();

    public InMemoryEventPublisher(
        IServiceProvider serviceProvider,
        ILogger<InMemoryEventPublisher> logger,
        IEventStore? eventStore = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventStore = eventStore;
    }

    public async Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default) 
        where TEvent : IInvalidationEvent
    {
        if (eventData == null) throw new ArgumentNullException(nameof(eventData));

        _logger.LogDebug("Publishing event {EventType} with ID {EventId}", 
            eventData.EventType, eventData.EventId);

        try
        {
            // Store event first if event store is available
            if (_eventStore != null)
            {
                await _eventStore.SaveEventAsync(eventData, cancellationToken);
            }

            // Get registered handlers for this event type
            var eventType = typeof(TEvent);
            var handlers = GetHandlersForType<TEvent>(eventType);

            if (handlers.Any())
            {
                // Execute all handlers concurrently
                var handlerTasks = handlers.Select(handler => 
                    ExecuteHandlerSafelyAsync(handler, eventData, cancellationToken));
                
                await Task.WhenAll(handlerTasks);
                
                _logger.LogDebug("Event {EventId} processed by {HandlerCount} handlers", 
                    eventData.EventId, handlers.Count());
            }
            else
            {
                _logger.LogDebug("No handlers found for event type {EventType}", eventType.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing event {EventType} with ID {EventId}", 
                eventData.EventType, eventData.EventId);
            throw;
        }
    }

    public async Task PublishBatchAsync(IEnumerable<IInvalidationEvent> events, CancellationToken cancellationToken = default)
    {
        if (events == null) throw new ArgumentNullException(nameof(events));

        var eventList = events.ToList();
        _logger.LogInformation("Publishing batch of {EventCount} events", eventList.Count);

        try
        {
            // Store events first if event store is available
            if (_eventStore != null)
            {
                await _eventStore.SaveEventsAsync(eventList, cancellationToken);
            }

            // Publish each event
            var publishTasks = eventList.Select(eventData => PublishSingleEventAsync(eventData, cancellationToken));
            await Task.WhenAll(publishTasks);
            
            _logger.LogInformation("Batch of {EventCount} events published successfully", eventList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing event batch of {EventCount} events", eventList.Count);
            throw;
        }
    }

    public async Task PublishToStreamAsync(string streamName, IInvalidationEvent eventData, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(streamName)) throw new ArgumentException("Stream name cannot be null or empty", nameof(streamName));
        if (eventData == null) throw new ArgumentNullException(nameof(eventData));

        _logger.LogDebug("Publishing event {EventType} to stream {StreamName}", 
            eventData.EventType, streamName);

        try
        {
            // Store event in stream if event store is available
            if (_eventStore != null)
            {
                await _eventStore.SaveEventAsync(eventData, cancellationToken);
            }

            // Execute stream-specific handlers
            if (_streamHandlers.TryGetValue(streamName, out var streamHandlers))
            {
                var handlerTasks = streamHandlers.Select(handler => 
                    ExecuteStreamHandlerSafelyAsync(handler, eventData, cancellationToken));
                
                await Task.WhenAll(handlerTasks);
                
                _logger.LogDebug("Stream event {EventId} processed by {HandlerCount} handlers", 
                    eventData.EventId, streamHandlers.Count);
            }

            // Also publish to regular handlers
            await PublishSingleEventAsync(eventData, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing event {EventType} to stream {StreamName}", 
                eventData.EventType, streamName);
            throw;
        }
    }

    private async Task PublishSingleEventAsync(IInvalidationEvent eventData, CancellationToken cancellationToken)
    {
        // Use reflection to call the generic PublishAsync method
        var eventType = eventData.GetType();
        var method = GetType().GetMethod(nameof(PublishAsync))!.MakeGenericMethod(eventType);
        var task = (Task)method.Invoke(this, new object[] { eventData, cancellationToken })!;
        await task;
    }

    private IEnumerable<IEventHandler<TEvent>> GetHandlersForType<TEvent>(Type eventType) 
        where TEvent : IInvalidationEvent
    {
        // Get handlers from DI container
        var handlersFromDI = _serviceProvider.GetServices<IEventHandler<TEvent>>();
        
        // Get handlers from internal registration
        var handlersFromRegistration = new List<IEventHandler<TEvent>>();
        if (_handlers.TryGetValue(eventType, out var registeredHandlers))
        {
            handlersFromRegistration.AddRange(registeredHandlers.Cast<IEventHandler<TEvent>>());
        }
        
        return handlersFromDI.Concat(handlersFromRegistration);
    }

    private async Task ExecuteHandlerSafelyAsync<TEvent>(IEventHandler<TEvent> handler, TEvent eventData, CancellationToken cancellationToken) 
        where TEvent : IInvalidationEvent
    {
        try
        {
            await handler.HandleAsync(eventData, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler {HandlerType} failed to process event {EventType} with ID {EventId}", 
                handler.GetType().Name, eventData.EventType, eventData.EventId);
            
            // Don't rethrow - we want other handlers to continue processing
        }
    }

    private async Task ExecuteStreamHandlerSafelyAsync(Func<IInvalidationEvent, CancellationToken, Task> handler, IInvalidationEvent eventData, CancellationToken cancellationToken)
    {
        try
        {
            await handler(eventData, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stream handler failed to process event {EventType} with ID {EventId}", 
                eventData.EventType, eventData.EventId);
            
            // Don't rethrow - we want other handlers to continue processing
        }
    }

    /// <summary>
    /// 핸들러를 런타임에 등록
    /// Register handler at runtime
    /// </summary>
    public void RegisterHandler<TEvent>(IEventHandler<TEvent> handler) where TEvent : IInvalidationEvent
    {
        var eventType = typeof(TEvent);
        _handlers.AddOrUpdate(eventType, 
            new List<object> { handler }, 
            (_, existing) => 
            {
                existing.Add(handler);
                return existing;
            });
        
        _logger.LogDebug("Handler {HandlerType} registered for event type {EventType}", 
            handler.GetType().Name, eventType.Name);
    }

    /// <summary>
    /// 스트림 핸들러를 런타임에 등록
    /// Register stream handler at runtime
    /// </summary>
    public void RegisterStreamHandler(string streamName, Func<IInvalidationEvent, CancellationToken, Task> handler)
    {
        _streamHandlers.AddOrUpdate(streamName,
            new List<Func<IInvalidationEvent, CancellationToken, Task>> { handler },
            (_, existing) =>
            {
                existing.Add(handler);
                return existing;
            });
        
        _logger.LogDebug("Stream handler registered for stream {StreamName}", streamName);
    }
}