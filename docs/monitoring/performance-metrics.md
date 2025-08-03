---
layout: default
title: Performance Metrics
---

# Performance Metrics

Comprehensive performance monitoring and metrics collection for Athena.Cache. Track cache effectiveness, system performance, and identify optimization opportunities.

## Core Metrics Collection

Enable detailed metrics collection for production monitoring.

### Configuration

```csharp
// Program.cs
builder.Services.AddAthenaCacheComplete(options =>
{
    // Enable metrics collection
    options.Monitoring.EnableMetrics = true;
    options.Monitoring.MetricsCollectionInterval = TimeSpan.FromSeconds(30);
    options.Monitoring.RetainMetricsFor = TimeSpan.FromHours(24);
    
    // Detailed performance tracking
    options.Monitoring.TrackResponseTimes = true;
    options.Monitoring.TrackCacheKeyDistribution = true;
    options.Monitoring.TrackMemoryUsage = true;
    options.Monitoring.TrackErrorRates = true;
    
    // Custom metrics
    options.Monitoring.EnableCustomMetrics = true;
    options.Monitoring.MaxCustomMetrics = 1000;
});
```

### Metrics Collection Service

```csharp
public class CacheMetricsCollector : ICacheMetricsCollector
{
    private readonly ConcurrentDictionary<string, MetricValue> _metrics = new();
    private readonly Timer _collectionTimer;
    private readonly ILogger<CacheMetricsCollector> _logger;

    public CacheMetricsCollector(ILogger<CacheMetricsCollector> logger)
    {
        _logger = logger;
        _collectionTimer = new Timer(CollectMetrics, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    private void CollectMetrics(object state)
    {
        try
        {
            var currentMetrics = new CacheMetrics
            {
                Timestamp = DateTimeOffset.UtcNow,
                
                // Cache effectiveness
                HitRate = CalculateHitRate(),
                MissRate = CalculateMissRate(),
                HitCount = _metrics.GetValueOrDefault("hit_count", 0),
                MissCount = _metrics.GetValueOrDefault("miss_count", 0),
                
                // Performance metrics
                AverageResponseTime = CalculateAverageResponseTime(),
                P50ResponseTime = CalculatePercentileResponseTime(50),
                P95ResponseTime = CalculatePercentileResponseTime(95),
                P99ResponseTime = CalculatePercentileResponseTime(99),
                
                // Throughput metrics
                RequestsPerSecond = CalculateRequestsPerSecond(),
                CacheOperationsPerSecond = CalculateCacheOperationsPerSecond(),
                
                // Resource metrics
                MemoryUsage = GC.GetTotalMemory(false),
                CacheSize = GetCacheSize(),
                KeyCount = GetKeyCount(),
                
                // Error metrics
                ErrorRate = CalculateErrorRate(),
                TimeoutCount = _metrics.GetValueOrDefault("timeout_count", 0),
                ConnectionErrors = _metrics.GetValueOrDefault("connection_errors", 0)
            };

            // Store metrics for historical analysis
            StoreMetrics(currentMetrics);
            
            // Publish metrics to monitoring systems
            PublishMetrics(currentMetrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting cache metrics");
        }
    }

    public void RecordCacheHit(string key, TimeSpan responseTime)
    {
        IncrementMetric("hit_count");
        RecordResponseTime(responseTime);
        RecordKeyAccess(key);
    }

    public void RecordCacheMiss(string key, TimeSpan responseTime)
    {
        IncrementMetric("miss_count");
        RecordResponseTime(responseTime);
        RecordKeyAccess(key);
    }

    public void RecordError(string operation, Exception exception)
    {
        IncrementMetric("error_count");
        IncrementMetric($"error_{operation}");
        
        if (exception is TimeoutException)
        {
            IncrementMetric("timeout_count");
        }
        else if (exception is ConnectionException)
        {
            IncrementMetric("connection_errors");
        }
    }
}
```

## Real-time Performance Dashboard

Create comprehensive dashboards for real-time monitoring.

### Metrics API Controller

```csharp
[ApiController]
[Route("api/cache/metrics")]
public class CacheMetricsController : ControllerBase
{
    private readonly ICacheMetricsCollector _metricsCollector;
    private readonly ICacheStatistics _statistics;
    private readonly ILogger<CacheMetricsController> _logger;

    [HttpGet("current")]
    public async Task<ActionResult<CacheMetrics>> GetCurrentMetrics()
    {
        return Ok(await _metricsCollector.GetCurrentMetricsAsync());
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<CacheMetrics>>> GetMetricsHistory(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] int intervalMinutes = 5)
    {
        var start = startTime ?? DateTime.UtcNow.AddHours(-1);
        var end = endTime ?? DateTime.UtcNow;
        
        var metrics = await _metricsCollector.GetMetricsHistoryAsync(start, end, TimeSpan.FromMinutes(intervalMinutes));
        return Ok(metrics);
    }

    [HttpGet("performance")]
    public async Task<ActionResult<PerformanceMetrics>> GetPerformanceMetrics()
    {
        var stats = await _statistics.GetCurrentStatsAsync();
        
        return Ok(new PerformanceMetrics
        {
            // Response time metrics
            AverageResponseTime = stats.AverageResponseTime,
            MedianResponseTime = stats.MedianResponseTime,
            P95ResponseTime = stats.P95ResponseTime,
            P99ResponseTime = stats.P99ResponseTime,
            
            // Throughput metrics
            RequestsPerSecond = stats.RequestsPerSecond,
            CacheOperationsPerSecond = stats.CacheOperationsPerSecond,
            PeakRequestsPerSecond = stats.PeakRequestsPerSecond,
            
            // Cache effectiveness
            HitRate = stats.HitRate,
            MissRate = stats.MissRate,
            EvictionRate = stats.EvictionRate,
            
            // Resource utilization
            MemoryUsage = stats.MemoryUsage,
            CpuUsage = stats.CpuUsage,
            NetworkLatency = stats.NetworkLatency,
            
            // Error metrics
            ErrorRate = stats.ErrorRate,
            TimeoutRate = stats.TimeoutRate,
            ConnectionFailureRate = stats.ConnectionFailureRate
        });
    }

    [HttpGet("cache-keys/analysis")]
    public async Task<ActionResult<CacheKeyAnalysis>> GetCacheKeyAnalysis()
    {
        var analysis = await _statistics.AnalyzeCacheKeysAsync();
        
        return Ok(new CacheKeyAnalysis
        {
            TotalKeys = analysis.TotalKeys,
            MostAccessedKeys = analysis.MostAccessedKeys.Take(20),
            LeastAccessedKeys = analysis.LeastAccessedKeys.Take(20),
            LargestKeys = analysis.LargestKeys.Take(20),
            KeysByController = analysis.KeysByController,
            KeysByPattern = analysis.KeysByPattern,
            ExpirationDistribution = analysis.ExpirationDistribution
        });
    }

    [HttpGet("memory/analysis")]
    public async Task<ActionResult<MemoryAnalysis>> GetMemoryAnalysis()
    {
        return Ok(new MemoryAnalysis
        {
            TotalMemoryUsage = GC.GetTotalMemory(false),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            TotalPauseDuration = GC.GetTotalPauseDuration(),
            WorkingSet = Environment.WorkingSet,
            
            // Pool statistics
            StringPoolStats = GetStringPoolStats(),
            CollectionPoolStats = GetCollectionPoolStats(),
            
            // Cache-specific memory
            CacheMemoryUsage = await _statistics.GetCacheMemoryUsageAsync(),
            KeyMemoryDistribution = await _statistics.GetKeyMemoryDistributionAsync()
        });
    }

    [HttpGet("health")]
    public async Task<ActionResult<CacheHealthMetrics>> GetHealthMetrics()
    {
        var health = await _statistics.GetHealthMetricsAsync();
        
        return Ok(new CacheHealthMetrics
        {
            OverallHealth = health.OverallHealth,
            ComponentHealth = health.ComponentHealth,
            Alerts = health.ActiveAlerts,
            
            // Service-level indicators
            Availability = health.Availability,
            Reliability = health.Reliability,
            Latency = health.Latency,
            Throughput = health.Throughput,
            
            // Trend indicators
            HealthTrend = health.HealthTrend,
            PerformanceTrend = health.PerformanceTrend,
            ErrorTrend = health.ErrorTrend
        });
    }
}
```

### Performance Dashboard HTML

```html
<!DOCTYPE html>
<html>
<head>
    <title>Athena Cache Performance Dashboard</title>
    <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
    <script src="https://unpkg.com/@microsoft/signalr/dist/browser/signalr.min.js"></script>
    <style>
        .dashboard {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(400px, 1fr));
            gap: 20px;
            padding: 20px;
        }
        .metric-card {
            background: white;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
            padding: 20px;
        }
        .metric-value {
            font-size: 2.5rem;
            font-weight: bold;
            color: #2c3e50;
        }
        .metric-label {
            color: #7f8c8d;
            font-size: 0.9rem;
        }
        .chart-container {
            position: relative;
            height: 300px;
            width: 100%;
        }
        .status-good { color: #27ae60; }
        .status-warning { color: #f39c12; }
        .status-error { color: #e74c3c; }
    </style>
</head>
<body>
    <h1>🏛️ Athena Cache Performance Dashboard</h1>
    
    <div class="dashboard">
        <!-- Key Performance Indicators -->
        <div class="metric-card">
            <h3>Cache Hit Rate</h3>
            <div class="metric-value" id="hit-rate">--%</div>
            <div class="metric-label">Current hit rate</div>
        </div>
        
        <div class="metric-card">
            <h3>Response Time</h3>
            <div class="metric-value" id="response-time">-- ms</div>
            <div class="metric-label">P95 response time</div>
        </div>
        
        <div class="metric-card">
            <h3>Throughput</h3>
            <div class="metric-value" id="throughput">-- /s</div>
            <div class="metric-label">Requests per second</div>
        </div>
        
        <div class="metric-card">
            <h3>Memory Usage</h3>
            <div class="metric-value" id="memory-usage">-- MB</div>
            <div class="metric-label">Total memory consumption</div>
        </div>
        
        <!-- Performance Charts -->
        <div class="metric-card" style="grid-column: span 2;">
            <h3>Response Time Trends</h3>
            <div class="chart-container">
                <canvas id="responseTimeChart"></canvas>
            </div>
        </div>
        
        <div class="metric-card" style="grid-column: span 2;">
            <h3>Hit Rate Trends</h3>
            <div class="chart-container">
                <canvas id="hitRateChart"></canvas>
            </div>
        </div>
        
        <div class="metric-card" style="grid-column: span 2;">
            <h3>Throughput Analysis</h3>
            <div class="chart-container">
                <canvas id="throughputChart"></canvas>
            </div>
        </div>
        
        <!-- Cache Key Analysis -->
        <div class="metric-card">
            <h3>Top Cache Keys</h3>
            <div id="top-keys">Loading...</div>
        </div>
        
        <div class="metric-card">
            <h3>Memory Distribution</h3>
            <div class="chart-container">
                <canvas id="memoryDistributionChart"></canvas>
            </div>
        </div>
        
        <!-- Health Status -->
        <div class="metric-card">
            <h3>System Health</h3>
            <div class="metric-value" id="health-status">Unknown</div>
            <div class="metric-label">Overall system status</div>
            <div id="health-details"></div>
        </div>
    </div>

    <script>
        // Initialize charts
        const responseTimeChart = new Chart(document.getElementById('responseTimeChart'), {
            type: 'line',
            data: {
                labels: [],
                datasets: [{
                    label: 'P95 Response Time (ms)',
                    data: [],
                    borderColor: '#3498db',
                    backgroundColor: 'rgba(52, 152, 219, 0.1)',
                    tension: 0.4
                }, {
                    label: 'Average Response Time (ms)',
                    data: [],
                    borderColor: '#2ecc71',
                    backgroundColor: 'rgba(46, 204, 113, 0.1)',
                    tension: 0.4
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    y: {
                        beginAtZero: true,
                        title: {
                            display: true,
                            text: 'Response Time (ms)'
                        }
                    }
                }
            }
        });

        const hitRateChart = new Chart(document.getElementById('hitRateChart'), {
            type: 'line',
            data: {
                labels: [],
                datasets: [{
                    label: 'Hit Rate (%)',
                    data: [],
                    borderColor: '#e74c3c',
                    backgroundColor: 'rgba(231, 76, 60, 0.1)',
                    tension: 0.4
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    y: {
                        min: 0,
                        max: 100,
                        title: {
                            display: true,
                            text: 'Hit Rate (%)'
                        }
                    }
                }
            }
        });

        const throughputChart = new Chart(document.getElementById('throughputChart'), {
            type: 'line',
            data: {
                labels: [],
                datasets: [{
                    label: 'Requests/sec',
                    data: [],
                    borderColor: '#f39c12',
                    backgroundColor: 'rgba(243, 156, 18, 0.1)',
                    tension: 0.4
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    y: {
                        beginAtZero: true,
                        title: {
                            display: true,
                            text: 'Requests per Second'
                        }
                    }
                }
            }
        });

        const memoryDistributionChart = new Chart(document.getElementById('memoryDistributionChart'), {
            type: 'doughnut',
            data: {
                labels: ['Cache Data', 'String Pool', 'Collection Pool', 'Other'],
                datasets: [{
                    data: [0, 0, 0, 0],
                    backgroundColor: ['#3498db', '#2ecc71', '#f39c12', '#95a5a6']
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false
            }
        });

        // Real-time data updates
        function updateDashboard() {
            fetch('/api/cache/metrics/current')
                .then(response => response.json())
                .then(data => {
                    // Update KPI values
                    document.getElementById('hit-rate').textContent = data.hitRate.toFixed(1) + '%';
                    document.getElementById('response-time').textContent = data.p95ResponseTime.toFixed(1) + ' ms';
                    document.getElementById('throughput').textContent = data.requestsPerSecond.toFixed(0) + ' /s';
                    document.getElementById('memory-usage').textContent = (data.memoryUsage / 1024 / 1024).toFixed(1) + ' MB';
                    
                    // Update charts
                    const now = new Date().toLocaleTimeString();
                    
                    // Response time chart
                    responseTimeChart.data.labels.push(now);
                    responseTimeChart.data.datasets[0].data.push(data.p95ResponseTime);
                    responseTimeChart.data.datasets[1].data.push(data.averageResponseTime);
                    
                    // Hit rate chart
                    hitRateChart.data.labels.push(now);
                    hitRateChart.data.datasets[0].data.push(data.hitRate);
                    
                    // Throughput chart
                    throughputChart.data.labels.push(now);
                    throughputChart.data.datasets[0].data.push(data.requestsPerSecond);
                    
                    // Limit data points
                    const maxPoints = 20;
                    [responseTimeChart, hitRateChart, throughputChart].forEach(chart => {
                        if (chart.data.labels.length > maxPoints) {
                            chart.data.labels.shift();
                            chart.data.datasets.forEach(dataset => dataset.data.shift());
                        }
                        chart.update('none');
                    });
                })
                .catch(error => console.error('Error fetching metrics:', error));

            // Update cache key analysis
            fetch('/api/cache/metrics/cache-keys/analysis')
                .then(response => response.json())
                .then(data => {
                    const topKeysHtml = data.mostAccessedKeys
                        .slice(0, 10)
                        .map(key => `<div><code>${key.key}</code> (${key.accessCount} hits)</div>`)
                        .join('');
                    document.getElementById('top-keys').innerHTML = topKeysHtml;
                });

            // Update memory distribution
            fetch('/api/cache/metrics/memory/analysis')
                .then(response => response.json())
                .then(data => {
                    memoryDistributionChart.data.datasets[0].data = [
                        data.cacheMemoryUsage,
                        data.stringPoolStats.memoryUsage,
                        data.collectionPoolStats.memoryUsage,
                        data.totalMemoryUsage - data.cacheMemoryUsage - data.stringPoolStats.memoryUsage - data.collectionPoolStats.memoryUsage
                    ];
                    memoryDistributionChart.update();
                });

            // Update health status
            fetch('/api/cache/metrics/health')
                .then(response => response.json())
                .then(data => {
                    const statusElement = document.getElementById('health-status');
                    statusElement.textContent = data.overallHealth;
                    statusElement.className = 'metric-value ' + getHealthStatusClass(data.overallHealth);
                    
                    const detailsHtml = Object.entries(data.componentHealth)
                        .map(([component, status]) => `<div>${component}: <span class="${getHealthStatusClass(status)}">${status}</span></div>`)
                        .join('');
                    document.getElementById('health-details').innerHTML = detailsHtml;
                });
        }

        function getHealthStatusClass(status) {
            switch (status.toLowerCase()) {
                case 'healthy': return 'status-good';
                case 'degraded': return 'status-warning';
                case 'unhealthy': return 'status-error';
                default: return '';
            }
        }

        // Update dashboard every 10 seconds
        updateDashboard();
        setInterval(updateDashboard, 10000);
    </script>
</body>
</html>
```

## Custom Metrics and Alerts

Implement custom metrics for business-specific monitoring.

### Custom Metrics Configuration

```csharp
public class CustomMetricsService : ICustomMetricsService
{
    private readonly ConcurrentDictionary<string, CustomMetric> _customMetrics = new();
    private readonly ILogger<CustomMetricsService> _logger;

    public void RecordCustomMetric(string name, double value, Dictionary<string, string> tags = null)
    {
        var metric = new CustomMetric
        {
            Name = name,
            Value = value,
            Tags = tags ?? new Dictionary<string, string>(),
            Timestamp = DateTimeOffset.UtcNow
        };

        _customMetrics.AddOrUpdate(name, metric, (key, existing) =>
        {
            existing.Value = value;
            existing.Timestamp = DateTimeOffset.UtcNow;
            return existing;
        });

        // Check for alert conditions
        CheckAlertConditions(metric);
    }

    public void IncrementCounter(string name, Dictionary<string, string> tags = null)
    {
        var key = $"{name}_{string.Join("_", tags?.Values ?? Array.Empty<string>())}";
        var current = _customMetrics.GetOrAdd(key, _ => new CustomMetric
        {
            Name = name,
            Value = 0,
            Tags = tags ?? new Dictionary<string, string>(),
            Timestamp = DateTimeOffset.UtcNow
        });

        Interlocked.Increment(ref current.Value);
        current.Timestamp = DateTimeOffset.UtcNow;
    }

    public void RecordBusinessMetric(string operation, TimeSpan duration, bool success)
    {
        var tags = new Dictionary<string, string>
        {
            ["operation"] = operation,
            ["success"] = success.ToString().ToLower()
        };

        RecordCustomMetric($"business_operation_duration_ms", duration.TotalMilliseconds, tags);
        IncrementCounter("business_operation_count", tags);

        if (!success)
        {
            IncrementCounter("business_operation_errors", new Dictionary<string, string> { ["operation"] = operation });
        }
    }

    private void CheckAlertConditions(CustomMetric metric)
    {
        // Example: Alert if error rate exceeds threshold
        if (metric.Name == "business_operation_errors" && metric.Value > 10)
        {
            TriggerAlert($"High error rate detected: {metric.Value} errors for {metric.Tags.GetValueOrDefault("operation", "unknown")}");
        }

        // Example: Alert if response time is too high
        if (metric.Name == "business_operation_duration_ms" && metric.Value > 5000)
        {
            TriggerAlert($"Slow operation detected: {metric.Value}ms for {metric.Tags.GetValueOrDefault("operation", "unknown")}");
        }
    }

    private void TriggerAlert(string message)
    {
        _logger.LogWarning("ALERT: {Message}", message);
        
        // Send to alerting system (PagerDuty, Slack, etc.)
        // Implementation depends on your alerting infrastructure
    }
}

// Usage in business logic
[HttpGet("{id}")]
[AthenaCache(ExpirationMinutes = 30)]
public async Task<ActionResult<ProductDto>> GetProduct(
    int id, 
    [FromServices] ICustomMetricsService customMetrics)
{
    var stopwatch = Stopwatch.StartNew();
    var success = false;
    
    try
    {
        var product = await _productService.GetProductAsync(id);
        success = true;
        return Ok(product);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to get product {ProductId}", id);
        return StatusCode(500);
    }
    finally
    {
        stopwatch.Stop();
        customMetrics.RecordBusinessMetric("get_product", stopwatch.Elapsed, success);
    }
}
```

## Integration with Monitoring Systems

### OpenTelemetry Integration

```csharp
// Program.cs
builder.Services.AddOpenTelemetryMetrics(builder =>
{
    builder
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddAthenaCacheInstrumentation() // Custom instrumentation
        .AddPrometheusExporter()
        .AddConsoleExporter();
});

public class AthenaCacheInstrumentation
{
    private static readonly ActivitySource ActivitySource = new("Athena.Cache");
    private static readonly Meter Meter = new("Athena.Cache");
    
    private readonly Counter<long> _cacheHitCounter;
    private readonly Counter<long> _cacheMissCounter;
    private readonly Histogram<double> _cacheOperationDuration;
    private readonly ObservableGauge<long> _cacheSize;

    public AthenaCacheInstrumentation()
    {
        _cacheHitCounter = Meter.CreateCounter<long>(
            "athena_cache_hits_total",
            description: "Total number of cache hits");

        _cacheMissCounter = Meter.CreateCounter<long>(
            "athena_cache_misses_total", 
            description: "Total number of cache misses");

        _cacheOperationDuration = Meter.CreateHistogram<double>(
            "athena_cache_operation_duration_ms",
            unit: "ms",
            description: "Cache operation duration in milliseconds");

        _cacheSize = Meter.CreateObservableGauge<long>(
            "athena_cache_size_bytes",
            description: "Current cache size in bytes",
            observeValue: () => GetCurrentCacheSize());
    }

    public void RecordCacheHit(string key, string controller, TimeSpan duration)
    {
        using var activity = ActivitySource.StartActivity("cache.hit");
        activity?.SetTag("cache.key", key);
        activity?.SetTag("cache.controller", controller);

        _cacheHitCounter.Add(1, new TagList
        {
            ["controller"] = controller,
            ["operation"] = "hit"
        });

        _cacheOperationDuration.Record(duration.TotalMilliseconds, new TagList
        {
            ["controller"] = controller,
            ["operation"] = "hit"
        });
    }

    public void RecordCacheMiss(string key, string controller, TimeSpan duration)
    {
        using var activity = ActivitySource.StartActivity("cache.miss");
        activity?.SetTag("cache.key", key);
        activity?.SetTag("cache.controller", controller);

        _cacheMissCounter.Add(1, new TagList
        {
            ["controller"] = controller,
            ["operation"] = "miss"
        });

        _cacheOperationDuration.Record(duration.TotalMilliseconds, new TagList
        {
            ["controller"] = controller,
            ["operation"] = "miss"
        });
    }
}
```

### Prometheus Metrics Export

```csharp
[HttpGet("metrics")]
public async Task<IActionResult> GetPrometheusMetrics([FromServices] ICacheStatistics stats)
{
    var metrics = await stats.GetCurrentStatsAsync();
    
    var prometheusMetrics = new StringBuilder();
    
    // Cache hit rate
    prometheusMetrics.AppendLine("# HELP athena_cache_hit_rate Cache hit rate percentage");
    prometheusMetrics.AppendLine("# TYPE athena_cache_hit_rate gauge");
    prometheusMetrics.AppendLine($"athena_cache_hit_rate {metrics.HitRate}");
    
    // Response time
    prometheusMetrics.AppendLine("# HELP athena_cache_response_time_ms Response time in milliseconds");
    prometheusMetrics.AppendLine("# TYPE athena_cache_response_time_ms histogram");
    prometheusMetrics.AppendLine($"athena_cache_response_time_ms_bucket{{le=\"50\"}} {metrics.ResponseTimeBuckets.Le50}");
    prometheusMetrics.AppendLine($"athena_cache_response_time_ms_bucket{{le=\"100\"}} {metrics.ResponseTimeBuckets.Le100}");
    prometheusMetrics.AppendLine($"athena_cache_response_time_ms_bucket{{le=\"250\"}} {metrics.ResponseTimeBuckets.Le250}");
    prometheusMetrics.AppendLine($"athena_cache_response_time_ms_bucket{{le=\"500\"}} {metrics.ResponseTimeBuckets.Le500}");
    prometheusMetrics.AppendLine($"athena_cache_response_time_ms_bucket{{le=\"+Inf\"}} {metrics.TotalRequests}");
    
    // Memory usage
    prometheusMetrics.AppendLine("# HELP athena_cache_memory_usage_bytes Memory usage in bytes");
    prometheusMetrics.AppendLine("# TYPE athena_cache_memory_usage_bytes gauge");
    prometheusMetrics.AppendLine($"athena_cache_memory_usage_bytes {metrics.MemoryUsage}");
    
    // Request rate
    prometheusMetrics.AppendLine("# HELP athena_cache_requests_per_second Requests per second");
    prometheusMetrics.AppendLine("# TYPE athena_cache_requests_per_second gauge");
    prometheusMetrics.AppendLine($"athena_cache_requests_per_second {metrics.RequestsPerSecond}");

    return Content(prometheusMetrics.ToString(), "text/plain; version=0.0.4");
}
```

## Performance Baselines and SLOs

Establish service level objectives and track against baselines.

### SLO Configuration

```csharp
public class CacheServiceLevelObjectives
{
    public SLOConfig Availability { get; set; } = new()
    {
        Target = 99.9, // 99.9% availability
        Window = TimeSpan.FromDays(30)
    };

    public SLOConfig Latency { get; set; } = new()
    {
        Target = 95.0, // 95% of requests under 100ms
        Threshold = 100, // milliseconds
        Window = TimeSpan.FromHours(1)
    };

    public SLOConfig HitRate { get; set; } = new()
    {
        Target = 80.0, // 80% hit rate minimum
        Window = TimeSpan.FromHours(1)
    };

    public SLOConfig ErrorRate { get; set; } = new()
    {
        Target = 1.0, // Less than 1% error rate
        Window = TimeSpan.FromHours(1)
    };
}

public class SLOMonitoringService : BackgroundService
{
    private readonly ICacheStatistics _stats;
    private readonly CacheServiceLevelObjectives _slos;
    private readonly ILogger<SLOMonitoringService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckSLOs();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking SLOs");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task CheckSLOs()
    {
        var stats = await _stats.GetStatsForWindowAsync(_slos.Latency.Window);
        
        // Check latency SLO
        var latencyCompliance = CalculateLatencyCompliance(stats);
        if (latencyCompliance < _slos.Latency.Target)
        {
            _logger.LogWarning("Latency SLO violation: {Compliance}% (target: {Target}%)", 
                latencyCompliance, _slos.Latency.Target);
        }

        // Check hit rate SLO
        if (stats.HitRate < _slos.HitRate.Target)
        {
            _logger.LogWarning("Hit rate SLO violation: {HitRate}% (target: {Target}%)", 
                stats.HitRate, _slos.HitRate.Target);
        }

        // Check error rate SLO
        if (stats.ErrorRate > _slos.ErrorRate.Target)
        {
            _logger.LogError("Error rate SLO violation: {ErrorRate}% (target: < {Target}%)", 
                stats.ErrorRate, _slos.ErrorRate.Target);
        }

        // Calculate error budget burn rate
        var errorBudgetBurnRate = CalculateErrorBudgetBurnRate(stats);
        if (errorBudgetBurnRate > 1.0) // Burning error budget faster than sustainable
        {
            _logger.LogWarning("Error budget burn rate is {BurnRate}x the sustainable rate", 
                errorBudgetBurnRate);
        }
    }

    private double CalculateLatencyCompliance(CacheStatistics stats)
    {
        var totalRequests = stats.TotalRequests;
        var requestsUnderThreshold = stats.RequestsUnderThreshold(_slos.Latency.Threshold);
        
        return totalRequests > 0 ? (requestsUnderThreshold / (double)totalRequests) * 100 : 100;
    }

    private double CalculateErrorBudgetBurnRate(CacheStatistics stats)
    {
        var allowedErrorRate = 100 - _slos.Availability.Target; // 0.1% for 99.9% availability
        var actualErrorRate = stats.ErrorRate;
        
        return actualErrorRate / allowedErrorRate;
    }
}
```

## Troubleshooting Performance Issues

### Performance Diagnostic Tools

```csharp
[HttpGet("diagnostics/performance")]
public async Task<IActionResult> DiagnosePerformance(
    [FromServices] ICacheStatistics stats,
    [FromServices] ICacheProfiler profiler)
{
    var diagnostics = new PerformanceDiagnostics
    {
        Timestamp = DateTimeOffset.UtcNow,
        
        // System metrics
        SystemMetrics = new()
        {
            CpuUsage = await GetCpuUsageAsync(),
            MemoryUsage = GC.GetTotalMemory(false),
            ThreadCount = Process.GetCurrentProcess().Threads.Count,
            HandleCount = Process.GetCurrentProcess().HandleCount
        },
        
        // Cache metrics
        CacheMetrics = await stats.GetDetailedStatsAsync(),
        
        // Performance hotspots
        PerformanceHotspots = await profiler.GetHotspotsAsync(),
        
        // Slow operations
        SlowOperations = await profiler.GetSlowOperationsAsync(TimeSpan.FromMilliseconds(100)),
        
        // Memory analysis
        MemoryAnalysis = await AnalyzeMemoryUsageAsync(),
        
        // Recommendations
        Recommendations = GeneratePerformanceRecommendations(await stats.GetCurrentStatsAsync())
    };

    return Ok(diagnostics);
}

private List<string> GeneratePerformanceRecommendations(CacheStatistics stats)
{
    var recommendations = new List<string>();

    if (stats.HitRate < 70)
    {
        recommendations.Add("Consider increasing cache expiration times to improve hit rate");
        recommendations.Add("Review cache key patterns for potential improvements");
    }

    if (stats.AverageResponseTime > 50)
    {
        recommendations.Add("Enable Source Generator for better performance");
        recommendations.Add("Review serialization settings for optimization");
    }

    if (stats.MemoryUsage > 512 * 1024 * 1024) // 512MB
    {
        recommendations.Add("Enable memory pressure management");
        recommendations.Add("Consider reducing cache size or implementing eviction policies");
    }

    if (stats.ErrorRate > 1)
    {
        recommendations.Add("Investigate cache errors and implement proper error handling");
        recommendations.Add("Consider enabling fallback mechanisms");
    }

    return recommendations;
}
```

For advanced topics:
- **[Real-time Dashboards]({{ site.baseurl }}/monitoring/real-time-dashboards)** - Interactive monitoring
- **[Analytics]({{ site.baseurl }}/monitoring/analytics)** - Advanced analysis and insights
- **[Production Tuning]({{ site.baseurl }}/performance/production-tuning)** - Performance optimization
- **[Troubleshooting]({{ site.baseurl }}/advanced/troubleshooting)** - Diagnosing issues