using Athena.Cache.Analytics.Abstractions;
using Athena.Cache.Analytics.Data;
using Athena.Cache.Analytics.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Athena.Cache.Analytics.Implementations;

/// <summary>
/// SQLite 기반 캐시 이벤트 수집기
/// </summary>
public class SqliteCacheEventCollector : ICacheEventCollector, IDisposable
{
    private readonly CacheAnalyticsDbContext _dbContext;
    private readonly ConcurrentQueue<CacheEvent> _eventQueue = new();
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    private readonly Timer _flushTimer;
    private readonly int _maxBufferSize;
    private readonly TimeSpan _flushInterval;

    public SqliteCacheEventCollector(
        CacheAnalyticsDbContext dbContext,
        int maxBufferSize = 1000,
        TimeSpan? flushInterval = null)
    {
        _dbContext = dbContext;
        _maxBufferSize = maxBufferSize;
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(30);

        // 주기적 플러시
        _flushTimer = new Timer(async _ => await FlushAsync(), null, _flushInterval, _flushInterval);

        // 데이터베이스 초기화
        _dbContext.Database.EnsureCreated();
    }

    public async Task RecordEventAsync(CacheEvent cacheEvent)
    {
        _eventQueue.Enqueue(cacheEvent);

        if (_eventQueue.Count >= _maxBufferSize)
        {
            await FlushAsync();
        }
    }

    public async Task RecordEventsAsync(IEnumerable<CacheEvent> events)
    {
        foreach (var evt in events)
        {
            await RecordEventAsync(evt);
        }
    }

    public async Task FlushAsync()
    {
        await _flushSemaphore.WaitAsync();
        try
        {
            var eventsToFlush = new List<CacheEvent>();
            while (_eventQueue.TryDequeue(out var evt))
            {
                eventsToFlush.Add(evt);
            }

            if (eventsToFlush.Count > 0)
            {
                var entities = eventsToFlush.Select(evt => new CacheEventEntity
                {
                    Id = evt.Id,
                    Timestamp = evt.Timestamp,
                    EventType = (int)evt.EventType,
                    CacheKey = evt.CacheKey,
                    Namespace = evt.Namespace,
                    TableName = evt.TableName,
                    EndpointPath = evt.EndpointPath,
                    HttpMethod = evt.HttpMethod,
                    ResponseSize = evt.ResponseSize,
                    ProcessingTimeMs = evt.ProcessingTimeMs,
                    UserId = evt.UserId,
                    SessionId = evt.SessionId,
                    MetadataJson = evt.Metadata.Count > 0 ? JsonSerializer.Serialize(evt.Metadata) : null
                }).ToList();

                _dbContext.CacheEvents.AddRange(entities);
                await _dbContext.SaveChangesAsync();
            }
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }

    public void Dispose()
    {
        FlushAsync().Wait();
        _flushTimer?.Dispose();
        _flushSemaphore?.Dispose();
        _dbContext?.Dispose();
    }
}