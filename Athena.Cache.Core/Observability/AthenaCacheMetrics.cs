using System.Diagnostics.Metrics;
using System.Diagnostics;

namespace Athena.Cache.Core.Observability;

/// <summary>
/// Athena Cache OpenTelemetry 메트릭 정의
/// Prometheus, Grafana, Azure Monitor 등과 호환
/// </summary>
public class AthenaCacheMetrics : IDisposable
{
    private readonly Meter _meter;
    
    // Counters - 누적 값
    private readonly Counter<long> _cacheHitsTotal;
    private readonly Counter<long> _cacheMissesTotal;
    private readonly Counter<long> _cacheInvalidationsTotal;
    private readonly Counter<long> _cacheErrorsTotal;
    
    // Histograms - 분포 값
    private readonly Histogram<double> _cacheOperationDuration;
    private readonly Histogram<long> _cacheKeySizeBytes;
    private readonly Histogram<long> _cacheValueSizeBytes;
    
    // Gauges - 현재 값
    private readonly ObservableGauge<long> _cacheMemoryUsageBytes;
    private readonly ObservableGauge<long> _cacheItemCount;
    private readonly ObservableGauge<double> _cacheHitRatio;
    private readonly ObservableGauge<long> _hotKeysCount;
    
    // UpDownCounters - 증감 값
    private readonly UpDownCounter<long> _activeCacheConnections;
    private readonly UpDownCounter<long> _distributedInvalidationMessages;

    // Observable Gauge 콜백들
    private Func<long>? _memoryUsageGetter;
    private Func<long>? _itemCountGetter; 
    private Func<double>? _hitRatioGetter;
    private Func<long>? _hotKeysCountGetter;

    public AthenaCacheMetrics()
    {
        _meter = new Meter("Athena.Cache", "1.0.0");
        
        // === Counters ===
        _cacheHitsTotal = _meter.CreateCounter<long>(
            name: "athena_cache_hits_total",
            unit: "count",
            description: "Total number of cache hits");
            
        _cacheMissesTotal = _meter.CreateCounter<long>(
            name: "athena_cache_misses_total",
            unit: "count", 
            description: "Total number of cache misses");
            
        _cacheInvalidationsTotal = _meter.CreateCounter<long>(
            name: "athena_cache_invalidations_total",
            unit: "count",
            description: "Total number of cache invalidations");
            
        _cacheErrorsTotal = _meter.CreateCounter<long>(
            name: "athena_cache_errors_total",
            unit: "count",
            description: "Total number of cache errors");
        
        // === Histograms ===
        _cacheOperationDuration = _meter.CreateHistogram<double>(
            name: "athena_cache_operation_duration_seconds",
            unit: "s",
            description: "Duration of cache operations");
            
        _cacheKeySizeBytes = _meter.CreateHistogram<long>(
            name: "athena_cache_key_size_bytes",
            unit: "bytes",
            description: "Size of cache keys in bytes");
            
        _cacheValueSizeBytes = _meter.CreateHistogram<long>(
            name: "athena_cache_value_size_bytes", 
            unit: "bytes",
            description: "Size of cache values in bytes");
        
        // === Observable Gauges ===
        _cacheMemoryUsageBytes = _meter.CreateObservableGauge<long>(
            name: "athena_cache_memory_usage_bytes",
            description: "Current memory usage by cache",
            observeValue: () => _memoryUsageGetter?.Invoke() ?? 0);
            
        _cacheItemCount = _meter.CreateObservableGauge<long>(
            name: "athena_cache_items_count",
            description: "Current number of items in cache",
            observeValue: () => _itemCountGetter?.Invoke() ?? 0);
            
        _cacheHitRatio = _meter.CreateObservableGauge<double>(
            name: "athena_cache_hit_ratio",
            description: "Current cache hit ratio (0.0-1.0)",
            observeValue: () => _hitRatioGetter?.Invoke() ?? 0.0);
            
        _hotKeysCount = _meter.CreateObservableGauge<long>(
            name: "athena_cache_hot_keys_count",
            description: "Current number of detected hot keys",
            observeValue: () => _hotKeysCountGetter?.Invoke() ?? 0);
        
        // === UpDown Counters ===
        _activeCacheConnections = _meter.CreateUpDownCounter<long>(
            name: "athena_cache_active_connections",
            unit: "count",
            description: "Number of active cache connections");
            
        _distributedInvalidationMessages = _meter.CreateUpDownCounter<long>(
            name: "athena_cache_distributed_invalidation_messages",
            unit: "count",
            description: "Number of distributed invalidation messages in queue");
    }

    #region Counter Methods
    
    public void RecordCacheHit(string? cacheType = null, string? keyPattern = null)
    {
        var tags = CreateTags(cacheType, keyPattern);
        _cacheHitsTotal.Add(1, tags);
    }
    
    public void RecordCacheMiss(string? cacheType = null, string? keyPattern = null)
    {
        var tags = CreateTags(cacheType, keyPattern);
        _cacheMissesTotal.Add(1, tags);
    }
    
    public void RecordCacheInvalidation(string tableName, string invalidationType = "table")
    {
        _cacheInvalidationsTotal.Add(1, 
            new("table_name", tableName),
            new("invalidation_type", invalidationType));
    }
    
    public void RecordCacheError(string operation, string errorType, string? errorMessage = null)
    {
        _cacheErrorsTotal.Add(1,
            new("operation", operation),
            new("error_type", errorType),
            new("error_message", errorMessage ?? "unknown"));
    }
    
    #endregion
    
    #region Histogram Methods
    
    public void RecordOperationDuration(TimeSpan duration, string operation, string? cacheType = null)
    {
        _cacheOperationDuration.Record(duration.TotalSeconds,
            new("operation", operation),
            new("cache_type", cacheType ?? "default"));
    }
    
    public void RecordKeySize(long sizeBytes, string? keyPattern = null)
    {
        var tags = new KeyValuePair<string, object?>[] { new("key_pattern", keyPattern ?? "unknown") };
        _cacheKeySizeBytes.Record(sizeBytes, tags);
    }
    
    public void RecordValueSize(long sizeBytes, string? valueType = null) 
    {
        var tags = new KeyValuePair<string, object?>[] { new("value_type", valueType ?? "unknown") };
        _cacheValueSizeBytes.Record(sizeBytes, tags);
    }
    
    #endregion
    
    #region Observable Methods - These are set externally via callbacks
    
    public void SetMemoryUsageCallback(Func<long> memoryUsageGetter)
    {
        _memoryUsageGetter = memoryUsageGetter;
    }
    
    public void SetItemCountCallback(Func<long> itemCountGetter)
    {
        _itemCountGetter = itemCountGetter;
    }
    
    public void SetHitRatioCallback(Func<double> hitRatioGetter)
    {
        _hitRatioGetter = hitRatioGetter;
    }
    
    public void SetHotKeysCountCallback(Func<long> hotKeysCountGetter)
    {
        _hotKeysCountGetter = hotKeysCountGetter;
    }
    
    #endregion
    
    #region UpDown Counter Methods
    
    public void IncrementActiveConnections(int delta = 1)
    {
        _activeCacheConnections.Add(delta);
    }
    
    public void DecrementActiveConnections(int delta = 1)
    {
        _activeCacheConnections.Add(-delta);
    }
    
    public void IncrementDistributedMessages(int delta = 1)
    {
        _distributedInvalidationMessages.Add(delta);
    }
    
    public void DecrementDistributedMessages(int delta = 1)
    {
        _distributedInvalidationMessages.Add(-delta);
    }
    
    #endregion
    
    #region Activity/Tracing Support
    
    private static readonly ActivitySource ActivitySource = new("Athena.Cache");
    
    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind);
    }
    
    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.SetTag("exception.type", exception.GetType().Name);
        activity?.SetTag("exception.message", exception.Message);
        activity?.SetTag("exception.stacktrace", exception.StackTrace);
    }
    
    #endregion
    
    #region Helper Methods
    
    private static KeyValuePair<string, object?>[] CreateTags(string? cacheType = null, string? keyPattern = null)
    {
        var tags = new List<KeyValuePair<string, object?>>();
        
        if (!string.IsNullOrEmpty(cacheType))
            tags.Add(new("cache_type", cacheType));
            
        if (!string.IsNullOrEmpty(keyPattern))
            tags.Add(new("key_pattern", keyPattern));
            
        return tags.ToArray();
    }
    
    #endregion
    
    public void Dispose()
    {
        _meter?.Dispose();
        ActivitySource?.Dispose();
    }
}

/// <summary>
/// 캐시 성능 통계 스냅샷
/// </summary>
public class CachePerformanceSnapshot
{
    public long TotalHits { get; init; }
    public long TotalMisses { get; init; }
    public double HitRatio { get; init; }
    public long TotalInvalidations { get; init; }
    public long TotalErrors { get; init; }
    public long MemoryUsageBytes { get; init; }
    public long ItemCount { get; init; }
    public long HotKeysCount { get; init; }
    public TimeSpan AverageOperationDuration { get; init; }
    public DateTime SnapshotTime { get; init; } = DateTime.UtcNow;
}