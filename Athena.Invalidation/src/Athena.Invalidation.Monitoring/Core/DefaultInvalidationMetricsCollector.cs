using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Monitoring.Abstractions;
using System.Diagnostics.Metrics;

namespace Athena.Invalidation.Monitoring.Core;

/// <summary>
/// 기본 무효화 메트릭 수집기 - OpenTelemetry 기반
/// </summary>
public class DefaultInvalidationMetricsCollector : IInvalidationMetricsCollector, IDisposable
{
    private readonly ILogger<DefaultInvalidationMetricsCollector> _logger;
    private readonly MonitoringOptions _options;
    private readonly Meter _meter;
    private readonly DateTimeOffset _startTime;
    
    // OpenTelemetry Instruments
    private readonly Counter<long> _invalidationCounter;
    private readonly Counter<long> _invalidationErrorCounter;
    private readonly Histogram<double> _invalidationDuration;
    private readonly Counter<long> _cacheAccessCounter;
    private readonly Counter<long> _cacheHitCounter;
    private readonly Counter<long> _distributedEventCounter;
    private readonly Histogram<double> _distributedEventDuration;
    
    // 내부 카운터 (스냅샷용)
    private readonly ConcurrentDictionary<InvalidationType, long> _invalidationsByType = new();
    private readonly ConcurrentDictionary<string, long> _distributedEventsByType = new();
    private readonly ConcurrentDictionary<string, long> _distributedEventsByNode = new();
    private readonly ConcurrentDictionary<string, (long hits, long total)> _cacheStats = new();
    
    private long _totalInvalidations = 0;
    private long _successfulInvalidations = 0;
    private long _failedInvalidations = 0;
    private long _totalDistributedEvents = 0;
    private long _successfulDistributedEvents = 0;
    private long _failedDistributedEvents = 0;
    
    private readonly List<TimeSpan> _recentInvalidationTimes = new();
    private readonly List<TimeSpan> _recentDistributedEventTimes = new();
    private readonly object _timingLock = new object();
    
    private volatile bool _isStarted = false;
    private volatile bool _disposed = false;

    public DefaultInvalidationMetricsCollector(
        ILogger<DefaultInvalidationMetricsCollector> logger,
        IOptions<MonitoringOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new MonitoringOptions();
        _startTime = DateTimeOffset.UtcNow;
        
        _meter = new Meter(_options.MeterName, _options.MeterVersion);
        
        // OpenTelemetry 계측기 초기화
        _invalidationCounter = _meter.CreateCounter<long>(
            "athena_invalidation_total",
            "requests",
            "Total number of cache invalidation operations");
            
        _invalidationErrorCounter = _meter.CreateCounter<long>(
            "athena_invalidation_errors_total",
            "errors",
            "Total number of failed cache invalidation operations");
            
        _invalidationDuration = _meter.CreateHistogram<double>(
            "athena_invalidation_duration_seconds",
            "seconds",
            "Duration of cache invalidation operations");
            
        _cacheAccessCounter = _meter.CreateCounter<long>(
            "athena_cache_accesses_total",
            "requests",
            "Total number of cache access operations");
            
        _cacheHitCounter = _meter.CreateCounter<long>(
            "athena_cache_hits_total",
            "hits",
            "Total number of cache hits");
            
        _distributedEventCounter = _meter.CreateCounter<long>(
            "athena_distributed_events_total",
            "events",
            "Total number of distributed invalidation events");
            
        _distributedEventDuration = _meter.CreateHistogram<double>(
            "athena_distributed_event_duration_seconds",
            "seconds",
            "Duration of distributed event processing");
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DefaultInvalidationMetricsCollector));
        if (_isStarted) return Task.CompletedTask;

        _isStarted = true;
        _logger.LogInformation("Invalidation metrics collector started with meter '{MeterName}:{MeterVersion}'", 
            _options.MeterName, _options.MeterVersion);
            
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !_isStarted) return Task.CompletedTask;

        _isStarted = false;
        _logger.LogInformation("Invalidation metrics collector stopped");
        
        return Task.CompletedTask;
    }

    public void RecordInvalidationEvent(InvalidationType type, string target, TimeSpan duration, bool success = true)
    {
        if (_disposed || !_isStarted) return;

        try
        {
            var tags = new TagList
            {
                ["type"] = type.ToString().ToLowerInvariant(),
                ["target"] = target,
                ["success"] = success.ToString().ToLowerInvariant()
            };

            _invalidationCounter.Add(1, tags);
            _invalidationDuration.Record(duration.TotalSeconds, tags);

            if (!success)
            {
                _invalidationErrorCounter.Add(1, tags);
            }

            // 내부 통계 업데이트
            Interlocked.Increment(ref _totalInvalidations);
            if (success)
            {
                Interlocked.Increment(ref _successfulInvalidations);
            }
            else
            {
                Interlocked.Increment(ref _failedInvalidations);
            }

            _invalidationsByType.AddOrUpdate(type, 1, (key, value) => value + 1);

            lock (_timingLock)
            {
                _recentInvalidationTimes.Add(duration);
                if (_recentInvalidationTimes.Count > _options.RecentEventsSampleSize)
                {
                    _recentInvalidationTimes.RemoveAt(0);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record invalidation event metric");
        }
    }

    public void RecordBatchInvalidationEvent(int count, TimeSpan duration, bool success = true)
    {
        if (_disposed || !_isStarted) return;

        try
        {
            var tags = new TagList
            {
                ["type"] = "batch",
                ["count"] = count.ToString(),
                ["success"] = success.ToString().ToLowerInvariant()
            };

            _invalidationCounter.Add(count, tags);
            _invalidationDuration.Record(duration.TotalSeconds, tags);

            if (!success)
            {
                _invalidationErrorCounter.Add(count, tags);
            }

            // 내부 통계 업데이트
            Interlocked.Add(ref _totalInvalidations, count);
            if (success)
            {
                Interlocked.Add(ref _successfulInvalidations, count);
            }
            else
            {
                Interlocked.Add(ref _failedInvalidations, count);
            }

            _invalidationsByType.AddOrUpdate(InvalidationType.Batch, count, (key, value) => value + count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record batch invalidation event metric");
        }
    }

    public void RecordCacheAccess(string cacheName, bool hit)
    {
        if (_disposed || !_isStarted) return;

        try
        {
            var tags = new TagList
            {
                ["cache_name"] = cacheName,
                ["result"] = hit ? "hit" : "miss"
            };

            _cacheAccessCounter.Add(1, tags);
            if (hit)
            {
                _cacheHitCounter.Add(1, tags);
            }

            // 내부 통계 업데이트
            _cacheStats.AddOrUpdate(cacheName, 
                hit ? (1L, 1L) : (0L, 1L),
                (key, existing) => hit ? (existing.hits + 1, existing.total + 1) : (existing.hits, existing.total + 1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record cache access metric");
        }
    }

    public void RecordDistributedEvent(string eventType, string sourceNode, TimeSpan processingTime, bool success = true)
    {
        if (_disposed || !_isStarted) return;

        try
        {
            var tags = new TagList
            {
                ["event_type"] = eventType,
                ["source_node"] = sourceNode,
                ["success"] = success.ToString().ToLowerInvariant()
            };

            _distributedEventCounter.Add(1, tags);
            _distributedEventDuration.Record(processingTime.TotalSeconds, tags);

            // 내부 통계 업데이트
            Interlocked.Increment(ref _totalDistributedEvents);
            if (success)
            {
                Interlocked.Increment(ref _successfulDistributedEvents);
            }
            else
            {
                Interlocked.Increment(ref _failedDistributedEvents);
            }

            _distributedEventsByType.AddOrUpdate(eventType, 1, (key, value) => value + 1);
            _distributedEventsByNode.AddOrUpdate(sourceNode, 1, (key, value) => value + 1);

            lock (_timingLock)
            {
                _recentDistributedEventTimes.Add(processingTime);
                if (_recentDistributedEventTimes.Count > _options.RecentEventsSampleSize)
                {
                    _recentDistributedEventTimes.RemoveAt(0);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record distributed event metric");
        }
    }

    public Task<MetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DefaultInvalidationMetricsCollector));

        var totalInvalidations = Interlocked.Read(ref _totalInvalidations);
        var successfulInvalidations = Interlocked.Read(ref _successfulInvalidations);
        var failedInvalidations = Interlocked.Read(ref _failedInvalidations);
        var totalDistributedEvents = Interlocked.Read(ref _totalDistributedEvents);
        var successfulDistributedEvents = Interlocked.Read(ref _successfulDistributedEvents);
        var failedDistributedEvents = Interlocked.Read(ref _failedDistributedEvents);

        var cacheHits = _cacheStats.Values.Sum(s => s.hits);
        var totalCacheAccesses = _cacheStats.Values.Sum(s => s.total);

        TimeSpan avgInvalidationTime, avgDistributedEventTime;
        lock (_timingLock)
        {
            avgInvalidationTime = _recentInvalidationTimes.Count > 0 
                ? TimeSpan.FromMilliseconds(_recentInvalidationTimes.Average(t => t.TotalMilliseconds))
                : TimeSpan.Zero;
                
            avgDistributedEventTime = _recentDistributedEventTimes.Count > 0
                ? TimeSpan.FromMilliseconds(_recentDistributedEventTimes.Average(t => t.TotalMilliseconds))
                : TimeSpan.Zero;
        }

        var snapshot = new MetricsSnapshot
        {
            Uptime = DateTimeOffset.UtcNow - _startTime,
            TotalInvalidations = totalInvalidations,
            SuccessfulInvalidations = successfulInvalidations,
            FailedInvalidations = failedInvalidations,
            InvalidationSuccessRate = totalInvalidations > 0 ? (double)successfulInvalidations / totalInvalidations : 1.0,
            AverageInvalidationTime = avgInvalidationTime,
            InvalidationsByType = new Dictionary<InvalidationType, long>(_invalidationsByType),
            TotalCacheAccesses = totalCacheAccesses,
            CacheHits = cacheHits,
            CacheMisses = totalCacheAccesses - cacheHits,
            CacheHitRatio = totalCacheAccesses > 0 ? (double)cacheHits / totalCacheAccesses : 0.0,
            TotalDistributedEvents = totalDistributedEvents,
            SuccessfulDistributedEvents = successfulDistributedEvents,
            FailedDistributedEvents = failedDistributedEvents,
            AverageDistributedEventProcessingTime = avgDistributedEventTime,
            DistributedEventsByType = new Dictionary<string, long>(_distributedEventsByType),
            DistributedEventsByNode = new Dictionary<string, long>(_distributedEventsByNode),
            SystemMetrics = new Dictionary<string, object>
            {
                ["is_started"] = _isStarted,
                ["meter_name"] = _options.MeterName,
                ["meter_version"] = _options.MeterVersion,
                ["recent_events_sample_size"] = _options.RecentEventsSampleSize
            }
        };

        return Task.FromResult(snapshot);
    }

    public Task ResetMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DefaultInvalidationMetricsCollector));

        Interlocked.Exchange(ref _totalInvalidations, 0);
        Interlocked.Exchange(ref _successfulInvalidations, 0);
        Interlocked.Exchange(ref _failedInvalidations, 0);
        Interlocked.Exchange(ref _totalDistributedEvents, 0);
        Interlocked.Exchange(ref _successfulDistributedEvents, 0);
        Interlocked.Exchange(ref _failedDistributedEvents, 0);

        _invalidationsByType.Clear();
        _distributedEventsByType.Clear();
        _distributedEventsByNode.Clear();
        _cacheStats.Clear();

        lock (_timingLock)
        {
            _recentInvalidationTimes.Clear();
            _recentDistributedEventTimes.Clear();
        }

        _logger.LogInformation("Metrics have been reset");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _meter?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing metrics collector");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// 모니터링 구성 옵션
/// </summary>
public class MonitoringOptions
{
    public string MeterName { get; set; } = "Athena.Invalidation";
    public string MeterVersion { get; set; } = "1.0.0";
    public int RecentEventsSampleSize { get; set; } = 1000;
    public bool EnableDetailedMetrics { get; set; } = true;
    public TimeSpan MetricsRetentionPeriod { get; set; } = TimeSpan.FromHours(24);
}