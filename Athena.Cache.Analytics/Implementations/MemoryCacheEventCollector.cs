using Athena.Cache.Analytics.Abstractions;
using Athena.Cache.Analytics.Models;
using System.Collections.Concurrent;

namespace Athena.Cache.Analytics;

/// <summary>
/// 메모리 기반 캐시 이벤트 수집기
/// </summary>
public class MemoryCacheEventCollector : ICacheEventCollector
{
    private readonly ConcurrentQueue<CacheEvent> _events = new();
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    private readonly Timer _flushTimer;
    private readonly int _maxBufferSize;
    private readonly TimeSpan _flushInterval;

    public MemoryCacheEventCollector(int maxBufferSize = 1000, TimeSpan? flushInterval = null)
    {
        _maxBufferSize = maxBufferSize;
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(30);

        // 주기적으로 플러시
        _flushTimer = new Timer(async _ => await FlushAsync(), null, _flushInterval, _flushInterval);
    }

    public async Task RecordEventAsync(CacheEvent cacheEvent)
    {
        _events.Enqueue(cacheEvent);

        // 버퍼가 가득 차면 자동 플러시
        if (_events.Count >= _maxBufferSize)
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
            while (_events.TryDequeue(out var evt))
            {
                eventsToFlush.Add(evt);
            }

            if (eventsToFlush.Count > 0)
            {
                // 실제로는 여기서 데이터베이스나 파일에 저장
                // 지금은 메모리에 보관 (개발용)
                await SaveEventsToStorage(eventsToFlush);
            }
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }

    private static readonly ConcurrentBag<CacheEvent> _storedEvents = new();

    private async Task SaveEventsToStorage(List<CacheEvent> events)
    {
        await Task.Run(() =>
        {
            foreach (var evt in events)
            {
                _storedEvents.Add(evt);
            }
        });
    }

    public static IEnumerable<CacheEvent> GetStoredEvents() => _storedEvents.ToArray();

    public void Dispose()
    {
        _flushTimer?.Dispose();
        _flushSemaphore?.Dispose();
    }
}