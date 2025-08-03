---
layout: default
title: Real-time Dashboards
---

# Real-time Dashboards

Athena.Cache.Monitoring provides real-time dashboards using SignalR to monitor cache performance, track metrics, and receive alerts as they happen.

## Setup

### Install Package

```bash
dotnet add package Athena.Cache.Monitoring
```

### Configure Services

```csharp
// Program.cs
using Athena.Cache.Monitoring.Extensions;
using Athena.Cache.Monitoring.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add your cache (Core or Redis)
builder.Services.AddAthenaCacheComplete();

// Add monitoring services
builder.Services.AddAthenaCacheMonitoring(options =>
{
    options.MetricsCollectionInterval = TimeSpan.FromSeconds(30);
    options.HealthCheckInterval = TimeSpan.FromSeconds(5);
    options.EnableSignalRAlerts = true;
});

// Add SignalR
builder.Services.AddSignalR();

var app = builder.Build();

// Configure middleware
app.UseRouting();
app.UseAthenaCache();
app.UseAthenaCacheAnalytics();  // Add monitoring middleware

// Map SignalR hub
app.MapHub<CacheMonitoringHub>("/monitoring-hub");
app.MapControllers();

app.Run();
```

## Dashboard Implementation

### Basic HTML Dashboard

```html
<!DOCTYPE html>
<html>
<head>
    <title>Athena Cache Dashboard</title>
    <script src="https://unpkg.com/@microsoft/signalr/dist/browser/signalr.min.js"></script>
    <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
    <style>
        .dashboard {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
            gap: 20px;
            padding: 20px;
        }
        .card {
            background: white;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
            padding: 20px;
        }
        .metric-value {
            font-size: 2em;
            font-weight: bold;
            color: #2c3e50;
        }
        .metric-label {
            color: #7f8c8d;
            font-size: 0.9em;
        }
        .status-healthy { color: #27ae60; }
        .status-warning { color: #f39c12; }
        .status-error { color: #e74c3c; }
        .alert {
            padding: 10px;
            margin: 5px 0;
            border-radius: 4px;
            border-left: 4px solid;
        }
        .alert-info { border-color: #3498db; background: #ebf3fd; }
        .alert-warning { border-color: #f39c12; background: #fdf6e3; }
        .alert-error { border-color: #e74c3c; background: #fdf2f2; }
    </style>
</head>
<body>
    <h1>🏛️ Athena Cache Dashboard</h1>
    
    <div class="dashboard">
        <!-- Real-time Metrics -->
        <div class="card">
            <h3>Cache Performance</h3>
            <div class="metric-value" id="hit-rate">--%</div>
            <div class="metric-label">Hit Rate</div>
        </div>
        
        <div class="card">
            <h3>Request Volume</h3>
            <div class="metric-value" id="request-count">--</div>
            <div class="metric-label">Requests/min</div>
        </div>
        
        <div class="card">
            <h3>Response Time</h3>
            <div class="metric-value" id="response-time">-- ms</div>
            <div class="metric-label">Average</div>
        </div>
        
        <div class="card">
            <h3>Cache Size</h3>
            <div class="metric-value" id="cache-size">--</div>
            <div class="metric-label">Entries</div>
        </div>
        
        <!-- Health Status -->
        <div class="card">
            <h3>System Health</h3>
            <div class="metric-value" id="health-status">Unknown</div>
            <div class="metric-label">Status</div>
        </div>
        
        <!-- Memory Usage -->
        <div class="card">
            <h3>Memory Usage</h3>
            <div class="metric-value" id="memory-usage">-- MB</div>
            <div class="metric-label">Current</div>
        </div>
        
        <!-- Hit Rate Chart -->
        <div class="card" style="grid-column: span 2;">
            <h3>Hit Rate Trend</h3>
            <canvas id="hitRateChart" width="400" height="200"></canvas>
        </div>
        
        <!-- Recent Alerts -->
        <div class="card">
            <h3>Recent Alerts</h3>
            <div id="alerts-container">
                <p>No recent alerts</p>
            </div>
        </div>
    </div>

    <script>
        // SignalR Connection
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/monitoring-hub")
            .withAutomaticReconnect()
            .build();

        // Chart setup
        const ctx = document.getElementById('hitRateChart').getContext('2d');
        const hitRateChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: [],
                datasets: [{
                    label: 'Hit Rate %',
                    data: [],
                    borderColor: '#3498db',
                    backgroundColor: 'rgba(52, 152, 219, 0.1)',
                    tension: 0.4
                }]
            },
            options: {
                responsive: true,
                scales: {
                    y: {
                        beginAtZero: true,
                        max: 100,
                        ticks: {
                            callback: function(value) {
                                return value + '%';
                            }
                        }
                    }
                },
                plugins: {
                    legend: {
                        display: false
                    }
                }
            }
        });

        // Connect to SignalR
        connection.start().then(function () {
            console.log("Connected to cache monitoring hub");
            
            // Join monitoring group
            connection.invoke("JoinMonitoringGroup");
            
            // Request initial metrics
            connection.invoke("GetCurrentMetrics");
            
        }).catch(function (err) {
            console.error("Error connecting to hub: " + err);
        });

        // Handle incoming metrics
        connection.on("MetricsUpdated", function (metrics) {
            updateDashboard(metrics);
        });

        // Handle alerts
        connection.on("AlertReceived", function (alert) {
            addAlert(alert);
        });

        // Handle health status updates
        connection.on("HealthStatusChanged", function (healthStatus) {
            updateHealthStatus(healthStatus);
        });

        function updateDashboard(metrics) {
            // Update metric values
            document.getElementById('hit-rate').textContent = metrics.hitRate.toFixed(1) + '%';
            document.getElementById('request-count').textContent = metrics.requestCount.toLocaleString();
            document.getElementById('response-time').textContent = metrics.averageResponseTime.toFixed(1) + ' ms';
            document.getElementById('cache-size').textContent = metrics.cacheSize.toLocaleString();
            document.getElementById('memory-usage').textContent = (metrics.memoryUsage / 1024 / 1024).toFixed(1) + ' MB';
            
            // Update chart
            const now = new Date().toLocaleTimeString();
            hitRateChart.data.labels.push(now);
            hitRateChart.data.datasets[0].data.push(metrics.hitRate);
            
            // Keep only last 20 data points
            if (hitRateChart.data.labels.length > 20) {
                hitRateChart.data.labels.shift();
                hitRateChart.data.datasets[0].data.shift();
            }
            
            hitRateChart.update('none');
        }

        function updateHealthStatus(healthStatus) {
            const statusElement = document.getElementById('health-status');
            statusElement.textContent = healthStatus.status;
            
            // Update status color
            statusElement.className = 'metric-value';
            if (healthStatus.status === 'Healthy') {
                statusElement.classList.add('status-healthy');
            } else if (healthStatus.status === 'Warning') {
                statusElement.classList.add('status-warning');
            } else {
                statusElement.classList.add('status-error');
            }
        }

        function addAlert(alert) {
            const container = document.getElementById('alerts-container');
            
            // Remove "no alerts" message
            if (container.children.length === 1 && container.children[0].tagName === 'P') {
                container.innerHTML = '';
            }
            
            const alertDiv = document.createElement('div');
            alertDiv.className = `alert alert-${alert.level.toLowerCase()}`;
            alertDiv.innerHTML = `
                <strong>${alert.level}</strong>: ${alert.message}
                <br><small>${new Date(alert.timestamp).toLocaleString()}</small>
            `;
            
            container.insertBefore(alertDiv, container.firstChild);
            
            // Keep only last 5 alerts
            while (container.children.length > 5) {
                container.removeChild(container.lastChild);
            }
        }

        // Periodic refresh
        setInterval(() => {
            if (connection.state === signalR.HubConnectionState.Connected) {
                connection.invoke("GetCurrentMetrics");
            }
        }, 30000); // Every 30 seconds
    </script>
</body>
</html>
```

## Backend API for Dashboard Data

### Monitoring Controller

```csharp
[ApiController]
[Route("api/monitoring")]
public class MonitoringController : ControllerBase
{
    private readonly ICacheMetricsCollector _metricsCollector;
    private readonly ICacheHealthChecker _healthChecker;
    private readonly ICacheAlertService _alertService;

    public MonitoringController(
        ICacheMetricsCollector metricsCollector,
        ICacheHealthChecker healthChecker,
        ICacheAlertService alertService)
    {
        _metricsCollector = metricsCollector;
        _healthChecker = healthChecker;
        _alertService = alertService;
    }

    [HttpGet("metrics")]
    public async Task<ActionResult<CacheMetrics>> GetCurrentMetrics()
    {
        var metrics = await _metricsCollector.CollectMetricsAsync();
        return Ok(metrics);
    }

    [HttpGet("health")]
    public async Task<ActionResult<CacheHealthStatus>> GetHealthStatus()
    {
        var health = await _healthChecker.CheckHealthAsync();
        return Ok(health);
    }

    [HttpGet("alerts")]
    public async Task<ActionResult<IEnumerable<CacheAlert>>> GetRecentAlerts(
        [FromQuery] int count = 10)
    {
        var alerts = await _alertService.GetRecentAlertsAsync(count);
        return Ok(alerts);
    }

    [HttpGet("metrics/history")]
    public async Task<ActionResult<IEnumerable<CacheMetrics>>> GetMetricsHistory(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] int intervalMinutes = 5)
    {
        var start = startTime ?? DateTime.UtcNow.AddHours(-1);
        var metrics = await _metricsCollector.GetMetricsHistoryAsync(start, TimeSpan.FromMinutes(intervalMinutes));
        return Ok(metrics);
    }
}
```

## SignalR Hub Implementation

### Custom Monitoring Hub

```csharp
public class CacheMonitoringHub : Hub
{
    private readonly ICacheMetricsCollector _metricsCollector;
    private readonly ICacheHealthChecker _healthChecker;
    private readonly ILogger<CacheMonitoringHub> _logger;

    public CacheMonitoringHub(
        ICacheMetricsCollector metricsCollector,
        ICacheHealthChecker healthChecker,
        ILogger<CacheMonitoringHub> logger)
    {
        _metricsCollector = metricsCollector;
        _healthChecker = healthChecker;
        _logger = logger;
    }

    public async Task JoinMonitoringGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "Monitoring");
        _logger.LogInformation("Client {ConnectionId} joined monitoring group", Context.ConnectionId);
        
        // Send current metrics to new client
        await SendCurrentMetrics();
    }

    public async Task LeaveMonitoringGroup()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Monitoring");
        _logger.LogInformation("Client {ConnectionId} left monitoring group", Context.ConnectionId);
    }

    public async Task GetCurrentMetrics()
    {
        await SendCurrentMetrics();
    }

    private async Task SendCurrentMetrics()
    {
        try
        {
            var metrics = await _metricsCollector.CollectMetricsAsync();
            var health = await _healthChecker.CheckHealthAsync();
            
            await Clients.Caller.SendAsync("MetricsUpdated", metrics);
            await Clients.Caller.SendAsync("HealthStatusChanged", health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending current metrics to client {ConnectionId}", Context.ConnectionId);
        }
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        _logger.LogInformation("Client {ConnectionId} disconnected", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
```

## Advanced Dashboard Features

### Real-time Alerts

```javascript
// Enhanced alert handling with sound notifications
function addAlert(alert) {
    const container = document.getElementById('alerts-container');
    
    // Play sound for critical alerts
    if (alert.level === 'Error' || alert.level === 'Critical') {
        playAlertSound();
    }
    
    // Create alert element with dismiss button
    const alertDiv = document.createElement('div');
    alertDiv.className = `alert alert-${alert.level.toLowerCase()}`;
    alertDiv.innerHTML = `
        <div style="display: flex; justify-content: space-between; align-items: center;">
            <div>
                <strong>${alert.level}</strong>: ${alert.message}
                <br><small>${new Date(alert.timestamp).toLocaleString()}</small>
            </div>
            <button onclick="dismissAlert(this)" style="background: none; border: none; font-size: 1.2em; cursor: pointer;">&times;</button>
        </div>
    `;
    
    container.insertBefore(alertDiv, container.firstChild);
    
    // Auto-dismiss info alerts after 10 seconds
    if (alert.level === 'Info') {
        setTimeout(() => {
            if (alertDiv.parentNode) {
                alertDiv.parentNode.removeChild(alertDiv);
            }
        }, 10000);
    }
}

function playAlertSound() {
    // Create and play alert sound
    const audio = new Audio('data:audio/wav;base64,UklGRnoGAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQoGAACBhYqFbF1fdJivrJBhNjVgodDbq2EcBj+a2/LDciUFLIHO8tiJNwgZaLvt559NEAxQp+PwtmMcBjiR1/LMeSwFJHfH8N2QQAoUXrTp66hVFApGn+D0wmssBSV+zPDei');
    audio.play().catch(() => {
        // Ignore audio play errors (browser policies)
    });
}

function dismissAlert(button) {
    const alert = button.closest('.alert');
    alert.style.animation = 'slideOut 0.3s ease-out';
    setTimeout(() => {
        if (alert.parentNode) {
            alert.parentNode.removeChild(alert);
        }
    }, 300);
}
```

### Performance Charts

```javascript
// Multi-metric chart
const performanceChart = new Chart(document.getElementById('performanceChart'), {
    type: 'line',
    data: {
        labels: [],
        datasets: [
            {
                label: 'Hit Rate %',
                data: [],
                borderColor: '#3498db',
                backgroundColor: 'rgba(52, 152, 219, 0.1)',
                yAxisID: 'percentage'
            },
            {
                label: 'Response Time (ms)',
                data: [],
                borderColor: '#e74c3c',
                backgroundColor: 'rgba(231, 76, 60, 0.1)',
                yAxisID: 'time'
            },
            {
                label: 'Requests/min',
                data: [],
                borderColor: '#27ae60',
                backgroundColor: 'rgba(39, 174, 96, 0.1)',
                yAxisID: 'count'
            }
        ]
    },
    options: {
        responsive: true,
        interaction: {
            mode: 'index',
            intersect: false,
        },
        scales: {
            percentage: {
                type: 'linear',
                display: true,
                position: 'left',
                min: 0,
                max: 100
            },
            time: {
                type: 'linear',
                display: true,
                position: 'right',
                min: 0,
                grid: {
                    drawOnChartArea: false,
                }
            },
            count: {
                type: 'linear',
                display: false,
                min: 0
            }
        }
    }
});
```

### Cache Key Explorer

```html
<!-- Add to dashboard -->
<div class="card" style="grid-column: span 2;">
    <h3>Cache Key Explorer</h3>
    <input type="text" id="key-filter" placeholder="Filter cache keys..." style="width: 100%; margin-bottom: 10px;">
    <div id="cache-keys-list" style="max-height: 300px; overflow-y: auto;">
        <!-- Keys will be populated here -->
    </div>
</div>
```

```javascript
// Cache key exploration
connection.on("CacheKeysUpdated", function(keys) {
    updateCacheKeysList(keys);
});

function updateCacheKeysList(keys) {
    const container = document.getElementById('cache-keys-list');
    const filter = document.getElementById('key-filter').value.toLowerCase();
    
    const filteredKeys = keys.filter(key => 
        key.key.toLowerCase().includes(filter) || 
        key.tags.some(tag => tag.toLowerCase().includes(filter))
    );
    
    container.innerHTML = filteredKeys.map(key => `
        <div style="border-bottom: 1px solid #eee; padding: 8px 0;">
            <div style="font-family: monospace; font-size: 0.9em;">${key.key}</div>
            <div style="font-size: 0.8em; color: #666;">
                Tags: ${key.tags.join(', ')} | 
                Expires: ${new Date(key.expiration).toLocaleString()} |
                Size: ${formatBytes(key.size)}
            </div>
        </div>
    `).join('');
}

document.getElementById('key-filter').addEventListener('input', function() {
    if (connection.state === signalR.HubConnectionState.Connected) {
        connection.invoke("GetCacheKeys", this.value);
    }
});

function formatBytes(bytes) {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}
```

## Configuration Options

### Monitoring Configuration

```csharp
builder.Services.AddAthenaCacheMonitoring(options =>
{
    // Collection intervals
    options.MetricsCollectionInterval = TimeSpan.FromSeconds(15);
    options.HealthCheckInterval = TimeSpan.FromSeconds(5);
    options.MetricsRetentionPeriod = TimeSpan.FromHours(24);
    
    // Alert thresholds
    options.Thresholds.MaxMemoryUsagePercentage = 80.0;
    options.Thresholds.MinHitRatePercentage = 70.0;
    options.Thresholds.MaxResponseTimeMs = 100;
    options.Thresholds.MaxErrorRatePercentage = 5.0;
    
    // Real-time features
    options.EnableSignalRAlerts = true;
    options.EnableRealTimeMetrics = true;
    options.MaxConcurrentConnections = 100;
});
```

## Security Considerations

### Authentication for Dashboard

```csharp
// Secure SignalR hub
[Authorize(Roles = "Admin,CacheManager")]
public class SecureCacheMonitoringHub : CacheMonitoringHub
{
    // Hub implementation with authorization
}

// Configure authentication
app.MapHub<SecureCacheMonitoringHub>("/monitoring-hub")
   .RequireAuthorization("CacheMonitoringPolicy");
```

### API Security

```csharp
[ApiController]
[Route("api/monitoring")]
[Authorize(Policy = "CacheMonitoring")]
public class SecureMonitoringController : ControllerBase
{
    // Secure monitoring endpoints
}
```

## Next Steps

- **[Performance Metrics]({{ site.baseurl }}/monitoring/performance-metrics)** - Detailed metrics collection
- **[Analytics]({{ site.baseurl }}/monitoring/analytics)** - Advanced analytics and insights
- **[Performance Tuning]({{ site.baseurl }}/performance/production-tuning)** - Optimize based on monitoring data