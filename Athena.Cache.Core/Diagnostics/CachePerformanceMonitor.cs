using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Athena.Cache.Core.Diagnostics;

/// <summary>
/// 캐시 성능 모니터링 및 메트릭 수집
/// </summary>
public class CachePerformanceMonitor : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _cacheHitCounter;
    private readonly Counter<long> _cacheMissCounter;
    private readonly Histogram<double> _cacheOperationDuration;
    private readonly ObservableGauge<long> _memoryUsage;
    
    public CachePerformanceMonitor()
    {
        _meter = new Meter("Athena.Cache.Core", "1.0.0");
        
        // 캐시 히트/미스 카운터
        _cacheHitCounter = _meter.CreateCounter<long>(
            name: "athena_cache_hits_total",
            description: "Total number of cache hits");
            
        _cacheMissCounter = _meter.CreateCounter<long>(
            name: "athena_cache_misses_total", 
            description: "Total number of cache misses");
            
        // 캐시 작업 소요 시간
        _cacheOperationDuration = _meter.CreateHistogram<double>(
            name: "athena_cache_operation_duration_seconds",
            description: "Duration of cache operations in seconds");
            
        // 메모리 사용량 추적
        _memoryUsage = _meter.CreateObservableGauge<long>(
            name: "athena_cache_memory_usage_bytes",
            description: "Current memory usage by cache in bytes",
            observeValue: () => GC.GetTotalMemory(false));
    }
    
    /// <summary>
    /// 캐시 히트 기록
    /// </summary>
    public void RecordCacheHit(string cacheType = "default")
    {
        _cacheHitCounter.Add(1, new KeyValuePair<string, object?>("cache_type", cacheType));
    }
    
    /// <summary>
    /// 캐시 미스 기록
    /// </summary>
    public void RecordCacheMiss(string cacheType = "default")
    {
        _cacheMissCounter.Add(1, new KeyValuePair<string, object?>("cache_type", cacheType));
    }
    
    /// <summary>
    /// 캐시 작업 소요 시간 기록
    /// </summary>
    public void RecordOperationDuration(TimeSpan duration, string operation, string cacheType = "default")
    {
        _cacheOperationDuration.Record(
            duration.TotalSeconds,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("cache_type", cacheType));
    }
    
    /// <summary>
    /// 성능 측정을 위한 Stopwatch 래퍼
    /// </summary>
    public PerformanceMeasurement StartMeasurement(string operation, string cacheType = "default")
    {
        return new PerformanceMeasurement(this, operation, cacheType);
    }
    
    public void Dispose()
    {
        _meter?.Dispose();
    }
}

/// <summary>
/// 성능 측정 헬퍼 클래스 (using 패턴 지원)
/// </summary>
public class PerformanceMeasurement : IDisposable
{
    private readonly CachePerformanceMonitor _monitor;
    private readonly string _operation;
    private readonly string _cacheType;
    private readonly Stopwatch _stopwatch;
    
    internal PerformanceMeasurement(CachePerformanceMonitor monitor, string operation, string cacheType)
    {
        _monitor = monitor;
        _operation = operation;
        _cacheType = cacheType;
        _stopwatch = Stopwatch.StartNew();
    }
    
    public void Dispose()
    {
        _stopwatch.Stop();
        _monitor.RecordOperationDuration(_stopwatch.Elapsed, _operation, _cacheType);
    }
}