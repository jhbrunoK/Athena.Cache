# Invalidus.Monitoring

**Comprehensive Monitoring and Analytics for Invalidus** - The Universal Cache Invalidation Engine

## 🎯 What is Invalidus.Monitoring?

Invalidus.Monitoring provides complete observability for your cache invalidation operations:

- **🔍 Real-time Monitoring**: Live metrics for cache operations and invalidation patterns
- **🏥 Health Checks**: Comprehensive health monitoring for all providers and the engine
- **📊 Performance Analytics**: Deep insights into invalidation patterns and efficiency
- **🚨 Smart Alerting**: Configurable alerts for performance degradation and failures
- **📈 Trend Analysis**: Historical data analysis and pattern recognition
- **🎯 Hot Key Detection**: Identify frequently invalidated keys and optimization opportunities

## 📦 Installation

```bash
dotnet add package Invalidus.Monitoring
```

## 🚀 Quick Start

### Basic Monitoring Setup

```csharp
using Invalidus.Monitoring.Extensions;

// Program.cs or Startup.cs
builder.Services.AddInvalidusMonitoring(options =>
{
    options.MetricsCollectionInterval = TimeSpan.FromMinutes(1);
    options.AlertEvaluationInterval = TimeSpan.FromMinutes(1);
    options.LogEvents = true;
});
```

### Environment-Specific Setup

```csharp
// Development - Detailed monitoring
builder.Services.AddInvalidusMonitoringDevelopment();

// Production - Optimized for performance
builder.Services.AddInvalidusMonitoringProduction();

// High-Performance - Minimal overhead
builder.Services.AddInvalidusMonitoringHighPerformance();
```

### With Health Checks

```csharp
builder.Services.AddInvalidusMonitoringWithHealthChecks();

// Then map health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
```

## 💡 Core Features

### 1. Real-time Metrics Collection

```csharp
public class DashboardService
{
    private readonly IInvalidationMonitor _monitor;
    
    public DashboardService(IInvalidationMonitor monitor)
    {
        _monitor = monitor;
    }
    
    public async Task<DashboardData> GetDashboardAsync()
    {
        // Get current unified metrics
        var metrics = await _monitor.CollectMetricsAsync();
        
        // Get provider-specific metrics
        var providerMetrics = await _monitor.CollectProviderMetricsAsync();
        
        // Get system health
        var systemHealth = await _monitor.CheckSystemHealthAsync();
        
        return new DashboardData
        {
            CacheHitRatio = metrics.CacheHitRatio,
            InvalidationSuccessRate = metrics.InvalidationSuccessRate,
            TotalKeys = metrics.TotalKeys,
            TotalMemoryUsage = metrics.TotalMemoryUsage,
            IsSystemHealthy = systemHealth.IsHealthy,
            ProviderStatuses = systemHealth.ProviderStatuses
        };
    }
}
```

### 2. Performance Analytics

```csharp
public class AnalyticsService
{
    private readonly IInvalidationMonitor _monitor;
    
    public async Task<PerformanceReport> GenerateReportAsync()
    {
        // Get performance statistics for the last 24 hours
        var stats = await _monitor.GetPerformanceStatsAsync(TimeSpan.FromHours(24));
        
        // Analyze hot keys
        var hotKeys = await _monitor.GetHotKeysAnalysisAsync(TimeSpan.FromHours(24), 20);
        
        // Pattern analysis
        var patterns = await _monitor.AnalyzeInvalidationPatternsAsync(TimeSpan.FromHours(24));
        
        return new PerformanceReport
        {
            TotalInvalidations = stats.TotalInvalidations,
            AverageLatency = stats.AverageLatency,
            ErrorRate = stats.ErrorRate,
            TopHotKeys = hotKeys.Take(10),
            Recommendations = patterns.Recommendations
        };
    }
}
```

### 3. Smart Alerting

```csharp
// Configure custom alerts
builder.Services.AddInvalidusMonitoringWithAlerts(
    configureOptions: options =>
    {
        options.AlertEvaluationInterval = TimeSpan.FromMinutes(1);
    },
    configureAlerts: alertConfig =>
    {
        // Cache hit ratio alert
        alertConfig.Thresholds["CacheHitRatio"] = new AlertThreshold
        {
            MetricName = "CacheHitRatio",
            WarningThreshold = 0.80,  // 80%
            CriticalThreshold = 0.70, // 70%
            ComparisonType = AlertComparisonType.LessThan,
            EvaluationWindow = TimeSpan.FromMinutes(5)
        };
        
        // Memory usage alert
        alertConfig.Thresholds["MemoryUsage"] = new AlertThreshold
        {
            MetricName = "TotalMemoryUsage",
            WarningThreshold = 1_000_000_000,  // 1GB
            CriticalThreshold = 2_000_000_000, // 2GB
            ComparisonType = AlertComparisonType.GreaterThan
        };
        
        // Invalidation failure alert
        alertConfig.Thresholds["InvalidationFailures"] = new AlertThreshold
        {
            MetricName = "FailedInvalidations",
            WarningThreshold = 10,
            CriticalThreshold = 50,
            ComparisonType = AlertComparisonType.GreaterThan,
            EvaluationWindow = TimeSpan.FromMinutes(5)
        };
    });

// Handle alerts in your application
public class AlertHandler
{
    public AlertHandler(IInvalidationMonitor monitor)
    {
        monitor.AlertTriggered += OnAlertTriggered;
    }
    
    private void OnAlertTriggered(object? sender, AlertTriggeredEventArgs e)
    {
        var alert = e.Alert;
        
        switch (alert.Severity)
        {
            case AlertSeverity.Critical:
                await _notificationService.SendCriticalAlert(alert);
                await _slackService.PostToOpsChannel($"🚨 CRITICAL: {alert.Message}");
                break;
                
            case AlertSeverity.Warning:
                await _emailService.SendWarningEmail(alert);
                _logger.LogWarning("Warning alert: {Message}", alert.Message);
                break;
        }
    }
}
```

### 4. Historical Analysis

```csharp
public class HistoricalAnalysisService
{
    private readonly IInvalidationMonitor _monitor;
    
    public async Task<TrendAnalysis> AnalyzeTrendsAsync()
    {
        // Get metrics for the last 7 days
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddDays(-7);
        var interval = TimeSpan.FromHours(1);
        
        var history = await _monitor.GetMetricsHistoryAsync(startTime, endTime, interval);
        
        // Analyze trends
        var hitRatioTrend = CalculateTrend(history.Select(h => h.CacheHitRatio));
        var invalidationTrend = CalculateTrend(history.Select(h => (double)h.TotalInvalidations));
        var memoryTrend = CalculateTrend(history.Select(h => (double)h.TotalMemoryUsage));
        
        return new TrendAnalysis
        {
            HitRatioTrend = hitRatioTrend,
            InvalidationTrend = invalidationTrend,
            MemoryUsageTrend = memoryTrend,
            PeakInvalidationHours = FindPeakHours(history),
            OptimizationOpportunities = GenerateOptimizationSuggestions(history)
        };
    }
}
```

## 📊 Monitoring Dashboards

### Console Dashboard

```csharp
public class ConsoleDashboard
{
    private readonly IInvalidationMonitor _monitor;
    
    public async Task DisplayAsync()
    {
        while (true)
        {
            Console.Clear();
            
            var metrics = await _monitor.CollectMetricsAsync();
            var systemHealth = await _monitor.CheckSystemHealthAsync();
            
            Console.WriteLine("=== Invalidus Monitoring Dashboard ===");
            Console.WriteLine($"System Health: {(systemHealth.IsHealthy ? "✅ Healthy" : "❌ Issues")}");
            Console.WriteLine($"Cache Hit Ratio: {metrics.CacheHitRatio:P2}");
            Console.WriteLine($"Invalidation Success Rate: {metrics.InvalidationSuccessRate:P2}");
            Console.WriteLine($"Total Keys: {metrics.TotalKeys:N0}");
            Console.WriteLine($"Memory Usage: {metrics.TotalMemoryUsage / 1024 / 1024:N0} MB");
            Console.WriteLine();
            
            Console.WriteLine("=== Provider Status ===");
            foreach (var provider in systemHealth.ProviderStatuses)
            {
                var status = provider.Value.IsHealthy ? "✅" : "❌";
                Console.WriteLine($"{status} {provider.Key} ({provider.Value.ProviderType})");
            }
            
            var activeAlerts = await _monitor.GetActiveAlertsAsync();
            if (activeAlerts.Any())
            {
                Console.WriteLine();
                Console.WriteLine("=== Active Alerts ===");
                foreach (var alert in activeAlerts)
                {
                    var severity = alert.Severity == AlertSeverity.Critical ? "🚨" : "⚠️";
                    Console.WriteLine($"{severity} {alert.MetricName}: {alert.Message}");
                }
            }
            
            await Task.Delay(5000);
        }
    }
}
```

### ASP.NET Core Integration

```csharp
[ApiController]
[Route("api/[controller]")]
public class MonitoringController : ControllerBase
{
    private readonly IInvalidationMonitor _monitor;
    
    public MonitoringController(IInvalidationMonitor monitor)
    {
        _monitor = monitor;
    }
    
    [HttpGet("health")]
    public async Task<ActionResult<InvalidationSystemHealth>> GetHealthAsync()
    {
        var health = await _monitor.CheckSystemHealthAsync();
        return Ok(health);
    }
    
    [HttpGet("metrics")]
    public async Task<ActionResult<InvalidationMetrics>> GetMetricsAsync()
    {
        var metrics = await _monitor.CollectMetricsAsync();
        return Ok(metrics);
    }
    
    [HttpGet("performance")]
    public async Task<ActionResult<InvalidationPerformanceStats>> GetPerformanceAsync([FromQuery] int hours = 24)
    {
        var stats = await _monitor.GetPerformanceStatsAsync(TimeSpan.FromHours(hours));
        return Ok(stats);
    }
    
    [HttpGet("hotkeys")]
    public async Task<ActionResult<IEnumerable<HotKeyAnalysis>>> GetHotKeysAsync([FromQuery] int hours = 24)
    {
        var hotKeys = await _monitor.GetHotKeysAnalysisAsync(TimeSpan.FromHours(hours), 50);
        return Ok(hotKeys);
    }
    
    [HttpGet("patterns")]
    public async Task<ActionResult<InvalidationPatternAnalysis>> GetPatternsAsync([FromQuery] int hours = 24)
    {
        var patterns = await _monitor.AnalyzeInvalidationPatternsAsync(TimeSpan.FromHours(hours));
        return Ok(patterns);
    }
    
    [HttpGet("alerts")]
    public async Task<ActionResult<IEnumerable<ActiveAlert>>> GetActiveAlertsAsync()
    {
        var alerts = await _monitor.GetActiveAlertsAsync();
        return Ok(alerts);
    }
}
```

## 🔧 Configuration

### appsettings.json

```json
{
  "Invalidus": {
    "Monitoring": {
      "AlertEvaluationInterval": "00:01:00",
      "MetricsCollectionInterval": "00:01:00", 
      "MaxEventHistory": 10000,
      "MaxTrackedKeysWarning": 100000,
      "LogEvents": false,
      "LogPerformanceEvents": false
    }
  }
}
```

### Environment Variables

```bash
# Docker/Kubernetes environment
INVALIDUS__MONITORING__ALERTEVALUATIONINTERVAL=00:01:00
INVALIDUS__MONITORING__METRICSCOLLECTIONINTERVAL=00:01:00
INVALIDUS__MONITORING__LOGEVENTS=false
```

## 📈 Metrics Available

### System-Level Metrics
- **System Health**: Overall health status of all providers
- **Provider Status**: Individual provider health and availability
- **Engine Health**: Invalidation engine performance and status

### Cache Metrics
- **Hit Ratio**: Cache effectiveness across all providers
- **Total Operations**: Get/Set/Remove operation counts
- **Response Times**: Average/P95/P99 cache operation latencies
- **Memory Usage**: Memory consumption by provider
- **Connection Status**: Provider connectivity and uptime

### Invalidation Metrics
- **Invalidation Rate**: Operations per second
- **Success Rate**: Percentage of successful invalidations
- **Pattern Analysis**: Most frequent invalidation patterns
- **Hot Keys**: Most frequently invalidated keys
- **Cascade Analysis**: Related invalidation chains

### Performance Metrics
- **Throughput**: Operations per second by provider
- **Latency**: Response time percentiles
- **Error Rates**: Failure percentages by type
- **Resource Usage**: CPU, memory, connection utilization

## 🚨 Default Alert Thresholds

| Metric | Warning | Critical | Type |
|--------|---------|----------|------|
| Cache Hit Ratio | < 80% | < 70% | Less Than |
| Invalidation Success Rate | < 95% | < 90% | Less Than |
| Memory Usage | > 1GB | > 2GB | Greater Than |
| Failed Invalidations | > 10/5min | > 50/5min | Greater Than |
| Response Time P95 | > 100ms | > 500ms | Greater Than |

## 🔍 Integration Examples

### Prometheus Metrics Export

```csharp
// Would need additional Prometheus package
builder.Services.AddInvalidusMonitoringWithPrometheus();
```

### Application Insights Integration

```csharp
builder.Services.AddInvalidusMonitoringWithApplicationInsights();
```

### Custom Metrics Export

```csharp
public class CustomMetricsExporter : BackgroundService
{
    private readonly IInvalidationMonitor _monitor;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var metrics = await _monitor.CollectMetricsAsync();
            
            // Export to your monitoring system
            await _customMonitoringSystem.SendMetrics(metrics);
            
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
```

## 🔗 Ecosystem Integration

Invalidus.Monitoring works seamlessly with the entire Invalidus ecosystem:

```csharp
services.AddInvalidus(builder =>
{
    builder.UseRedis("localhost:6379")           // Cache provider
           .UseMemoryCache()                     // Additional provider  
           .EnableCQRS()                        // Event-driven invalidation
           .EnableMonitoring(options =>         // This package
           {
               options.LogEvents = true;
               options.MetricsCollectionInterval = TimeSpan.FromSeconds(30);
           })
           .EnableAlerts(alerts =>              // Smart alerting
           {
               alerts.OnCacheHitRatioBelow(0.80);
               alerts.OnMemoryUsageAbove(1_000_000_000);
           });
});
```

---

**Invalidus.Monitoring** - *Complete observability for your cache invalidation operations* 📊