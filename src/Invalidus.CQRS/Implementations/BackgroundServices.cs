using Invalidus.CQRS.Abstractions;

namespace Invalidus.CQRS.Implementations;

/// <summary>
/// Read Model 의존성 초기화 서비스
/// Read model dependency initializer service
/// </summary>
public class ReadModelDependencyInitializerService : BackgroundService
{
    private readonly IReadModelManager _readModelManager;
    private readonly List<ReadModelDependency> _dependencies;
    private readonly ILogger<ReadModelDependencyInitializerService> _logger;

    public ReadModelDependencyInitializerService(
        IReadModelManager readModelManager,
        List<ReadModelDependency> dependencies,
        ILogger<ReadModelDependencyInitializerService> logger)
    {
        _readModelManager = readModelManager ?? throw new ArgumentNullException(nameof(readModelManager));
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Initializing read model dependencies");

        try
        {
            // Register all dependencies
            foreach (var dependency in _dependencies)
            {
                await _readModelManager.RegisterReadModelDependencyAsync(
                    dependency.ReadModelType, 
                    dependency.DependsOnEntity, 
                    dependency.Properties, 
                    stoppingToken);
                
                _logger.LogDebug("Registered dependency: {ReadModelType} -> {Entity}", 
                    dependency.ReadModelType, dependency.DependsOnEntity);
            }

            _logger.LogInformation("Read model dependencies initialized: {DependencyCount} dependencies", 
                _dependencies.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing read model dependencies");
        }
    }
}

/// <summary>
/// 이벤트 구독 서비스
/// Event subscription service
/// </summary>
public class EventSubscriptionService : BackgroundService
{
    private readonly IEventSubscriber _eventSubscriber;
    private readonly ILogger<EventSubscriptionService> _logger;

    public EventSubscriptionService(
        IEventSubscriber eventSubscriber,
        ILogger<EventSubscriptionService> logger)
    {
        _eventSubscriber = eventSubscriber ?? throw new ArgumentNullException(nameof(eventSubscriber));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Event subscription service started");

        try
        {
            // Subscribe to all events for monitoring and logging
            await _eventSubscriber.SubscribeAllAsync(OnEventReceived, stoppingToken);

            // Keep service running
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in event subscription service");
        }
        finally
        {
            _logger.LogInformation("Event subscription service stopped");
        }
    }

    private Task OnEventReceived(IInvalidationEvent eventData, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Received event {EventType} with ID {EventId}", 
                eventData.EventType, eventData.EventId);
            
            // Additional event processing logic can be added here
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing event {EventId}", eventData.EventId);
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// 인메모리 이벤트 구독자 구현체
/// In-memory event subscriber implementation
/// </summary>
public class InMemoryEventSubscriber : IEventSubscriber
{
    private readonly ConcurrentDictionary<string, List<Func<IInvalidationEvent, CancellationToken, Task>>> _eventHandlers = new();
    private readonly ConcurrentDictionary<string, List<Func<IInvalidationEvent, CancellationToken, Task>>> _streamHandlers = new();
    private readonly List<Func<IInvalidationEvent, CancellationToken, Task>> _allEventHandlers = new();
    private readonly ILogger<InMemoryEventSubscriber> _logger;
    private readonly object _lock = new();

    public InMemoryEventSubscriber(ILogger<InMemoryEventSubscriber> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task SubscribeAsync<TEvent>(Func<TEvent, CancellationToken, Task> handler, CancellationToken cancellationToken = default) 
        where TEvent : IInvalidationEvent
    {
        var eventType = typeof(TEvent).Name;
        
        lock (_lock)
        {
            _eventHandlers.AddOrUpdate(eventType,
                new List<Func<IInvalidationEvent, CancellationToken, Task>> { (e, ct) => handler((TEvent)e, ct) },
                (_, existing) =>
                {
                    existing.Add((e, ct) => handler((TEvent)e, ct));
                    return existing;
                });
        }

        _logger.LogDebug("Subscribed to event type {EventType}", eventType);
        
        return Task.CompletedTask;
    }

    public Task SubscribeToStreamAsync(string streamName, Func<IInvalidationEvent, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _streamHandlers.AddOrUpdate(streamName,
                new List<Func<IInvalidationEvent, CancellationToken, Task>> { handler },
                (_, existing) =>
                {
                    existing.Add(handler);
                    return existing;
                });
        }

        _logger.LogDebug("Subscribed to stream {StreamName}", streamName);
        
        return Task.CompletedTask;
    }

    public Task SubscribeAllAsync(Func<IInvalidationEvent, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _allEventHandlers.Add(handler);
        }

        _logger.LogDebug("Subscribed to all events");
        
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        // In a real implementation, you'd track subscription IDs
        _logger.LogDebug("Unsubscribed from {SubscriptionId}", subscriptionId);
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// 이벤트를 구독자들에게 전달
    /// Deliver event to subscribers
    /// </summary>
    public async Task NotifyEventAsync(IInvalidationEvent eventData, CancellationToken cancellationToken = default)
    {
        var eventType = eventData.EventType;
        var tasks = new List<Task>();

        // Notify specific event type handlers
        if (_eventHandlers.TryGetValue(eventType, out var typeHandlers))
        {
            tasks.AddRange(typeHandlers.Select(handler => 
                ExecuteHandlerSafely(handler, eventData, cancellationToken)));
        }

        // Notify all event handlers
        lock (_lock)
        {
            tasks.AddRange(_allEventHandlers.Select(handler => 
                ExecuteHandlerSafely(handler, eventData, cancellationToken)));
        }

        if (tasks.Any())
        {
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// 스트림 이벤트를 구독자들에게 전달
    /// Deliver stream event to subscribers
    /// </summary>
    public async Task NotifyStreamEventAsync(string streamName, IInvalidationEvent eventData, CancellationToken cancellationToken = default)
    {
        if (_streamHandlers.TryGetValue(streamName, out var streamHandlers))
        {
            var tasks = streamHandlers.Select(handler => 
                ExecuteHandlerSafely(handler, eventData, cancellationToken));
            
            await Task.WhenAll(tasks);
        }
    }

    private async Task ExecuteHandlerSafely(Func<IInvalidationEvent, CancellationToken, Task> handler, IInvalidationEvent eventData, CancellationToken cancellationToken)
    {
        try
        {
            await handler(eventData, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing event handler for event {EventType} with ID {EventId}", 
                eventData.EventType, eventData.EventId);
        }
    }
}