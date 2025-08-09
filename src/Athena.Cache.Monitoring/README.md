# 📊 Athena.Cache.Monitoring

**Real-time monitoring and alerting system for Athena Cache with SignalR dashboard and multi-channel alerts**

Athena.Cache.Monitoring provides comprehensive monitoring capabilities for your Athena Cache implementation, including real-time dashboards, performance metrics, health checks, and multi-channel alerting.

## ✨ Key Features

- 📊 **Real-time Monitoring**: Live performance metrics and cache statistics
- 🔔 **Multi-Channel Alerting**: Console, Log, and SignalR alert channels
- 🏥 **Health Checks**: Automatic cache health monitoring and status reporting
- 📈 **Performance Metrics**: Cache hit rates, memory usage, and operation statistics
- 🌐 **SignalR Dashboard**: Real-time web dashboard for monitoring cache performance
- ⚡ **Background Monitoring**: Continuous monitoring with configurable intervals
- 🎯 **Threshold Alerts**: Customizable performance and health thresholds
- 🔧 **Easy Integration**: Simple setup with minimal configuration

## 🚀 Quick Start

### Installation

```bash
# Install the monitoring package
dotnet add package Athena.Cache.Monitoring
```

### Basic Setup

```csharp
// Program.cs
using Athena.Cache.Monitoring.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Athena Cache Core (required)
builder.Services.AddAthenaCacheComplete();

// Add Monitoring Services
builder.Services.AddAthenaCacheMonitoring(options =>
{
    options.MetricsCollectionInterval = TimeSpan.FromSeconds(30);
    options.HealthCheckInterval = TimeSpan.FromSeconds(5);
    options.MetricsRetentionPeriod = TimeSpan.FromHours(24);
    options.EnableSignalRAlerts = true;
});

// Add SignalR for real-time dashboard
builder.Services.AddSignalR();

var app = builder.Build();

// Configure middleware
app.UseRouting();
app.MapHub<CacheMonitoringHub>("/monitoring-hub");
app.MapControllers();

app.Run();
```

## 📊 Monitoring Features

### Real-time Metrics

```csharp
[ApiController]
[Route("api/[controller]")]
public class CacheMonitoringController : ControllerBase
{
    private readonly ICacheMetricsCollector _metricsCollector;
    private readonly ICacheHealthChecker _healthChecker;

    public CacheMonitoringController(
        ICacheMetricsCollector metricsCollector,
        ICacheHealthChecker healthChecker)
    {
        _metricsCollector = metricsCollector;
        _healthChecker = healthChecker;
    }

    [HttpGet("metrics")]
    public async Task<CacheMetrics> GetMetrics()
    {
        return await _metricsCollector.CollectMetricsAsync();
    }

    [HttpGet("health")]
    public async Task<CacheHealthStatus> GetHealth()
    {
        return await _healthChecker.CheckHealthAsync();
    }
}
```

### Alert Configuration

```csharp
// Configure alert thresholds
builder.Services.AddAthenaCacheMonitoring(options =>
{
    options.Thresholds.MaxMemoryUsagePercentage = 80.0;
    options.Thresholds.MinHitRatePercentage = 70.0;
    options.Thresholds.MaxResponseTimeMs = 100;
    options.Thresholds.MaxErrorRatePercentage = 5.0;
});
```

### Custom Alert Channels

```csharp
// Create a custom alert channel
public class EmailAlertChannel : IAlertChannel
{
    public string Name => "Email";

    public async Task SendAlertAsync(CacheAlert alert)
    {
        // Send email notification
        await EmailService.SendAsync(alert);
    }
}

// Register custom alert channel
builder.Services.AddSingleton<IAlertChannel, EmailAlertChannel>();
```

## 🌐 SignalR Dashboard

### Client-side JavaScript

```javascript
// Connect to the monitoring hub
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/monitoring-hub")
    .build();

// Join monitoring group
await connection.start();
await connection.invoke("JoinMonitoringGroup");

// Listen for real-time metrics
connection.on("MetricsUpdated", (metrics) => {
    updateDashboard(metrics);
});

// Listen for alerts
connection.on("AlertReceived", (alert) => {
    showAlert(alert);
});
```

### Dashboard HTML

```html
<!DOCTYPE html>
<html>
<head>
    <title>Cache Monitoring Dashboard</title>
    <script src="https://unpkg.com/@microsoft/signalr/dist/browser/signalr.min.js"></script>
</head>
<body>
    <div id="metrics">
        <h2>Cache Metrics</h2>
        <div id="hit-rate">Hit Rate: <span id="hit-rate-value">-</span>%</div>
        <div id="memory-usage">Memory Usage: <span id="memory-usage-value">-</span>%</div>
        <div id="request-count">Requests: <span id="request-count-value">-</span></div>
    </div>

    <div id="alerts">
        <h2>Alerts</h2>
        <div id="alert-list"></div>
    </div>

    <script>
        // Dashboard implementation
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/monitoring-hub")
            .build();

        connection.start().then(() => {
            connection.invoke("JoinMonitoringGroup");
        });

        connection.on("MetricsUpdated", (metrics) => {
            document.getElementById("hit-rate-value").textContent = metrics.hitRate.toFixed(1);
            document.getElementById("memory-usage-value").textContent = metrics.memoryUsage.toFixed(1);
            document.getElementById("request-count-value").textContent = metrics.requestCount;
        });
    </script>
</body>
</html>
```

## ⚙️ Configuration Options

### Monitoring Options

```csharp
public class CacheMonitoringOptions
{
    /// <summary>
    /// Metrics collection interval (default: 30 seconds)
    /// </summary>
    public TimeSpan MetricsCollectionInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Metrics retention period (default: 24 hours)
    /// </summary>
    public TimeSpan MetricsRetentionPeriod { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Health check interval (default: 5 seconds)
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Enable SignalR real-time alerts
    /// </summary>
    public bool EnableSignalRAlerts { get; set; } = true;

    /// <summary>
    /// Alert threshold settings
    /// </summary>
    public AlertThresholds Thresholds { get; set; } = new();
}
```

### Alert Thresholds

```csharp
public class AlertThresholds
{
    /// <summary>
    /// Maximum memory usage percentage before alert (default: 80%)
    /// </summary>
    public double MaxMemoryUsagePercentage { get; set; } = 80.0;

    /// <summary>
    /// Minimum cache hit rate percentage before alert (default: 70%)
    /// </summary>
    public double MinHitRatePercentage { get; set; } = 70.0;

    /// <summary>
    /// Maximum response time in milliseconds before alert (default: 100ms)
    /// </summary>
    public int MaxResponseTimeMs { get; set; } = 100;

    /// <summary>
    /// Maximum error rate percentage before alert (default: 5%)
    /// </summary>
    public double MaxErrorRatePercentage { get; set; } = 5.0;
}
```

## 🔧 Advanced Usage

### Redis Monitoring

```csharp
// Add Redis-specific monitoring
builder.Services.AddAthenaCacheMonitoring(options =>
{
    // Standard monitoring options
    options.MetricsCollectionInterval = TimeSpan.FromSeconds(15);
})
.AddSingleton<ICacheMetricsCollector, RedisCacheMetricsCollector>();
```

### Health Check Integration

```csharp
// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<CacheHealthChecker>("cache");

// Configure health check UI
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

## 📈 Metrics Available

- **Hit Rate**: Percentage of cache hits vs total requests
- **Memory Usage**: Current memory consumption percentage
- **Request Count**: Total number of cache requests
- **Error Rate**: Percentage of failed cache operations
- **Response Time**: Average cache operation response time
- **Eviction Count**: Number of cache entries evicted
- **Entry Count**: Current number of cached entries

## 🔔 Alert Types

- **High Memory Usage**: When memory usage exceeds threshold
- **Low Hit Rate**: When cache hit rate falls below threshold
- **High Error Rate**: When error rate exceeds threshold
- **Slow Response**: When response time exceeds threshold
- **Health Issues**: When cache becomes unhealthy

## 🤝 Integration with Athena.Cache

This monitoring package seamlessly integrates with:
- **[Athena.Cache.Core](https://www.nuget.org/packages/Athena.Cache.Core/)**: Core caching functionality
- **[Athena.Cache.Redis](https://www.nuget.org/packages/Athena.Cache.Redis/)**: Redis distributed caching
- **[Athena.Cache.Analytics](https://www.nuget.org/packages/Athena.Cache.Analytics/)**: Advanced analytics and insights

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/jhbrunoK/Athena.Cache/blob/main/LICENSE.txt) file for details.

## 🐛 Issues & Support

- **Repository**: [https://github.com/jhbrunoK/Athena.Cache](https://github.com/jhbrunoK/Athena.Cache)
- **Issues**: [https://github.com/jhbrunoK/Athena.Cache/issues](https://github.com/jhbrunoK/Athena.Cache/issues)
- **Documentation**: [https://github.com/jhbrunoK/Athena.Cache](https://github.com/jhbrunoK/Athena.Cache)