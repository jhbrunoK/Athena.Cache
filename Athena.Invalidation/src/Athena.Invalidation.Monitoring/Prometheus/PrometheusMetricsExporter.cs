using Athena.Invalidation.Monitoring.Abstractions;
using Prometheus;

namespace Athena.Invalidation.Monitoring.Prometheus;

/// <summary>
/// Prometheus 메트릭 익스포터
/// </summary>
public class PrometheusMetricsExporter : IDisposable
{
    private readonly IInvalidationMetricsCollector _metricsCollector;
    private readonly ILogger<PrometheusMetricsExporter> _logger;
    private readonly PrometheusOptions _options;
    
    // Prometheus Metrics
    private readonly Counter _invalidationTotal;
    private readonly Counter _invalidationErrors;
    private readonly Histogram _invalidationDuration;
    private readonly Gauge _invalidationSuccessRate;
    private readonly Counter _cacheAccesses;
    private readonly Counter _cacheHits;
    private readonly Gauge _cacheHitRatio;
    private readonly Counter _distributedEvents;
    private readonly Histogram _distributedEventDuration;
    private readonly Gauge _systemUptime;
    
    private readonly Timer _metricsUpdateTimer;
    private volatile bool _disposed = false;

    public PrometheusMetricsExporter(
        IInvalidationMetricsCollector metricsCollector,
        ILogger<PrometheusMetricsExporter> logger,
        IOptions<PrometheusOptions> options)
    {
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new PrometheusOptions();
        
        // Prometheus 메트릭 정의
        _invalidationTotal = Metrics.CreateCounter(
            "athena_invalidation_operations_total",
            "Total number of cache invalidation operations",
            new[] { "type", "success" });
            
        _invalidationErrors = Metrics.CreateCounter(
            "athena_invalidation_errors_total",
            "Total number of failed cache invalidation operations",
            new[] { "type" });
            
        _invalidationDuration = Metrics.CreateHistogram(
            "athena_invalidation_duration_seconds",
            "Duration of cache invalidation operations",
            new[] { "type" });
            
        _invalidationSuccessRate = Metrics.CreateGauge(
            "athena_invalidation_success_rate",
            "Success rate of cache invalidation operations");
            
        _cacheAccesses = Metrics.CreateCounter(
            "athena_cache_accesses_total",
            "Total number of cache access operations",
            new[] { "cache_name", "result" });
            
        _cacheHits = Metrics.CreateCounter(
            "athena_cache_hits_total",
            "Total number of cache hits",
            new[] { "cache_name" });
            
        _cacheHitRatio = Metrics.CreateGauge(
            "athena_cache_hit_ratio",
            "Cache hit ratio");
            
        _distributedEvents = Metrics.CreateCounter(
            "athena_distributed_events_total",
            "Total number of distributed invalidation events",
            new[] { "event_type", "source_node", "success" });
            
        _distributedEventDuration = Metrics.CreateHistogram(
            "athena_distributed_event_duration_seconds",
            "Duration of distributed event processing",
            new[] { "event_type" });
            
        _systemUptime = Metrics.CreateGauge(
            "athena_system_uptime_seconds",
            "System uptime in seconds");

        // 주기적 메트릭 업데이트 타이머
        _metricsUpdateTimer = new Timer(UpdatePrometheusMetrics, null, 
            _options.UpdateInterval, _options.UpdateInterval);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PrometheusMetricsExporter));

        if (_options.EnableMetricServer)
        {
            var metricServer = new MetricServer(hostname: _options.Hostname, port: _options.Port);
            metricServer.Start();
            
            _logger.LogInformation("Prometheus metrics server started on {Hostname}:{Port}", 
                _options.Hostname, _options.Port);
        }
        
        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        
        _metricsUpdateTimer?.Dispose();
        
        if (_options.EnableMetricServer)
        {
            // MetricServer 정리는 별도로 관리 필요
            _logger.LogInformation("Prometheus metrics server stopped");
        }
        
        await Task.CompletedTask;
    }

    private async void UpdatePrometheusMetrics(object? state)
    {
        if (_disposed) return;

        try
        {
            var metrics = await _metricsCollector.GetMetricsAsync();
            
            // 기본 메트릭 업데이트
            _invalidationSuccessRate.Set(metrics.InvalidationSuccessRate);
            _cacheHitRatio.Set(metrics.CacheHitRatio);
            _systemUptime.Set(metrics.Uptime.TotalSeconds);
            
            // 타입별 무효화 카운터 업데이트
            foreach (var kvp in metrics.InvalidationsByType)
            {
                var typeName = kvp.Key.ToString().ToLowerInvariant();
                // 이전 값과 비교해서 증분만 추가하는 로직 필요
                // 현재는 단순 구현
                _logger.LogTrace("Invalidation type {Type}: {Count}", typeName, kvp.Value);
            }
            
            // 분산 이벤트 메트릭 업데이트
            foreach (var kvp in metrics.DistributedEventsByType)
            {
                _logger.LogTrace("Distributed event type {Type}: {Count}", kvp.Key, kvp.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Prometheus metrics");
        }
    }

    public void RecordInvalidationOperation(string type, bool success, double durationSeconds)
    {
        if (_disposed) return;
        
        _invalidationTotal.WithLabels(type, success.ToString().ToLowerInvariant()).Inc();
        _invalidationDuration.WithLabels(type).Observe(durationSeconds);
        
        if (!success)
        {
            _invalidationErrors.WithLabels(type).Inc();
        }
    }

    public void RecordCacheAccess(string cacheName, bool hit)
    {
        if (_disposed) return;
        
        _cacheAccesses.WithLabels(cacheName, hit ? "hit" : "miss").Inc();
        
        if (hit)
        {
            _cacheHits.WithLabels(cacheName).Inc();
        }
    }

    public void RecordDistributedEvent(string eventType, string sourceNode, bool success, double durationSeconds)
    {
        if (_disposed) return;
        
        _distributedEvents.WithLabels(eventType, sourceNode, success.ToString().ToLowerInvariant()).Inc();
        _distributedEventDuration.WithLabels(eventType).Observe(durationSeconds);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _metricsUpdateTimer?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing Prometheus metrics exporter");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Prometheus 구성 옵션
/// </summary>
public class PrometheusOptions
{
    /// <summary>메트릭 서버 활성화 여부</summary>
    public bool EnableMetricServer { get; set; } = true;
    
    /// <summary>메트릭 서버 호스트명</summary>
    public string Hostname { get; set; } = "*";
    
    /// <summary>메트릭 서버 포트</summary>
    public int Port { get; set; } = 9090;
    
    /// <summary>메트릭 업데이트 간격</summary>
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(10);
    
    /// <summary>메트릭 라벨 최대 개수</summary>
    public int MaxLabels { get; set; } = 10;
    
    /// <summary>메트릭 히스토리 보존 기간</summary>
    public TimeSpan MetricRetention { get; set; } = TimeSpan.FromDays(7);
}