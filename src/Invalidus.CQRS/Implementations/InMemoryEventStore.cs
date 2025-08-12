using Invalidus.CQRS.Abstractions;
using System.Collections.Concurrent;

namespace Invalidus.CQRS.Implementations;

/// <summary>
/// 인메모리 이벤트 저장소 구현체
/// In-memory event store implementation
/// </summary>
public class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentQueue<StoredEvent> _events = new();
    private readonly ConcurrentDictionary<string, List<StoredEvent>> _aggregateEvents = new();
    private readonly ConcurrentDictionary<string, List<StoredEvent>> _streamEvents = new();
    private readonly ILogger<InMemoryEventStore> _logger;
    private readonly object _lock = new();
    private long _globalVersion = 0;

    public InMemoryEventStore(ILogger<InMemoryEventStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task SaveEventAsync(IInvalidationEvent eventData, CancellationToken cancellationToken = default)
    {
        if (eventData == null) throw new ArgumentNullException(nameof(eventData));

        var storedEvent = CreateStoredEvent(eventData);
        
        lock (_lock)
        {
            _events.Enqueue(storedEvent);
            
            // Store by correlation ID (acting as aggregate ID)
            if (!string.IsNullOrEmpty(eventData.CorrelationId))
            {
                _aggregateEvents.AddOrUpdate(eventData.CorrelationId,
                    new List<StoredEvent> { storedEvent },
                    (_, existing) =>
                    {
                        existing.Add(storedEvent);
                        return existing;
                    });
            }
        }

        _logger.LogDebug("Event {EventId} of type {EventType} saved to store", 
            eventData.EventId, eventData.EventType);

        return Task.CompletedTask;
    }

    public Task SaveEventsAsync(IEnumerable<IInvalidationEvent> events, CancellationToken cancellationToken = default)
    {
        if (events == null) throw new ArgumentNullException(nameof(events));

        var eventList = events.ToList();
        var storedEvents = eventList.Select(CreateStoredEvent).ToList();

        lock (_lock)
        {
            foreach (var storedEvent in storedEvents)
            {
                _events.Enqueue(storedEvent);
                
                // Store by correlation ID
                var correlationId = storedEvent.Event.CorrelationId;
                if (!string.IsNullOrEmpty(correlationId))
                {
                    _aggregateEvents.AddOrUpdate(correlationId,
                        new List<StoredEvent> { storedEvent },
                        (_, existing) =>
                        {
                            existing.Add(storedEvent);
                            return existing;
                        });
                }
            }
        }

        _logger.LogDebug("Batch of {EventCount} events saved to store", eventList.Count);

        return Task.CompletedTask;
    }

    public Task<IEnumerable<IInvalidationEvent>> GetEventsAsync(string aggregateId, DateTime? fromTimestamp = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(aggregateId)) 
            throw new ArgumentException("Aggregate ID cannot be null or empty", nameof(aggregateId));

        var results = new List<IInvalidationEvent>();

        if (_aggregateEvents.TryGetValue(aggregateId, out var aggregateEvents))
        {
            var filteredEvents = aggregateEvents.AsEnumerable();
            
            if (fromTimestamp.HasValue)
            {
                filteredEvents = filteredEvents.Where(se => se.Event.Timestamp >= fromTimestamp.Value);
            }

            results.AddRange(filteredEvents
                .OrderBy(se => se.Event.Timestamp)
                .Select(se => se.Event));
        }

        _logger.LogDebug("Retrieved {EventCount} events for aggregate {AggregateId}", 
            results.Count, aggregateId);

        return Task.FromResult<IEnumerable<IInvalidationEvent>>(results);
    }

    public Task<IEnumerable<IInvalidationEvent>> GetEventStreamAsync(string streamName, long fromVersion = 0, int maxEvents = 1000, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(streamName)) 
            throw new ArgumentException("Stream name cannot be null or empty", nameof(streamName));

        var results = new List<IInvalidationEvent>();

        if (_streamEvents.TryGetValue(streamName, out var streamEvents))
        {
            results.AddRange(streamEvents
                .Where(se => se.GlobalVersion >= fromVersion)
                .OrderBy(se => se.GlobalVersion)
                .Take(maxEvents)
                .Select(se => se.Event));
        }

        _logger.LogDebug("Retrieved {EventCount} events from stream {StreamName} starting from version {FromVersion}", 
            results.Count, streamName, fromVersion);

        return Task.FromResult<IEnumerable<IInvalidationEvent>>(results);
    }

    public Task<IEnumerable<TEvent>> GetEventsByTypeAsync<TEvent>(DateTime? fromTimestamp = null, int maxEvents = 1000, CancellationToken cancellationToken = default) 
        where TEvent : IInvalidationEvent
    {
        var eventType = typeof(TEvent);
        var results = new List<TEvent>();

        var allEvents = _events.AsEnumerable();
        
        if (fromTimestamp.HasValue)
        {
            allEvents = allEvents.Where(se => se.Event.Timestamp >= fromTimestamp.Value);
        }

        results.AddRange(allEvents
            .Where(se => eventType.IsAssignableFrom(se.Event.GetType()))
            .OrderBy(se => se.Event.Timestamp)
            .Take(maxEvents)
            .Select(se => (TEvent)se.Event));

        _logger.LogDebug("Retrieved {EventCount} events of type {EventType}", 
            results.Count, eventType.Name);

        return Task.FromResult<IEnumerable<TEvent>>(results);
    }

    /// <summary>
    /// 스트림에 이벤트 저장
    /// Save event to stream
    /// </summary>
    public Task SaveEventToStreamAsync(string streamName, IInvalidationEvent eventData, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(streamName)) 
            throw new ArgumentException("Stream name cannot be null or empty", nameof(streamName));
        if (eventData == null) throw new ArgumentNullException(nameof(eventData));

        var storedEvent = CreateStoredEvent(eventData);

        lock (_lock)
        {
            _events.Enqueue(storedEvent);
            
            _streamEvents.AddOrUpdate(streamName,
                new List<StoredEvent> { storedEvent },
                (_, existing) =>
                {
                    existing.Add(storedEvent);
                    return existing;
                });
        }

        _logger.LogDebug("Event {EventId} saved to stream {StreamName}", eventData.EventId, streamName);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 저장된 이벤트 개수 조회
    /// Get stored event count
    /// </summary>
    public Task<long> GetEventCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((long)_events.Count);
    }

    /// <summary>
    /// 특정 기간의 이벤트 개수 조회
    /// Get event count for specific time period
    /// </summary>
    public Task<long> GetEventCountAsync(DateTime fromTimestamp, DateTime toTimestamp, CancellationToken cancellationToken = default)
    {
        var count = _events.Count(se => se.Event.Timestamp >= fromTimestamp && se.Event.Timestamp <= toTimestamp);
        return Task.FromResult((long)count);
    }

    /// <summary>
    /// 오래된 이벤트 정리
    /// Clean up old events
    /// </summary>
    public Task CleanupEventsAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var eventsToRemove = new List<StoredEvent>();
            
            // Note: ConcurrentQueue doesn't support efficient removal, 
            // so in a real implementation you'd use a different data structure
            foreach (var storedEvent in _events)
            {
                if (storedEvent.Event.Timestamp < olderThan)
                {
                    eventsToRemove.Add(storedEvent);
                }
            }
            
            // Remove from aggregate events
            foreach (var kvp in _aggregateEvents.ToList())
            {
                var filteredEvents = kvp.Value.Where(se => se.Event.Timestamp >= olderThan).ToList();
                if (filteredEvents.Any())
                {
                    _aggregateEvents[kvp.Key] = filteredEvents;
                }
                else
                {
                    _aggregateEvents.TryRemove(kvp.Key, out _);
                }
            }
            
            // Remove from stream events
            foreach (var kvp in _streamEvents.ToList())
            {
                var filteredEvents = kvp.Value.Where(se => se.Event.Timestamp >= olderThan).ToList();
                if (filteredEvents.Any())
                {
                    _streamEvents[kvp.Key] = filteredEvents;
                }
                else
                {
                    _streamEvents.TryRemove(kvp.Key, out _);
                }
            }
        }

        _logger.LogInformation("Cleaned up events older than {OlderThan}", olderThan);

        return Task.CompletedTask;
    }

    private StoredEvent CreateStoredEvent(IInvalidationEvent eventData)
    {
        return new StoredEvent
        {
            Event = eventData,
            GlobalVersion = Interlocked.Increment(ref _globalVersion),
            StoredAt = DateTime.UtcNow
        };
    }

    private record StoredEvent
    {
        public required IInvalidationEvent Event { get; init; }
        public required long GlobalVersion { get; init; }
        public required DateTime StoredAt { get; init; }
    }
}