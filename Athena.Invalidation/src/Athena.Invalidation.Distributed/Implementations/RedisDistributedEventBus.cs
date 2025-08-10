using Athena.Invalidation.Distributed.Abstractions;
using Athena.Invalidation.Distributed.Models;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Athena.Invalidation.Distributed.Implementations;

/// <summary>
/// Redis 기반 분산 이벤트 버스 구현
/// </summary>
public class RedisDistributedEventBus : IDistributedEventBus, IAsyncDisposable
{
    private readonly IDatabase _database;
    private readonly ISubscriber _subscriber;
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILogger<RedisDistributedEventBus> _logger;
    private readonly RedisEventBusOptions _options;
    private readonly string _nodeId;
    
    private readonly ConcurrentDictionary<Type, List<object>> _handlers = new();
    private readonly ConcurrentDictionary<string, ChannelMessageQueue> _subscriptions = new();
    private readonly EventBusStatistics _statistics = new();
    
    private volatile bool _isStarted = false;
    private volatile bool _disposed = false;

    public RedisDistributedEventBus(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisDistributedEventBus> logger,
        IOptions<RedisEventBusOptions> options)
    {
        _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
        _database = _connectionMultiplexer.GetDatabase();
        _subscriber = _connectionMultiplexer.GetSubscriber();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _nodeId = _options.NodeId ?? Environment.MachineName;
        
        _connectionMultiplexer.ConnectionFailed += OnConnectionFailed;
        _connectionMultiplexer.ConnectionRestored += OnConnectionRestored;
    }

    public async Task PublishInvalidationEventAsync<T>(T invalidationEvent, CancellationToken cancellationToken = default) 
        where T : class, IDistributedInvalidationEvent
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisDistributedEventBus));
        if (!_isStarted) throw new InvalidOperationException("Event bus is not started");

        try
        {
            var startTime = DateTimeOffset.UtcNow;
            
            // 자신이 발행한 이벤트는 원본 노드 ID 설정
            if (string.IsNullOrEmpty(invalidationEvent.SourceNodeId))
            {
                invalidationEvent.SourceNodeId = _nodeId;
            }

            var channelKey = GetChannelKey<T>();
            var serializedEvent = JsonSerializer.Serialize(invalidationEvent, _options.JsonOptions);
            
            var subscriberCount = await _subscriber.PublishAsync(RedisChannel.Literal(channelKey), serializedEvent);
            
            var duration = DateTimeOffset.UtcNow - startTime;
            
            _statistics.PublishedEvents++;
            _statistics.AverageProcessingTime = CalculateAverageTime(_statistics.AverageProcessingTime, duration);
            
            if (_options.LogEvents)
            {
                _logger.LogDebug("Published {EventType} to {SubscriberCount} subscribers in {Duration}ms: {EventId}",
                    typeof(T).Name, subscriberCount, duration.TotalMilliseconds, invalidationEvent.EventId);
            }
        }
        catch (Exception ex)
        {
            _statistics.FailedEvents++;
            _logger.LogError(ex, "Failed to publish invalidation event {EventType}:{EventId}",
                typeof(T).Name, invalidationEvent.EventId);
            throw;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisDistributedEventBus));
        if (_isStarted) return;

        try
        {
            await EnsureConnectionAsync();
            
            // 등록된 핸들러들에 대한 구독 시작
            foreach (var handlerType in _handlers.Keys)
            {
                var channelKey = GetChannelKey(handlerType);
                await SubscribeToChannelAsync(channelKey, handlerType);
            }
            
            _isStarted = true;
            _statistics.IsConnected = true;
            
            _logger.LogInformation("Redis distributed event bus started for node {NodeId}", _nodeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Redis distributed event bus");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !_isStarted) return;

        try
        {
            // 모든 구독 해제
            var unsubscribeTasks = _subscriptions.Select(async kvp =>
            {
                try
                {
                    await _subscriber.UnsubscribeAsync(RedisChannel.Literal(kvp.Key));
                    _logger.LogDebug("Unsubscribed from channel {ChannelKey}", kvp.Key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error unsubscribing from channel {ChannelKey}", kvp.Key);
                }
            });
            
            await Task.WhenAll(unsubscribeTasks);
            
            _subscriptions.Clear();
            _isStarted = false;
            _statistics.IsConnected = false;
            
            _logger.LogInformation("Redis distributed event bus stopped for node {NodeId}", _nodeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping Redis distributed event bus");
        }
    }

    public void Subscribe<T>(IDistributedInvalidationEventHandler<T> handler) 
        where T : class, IDistributedInvalidationEvent
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisDistributedEventBus));
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        var eventType = typeof(T);
        _handlers.AddOrUpdate(eventType,
            [handler],
            (key, existing) =>
            {
                existing.Add(handler);
                existing.Sort((a, b) => 
                    ((IDistributedInvalidationEventHandler<T>)b).Priority.CompareTo(
                        ((IDistributedInvalidationEventHandler<T>)a).Priority));
                return existing;
            });

        _logger.LogDebug("Subscribed handler {HandlerType} for event type {EventType} with priority {Priority}",
            handler.GetType().Name, typeof(T).Name, handler.Priority);

        // 이미 시작된 경우 즉시 구독 시작
        if (_isStarted)
        {
            var channelKey = GetChannelKey<T>();
            _ = Task.Run(async () =>
            {
                try
                {
                    await SubscribeToChannelAsync(channelKey, eventType);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to subscribe to channel {ChannelKey}", channelKey);
                }
            });
        }
    }

    public void Unsubscribe<T>(IDistributedInvalidationEventHandler<T> handler) 
        where T : class, IDistributedInvalidationEvent
    {
        if (_disposed) return;
        if (handler == null) return;

        var eventType = typeof(T);
        if (_handlers.TryGetValue(eventType, out var handlerList))
        {
            handlerList.Remove(handler);
            if (!handlerList.Any())
            {
                _handlers.TryRemove(eventType, out _);
                
                // 채널 구독도 해제
                var channelKey = GetChannelKey<T>();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _subscriber.UnsubscribeAsync(RedisChannel.Literal(channelKey));
                        _subscriptions.TryRemove(channelKey, out _);
                        _logger.LogDebug("Unsubscribed from channel {ChannelKey}", channelKey);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error unsubscribing from channel {ChannelKey}", channelKey);
                    }
                });
            }
        }

        _logger.LogDebug("Unsubscribed handler {HandlerType} for event type {EventType}",
            handler.GetType().Name, typeof(T).Name);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

        try
        {
            return _connectionMultiplexer.IsConnected && 
                   await _database.PingAsync() != TimeSpan.Zero;
        }
        catch
        {
            return false;
        }
    }

    public Task<EventBusStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        _statistics.IsConnected = _connectionMultiplexer.IsConnected;
        _statistics.ProviderSpecificMetrics["NodeId"] = _nodeId;
        _statistics.ProviderSpecificMetrics["SubscribedChannels"] = _subscriptions.Count;
        _statistics.ProviderSpecificMetrics["RegisteredHandlerTypes"] = _handlers.Count;
        _statistics.ProviderSpecificMetrics["TotalHandlers"] = _handlers.Values.Sum(h => h.Count);
        
        return Task.FromResult(_statistics);
    }

    private async Task SubscribeToChannelAsync(string channelKey, Type eventType)
    {
        if (_subscriptions.ContainsKey(channelKey)) return;

        var queue = await _subscriber.SubscribeAsync(RedisChannel.Literal(channelKey));
        _subscriptions[channelKey] = queue;
        
        queue.OnMessage(async message =>
        {
            await ProcessIncomingMessage(message, eventType);
        });

        _logger.LogDebug("Subscribed to channel {ChannelKey} for event type {EventType}", channelKey, eventType.Name);
    }

    private async Task ProcessIncomingMessage(ChannelMessage message, Type eventType)
    {
        var startTime = DateTimeOffset.UtcNow;
        
        try
        {
            var deserializedEvent = JsonSerializer.Deserialize(message.Message.ToString(), eventType, _options.JsonOptions);
            if (deserializedEvent is not IDistributedInvalidationEvent invalidationEvent)
            {
                _logger.LogWarning("Received invalid event format for type {EventType}", eventType.Name);
                return;
            }

            // 자신이 발행한 이벤트는 무시 (루프백 방지)
            if (invalidationEvent.SourceNodeId == _nodeId)
            {
                if (_options.LogEvents)
                {
                    _logger.LogTrace("Ignoring loopback event {EventId} from same node {NodeId}", 
                        invalidationEvent.EventId, _nodeId);
                }
                return;
            }

            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                var processingTasks = handlers.Cast<IDistributedInvalidationEventHandler<IDistributedInvalidationEvent>>()
                    .Where(h => h.CanHandle(invalidationEvent))
                    .Select(async handler =>
                    {
                        try
                        {
                            await handler.HandleAsync(invalidationEvent, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Handler {HandlerType} failed to process event {EventId}",
                                handler.GetType().Name, invalidationEvent.EventId);
                        }
                    });

                await Task.WhenAll(processingTasks);
            }

            _statistics.ConsumedEvents++;
            
            var duration = DateTimeOffset.UtcNow - startTime;
            _statistics.AverageProcessingTime = CalculateAverageTime(_statistics.AverageProcessingTime, duration);

            if (_options.LogEvents)
            {
                _logger.LogDebug("Processed event {EventType}:{EventId} from node {SourceNode} in {Duration}ms",
                    eventType.Name, invalidationEvent.EventId, invalidationEvent.SourceNodeId, duration.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _statistics.FailedEvents++;
            _logger.LogError(ex, "Error processing incoming message for event type {EventType}", eventType.Name);
        }
    }

    private async Task EnsureConnectionAsync()
    {
        if (!_connectionMultiplexer.IsConnected)
        {
            _logger.LogWarning("Redis connection is not available, attempting to reconnect...");
            
            // Redis 연결이 자동으로 복구되기를 기다림
            var timeout = TimeSpan.FromSeconds(30);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            while (!_connectionMultiplexer.IsConnected && stopwatch.Elapsed < timeout)
            {
                await Task.Delay(1000);
            }
            
            if (!_connectionMultiplexer.IsConnected)
            {
                throw new InvalidOperationException("Failed to establish Redis connection within timeout period");
            }
        }
    }

    private string GetChannelKey<T>() where T : class, IDistributedInvalidationEvent
    {
        return GetChannelKey(typeof(T));
    }

    private string GetChannelKey(Type eventType)
    {
        return $"{_options.ChannelPrefix}:{eventType.Name}";
    }

    private static TimeSpan CalculateAverageTime(TimeSpan currentAverage, TimeSpan newValue)
    {
        // 간단한 이동 평균 계산
        return TimeSpan.FromMilliseconds((currentAverage.TotalMilliseconds * 0.9) + (newValue.TotalMilliseconds * 0.1));
    }

    private void OnConnectionFailed(object? sender, ConnectionFailedEventArgs e)
    {
        _statistics.IsConnected = false;
        _logger.LogError(e.Exception, "Redis connection failed: {FailureType} - {EndPoint}",
            e.FailureType, e.EndPoint);
    }

    private void OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        _statistics.IsConnected = true;
        _logger.LogInformation("Redis connection restored: {EndPoint}", e.EndPoint);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        try
        {
            await StopAsync(CancellationToken.None);
            
            _connectionMultiplexer.ConnectionFailed -= OnConnectionFailed;
            _connectionMultiplexer.ConnectionRestored -= OnConnectionRestored;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during RedisDistributedEventBus disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Redis 이벤트 버스 구성 옵션
/// </summary>
public class RedisEventBusOptions
{
    public string NodeId { get; set; } = Environment.MachineName;
    public string ChannelPrefix { get; set; } = "athena-invalidation";
    public bool LogEvents { get; set; } = false;
    public JsonSerializerOptions JsonOptions { get; set; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
