# =È Athena.Cache.Analytics

**Advanced analytics and monitoring capabilities for Athena Cache with real-time dashboards, performance insights, and usage pattern analysis**

Athena.Cache.Analytics provides comprehensive analytics and insights for your Athena Cache implementation, including usage patterns, performance metrics, hot key analysis, and predictive optimization recommendations.

## ( Key Features

- =Ê **Real-time Analytics**: Live performance dashboards and metrics
- =% **Hot Key Analysis**: Identify frequently accessed cache keys
- D **Cold Key Detection**: Find underutilized cache entries
- =È **Usage Patterns**: Hourly, daily, and endpoint-based usage analysis
- ¡ **Performance Insights**: Response times, hit rates, and optimization suggestions
- =Ä **SQLite Storage**: Lightweight analytics data storage
- <¯ **Invalidation Analysis**: Track cache invalidation patterns
- =Å **Time Series Data**: Historical trend analysis
- = **Query Analytics**: Detailed cache operation insights
- =Ë **Export Capabilities**: Export analytics data in multiple formats

## =€ Quick Start

### Installation

```bash
# Install Analytics package
dotnet add package Athena.Cache.Analytics

# Core dependency (if not already installed)
dotnet add package Athena.Cache.Core
```

### Basic Setup

```csharp
// Program.cs
using Athena.Cache.Analytics.Extensions;
using Athena.Cache.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Athena Cache Core
builder.Services.AddAthenaCacheComplete();

// Add Analytics with SQLite storage
builder.Services.AddAthenaCacheAnalytics(options =>
{
    options.DatabasePath = "cache_analytics.db";
    options.EnableEventCollection = true;
    options.RetentionDays = 30;
    options.CollectDetailedMetrics = true;
});

var app = builder.Build();

// Add analytics middleware
app.UseRouting();
app.UseAthenaCache();
app.UseAthenaCacheAnalytics();  // Add analytics tracking
app.MapControllers();

app.Run();
```

### Dashboard Controller

```csharp
[ApiController]
[Route("api/analytics")]
public class AnalyticsDashboardController : ControllerBase
{
    private readonly ICacheAnalyticsService _analytics;

    public AnalyticsDashboardController(ICacheAnalyticsService analytics)
    {
        _analytics = analytics;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview()
    {
        var statistics = await _analytics.GetStatisticsAsync(
            DateTime.UtcNow.AddDays(-7), 
            DateTime.UtcNow);
            
        return Ok(statistics);
    }

    [HttpGet("hot-keys")]
    public async Task<IActionResult> GetHotKeys([FromQuery] int limit = 20)
    {
        var hotKeys = await _analytics.GetHotKeyAnalysisAsync(
            DateTime.UtcNow.AddDays(-1), 
            DateTime.UtcNow, 
            limit);
            
        return Ok(hotKeys);
    }

    [HttpGet("usage-patterns")]
    public async Task<IActionResult> GetUsagePatterns()
    {
        var patterns = await _analytics.GetUsagePatternAnalysisAsync(
            DateTime.UtcNow.AddDays(-7), 
            DateTime.UtcNow);
            
        return Ok(patterns);
    }
}
```

## =Ê Analytics Dashboard

### Real-time Statistics

```csharp
// Get comprehensive cache statistics
var stats = await _analytics.GetStatisticsAsync(startDate, endDate);

Console.WriteLine($"Hit Rate: {stats.HitRate:P2}");
Console.WriteLine($"Total Requests: {stats.TotalRequests}");
Console.WriteLine($"Average Response Time: {stats.AverageResponseTime}ms");
Console.WriteLine($"Cache Size: {stats.CacheSize} entries");
Console.WriteLine($"Memory Usage: {stats.MemoryUsage:F2} MB");
```

### Hot Key Analysis

```csharp
// Identify frequently accessed keys
var hotKeys = await _analytics.GetHotKeyAnalysisAsync(
    DateTime.UtcNow.AddHours(-24), 
    DateTime.UtcNow, 
    limit: 50);

foreach (var hotKey in hotKeys)
{
    Console.WriteLine($"Key: {hotKey.CacheKey}");
    Console.WriteLine($"Access Count: {hotKey.AccessCount}");
    Console.WriteLine($"Last Access: {hotKey.LastAccessTime}");
    Console.WriteLine($"Average Response Time: {hotKey.AverageResponseTime}ms");
    Console.WriteLine($"Hit Rate: {hotKey.HitRate:P2}");
    Console.WriteLine("---");
}
```

### Usage Pattern Analysis

```csharp
// Analyze usage patterns
var patterns = await _analytics.GetUsagePatternAnalysisAsync(
    DateTime.UtcNow.AddDays(-7), 
    DateTime.UtcNow);

// Hourly distribution
Console.WriteLine("Hourly Usage Distribution:");
foreach (var hour in patterns.HourlyDistribution.OrderBy(h => h.Key))
{
    Console.WriteLine($"Hour {hour.Key:D2}: {hour.Value} requests");
}

// Endpoint popularity
Console.WriteLine("\nMost Popular Endpoints:");
foreach (var endpoint in patterns.EndpointPopularity.OrderByDescending(e => e.Value).Take(10))
{
    Console.WriteLine($"{endpoint.Key}: {endpoint.Value} requests");
}

// Performance insights
Console.WriteLine("\nAverage Response Times:");
foreach (var response in patterns.AverageResponseTimes.OrderByDescending(r => r.Value).Take(10))
{
    Console.WriteLine($"{response.Key}: {response.Value:F2}ms");
}
```

## =È Time Series Analytics

### Historical Trends

```csharp
// Get time series data for trend analysis
var timeSeries = await _analytics.GetTimeSeriesDataAsync(
    DateTime.UtcNow.AddDays(-30), 
    DateTime.UtcNow, 
    TimeSpan.FromHours(1)); // 1-hour intervals

foreach (var dataPoint in timeSeries)
{
    Console.WriteLine($"Time: {dataPoint.Timestamp}");
    Console.WriteLine($"Requests: {dataPoint.RequestCount}");
    Console.WriteLine($"Hit Rate: {dataPoint.HitRate:P2}");
    Console.WriteLine($"Response Time: {dataPoint.AverageResponseTime}ms");
    Console.WriteLine("---");
}
```

### Performance Monitoring

```csharp
[ApiController]
public class PerformanceController : ControllerBase
{
    [HttpGet("performance/trends")]
    public async Task<IActionResult> GetPerformanceTrends(
        [FromQuery] int days = 7,
        [FromQuery] string interval = "hour")
    {
        var intervalSpan = interval switch
        {
            "minute" => TimeSpan.FromMinutes(1),
            "hour" => TimeSpan.FromHours(1),
            "day" => TimeSpan.FromDays(1),
            _ => TimeSpan.FromHours(1)
        };

        var trends = await _analytics.GetTimeSeriesDataAsync(
            DateTime.UtcNow.AddDays(-days),
            DateTime.UtcNow,
            intervalSpan);

        return Ok(new
        {
            Period = $"Last {days} days",
            Interval = interval,
            DataPoints = trends.Count,
            Data = trends
        });
    }
}
```

## =' Advanced Configuration

### Custom Analytics Options

```csharp
builder.Services.AddAthenaCacheAnalytics(options =>
{
    // Storage configuration
    options.DatabasePath = "analytics/cache_data.db";
    options.RetentionDays = 90;  // Keep data for 90 days
    
    // Collection settings
    options.EnableEventCollection = true;
    options.CollectDetailedMetrics = true;
    options.SampleRate = 1.0;  // Collect 100% of events
    
    // Performance settings
    options.BatchSize = 1000;
    options.FlushIntervalSeconds = 30;
    options.MaxQueueSize = 10000;
    
    // Analysis settings
    options.HotKeyThreshold = 100;  // Keys accessed >100 times
    options.ColdKeyThreshold = 5;   // Keys accessed <5 times
    options.AnalysisIntervalMinutes = 15;
});
```

### Custom Event Collectors

```csharp
public class CustomCacheEventCollector : ICacheEventCollector
{
    public async Task CollectEventAsync(CacheEvent cacheEvent)
    {
        // Custom event collection logic
        // e.g., send to external analytics service
        await ExternalAnalyticsService.SendEventAsync(cacheEvent);
    }

    public async Task<IEnumerable<CacheEvent>> GetEventsAsync(
        DateTime startDate, 
        DateTime endDate)
    {
        // Retrieve events from custom source
        return await ExternalAnalyticsService.GetEventsAsync(startDate, endDate);
    }
}

// Register custom collector
builder.Services.AddSingleton<ICacheEventCollector, CustomCacheEventCollector>();
```

## =Ê Data Export and Reporting

### Export Analytics Data

```csharp
[ApiController]
public class ReportsController : ControllerBase
{
    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportToCsv(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate)
    {
        var events = await _analytics.GetEventsAsync(startDate, endDate);
        var csv = GenerateCsv(events);
        
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", "cache_analytics.csv");
    }

    [HttpGet("export/json")]
    public async Task<IActionResult> ExportToJson(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate)
    {
        var analytics = new
        {
            Statistics = await _analytics.GetStatisticsAsync(startDate, endDate),
            HotKeys = await _analytics.GetHotKeyAnalysisAsync(startDate, endDate, 100),
            UsagePatterns = await _analytics.GetUsagePatternAnalysisAsync(startDate, endDate),
            TimeSeries = await _analytics.GetTimeSeriesDataAsync(startDate, endDate, TimeSpan.FromHours(1))
        };

        return Ok(analytics);
    }
}
```

### Automated Reports

```csharp
public class AnalyticsReportService : BackgroundService
{
    private readonly ICacheAnalyticsService _analytics;
    private readonly IEmailService _emailService;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await GenerateAndSendDailyReport();
            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }

    private async Task GenerateAndSendDailyReport()
    {
        var yesterday = DateTime.UtcNow.AddDays(-1);
        var stats = await _analytics.GetStatisticsAsync(yesterday, DateTime.UtcNow);
        
        var report = $"""
            Daily Cache Analytics Report - {yesterday:yyyy-MM-dd}
            
            Performance Summary:
            - Hit Rate: {stats.HitRate:P2}
            - Total Requests: {stats.TotalRequests:N0}
            - Average Response Time: {stats.AverageResponseTime:F2}ms
            - Cache Size: {stats.CacheSize:N0} entries
            
            Top Issues:
            {await GenerateIssuesSummary(yesterday)}
            """;

        await _emailService.SendReportAsync("daily-cache-report@company.com", report);
    }
}
```

## <¯ Performance Optimization Insights

### Automatic Recommendations

```csharp
[ApiController]
public class OptimizationController : ControllerBase
{
    [HttpGet("recommendations")]
    public async Task<IActionResult> GetOptimizationRecommendations()
    {
        var patterns = await _analytics.GetUsagePatternAnalysisAsync(
            DateTime.UtcNow.AddDays(-7), 
            DateTime.UtcNow);
            
        var recommendations = new List<string>();

        // Analyze cold keys
        if (patterns.ColdKeys.Count > 100)
        {
            recommendations.Add($"Consider removing {patterns.ColdKeys.Count} cold keys to reduce memory usage");
        }

        // Analyze hot keys
        var hotKeysWithLowHitRate = patterns.HotKeys.Where(k => k.HitRate < 0.7).ToList();
        if (hotKeysWithLowHitRate.Any())
        {
            recommendations.Add($"Optimize {hotKeysWithLowHitRate.Count} hot keys with low hit rates");
        }

        // Analyze response times
        var slowEndpoints = patterns.AverageResponseTimes
            .Where(r => r.Value > 100)
            .OrderByDescending(r => r.Value)
            .Take(5);
            
        if (slowEndpoints.Any())
        {
            recommendations.Add($"Optimize slow endpoints: {string.Join(", ", slowEndpoints.Select(e => e.Key))}");
        }

        return Ok(new { Recommendations = recommendations });
    }
}
```

## = Integration with Other Packages

Works seamlessly with:
- **[Athena.Cache.Core](https://www.nuget.org/packages/Athena.Cache.Core/)**: Core caching functionality
- **[Athena.Cache.Redis](https://www.nuget.org/packages/Athena.Cache.Redis/)**: Distributed cache analytics
- **[Athena.Cache.Monitoring](https://www.nuget.org/packages/Athena.Cache.Monitoring/)**: Real-time monitoring
- **[Athena.Cache.SourceGenerator](https://www.nuget.org/packages/Athena.Cache.SourceGenerator/)**: Compile-time optimization

## =Ê Analytics Schema

### SQLite Database Structure

```sql
-- Cache events table
CREATE TABLE CacheEvents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp DATETIME NOT NULL,
    EventType TEXT NOT NULL,
    CacheKey TEXT NOT NULL,
    Endpoint TEXT,
    ResponseTime REAL,
    Success BOOLEAN,
    ErrorMessage TEXT
);

-- Aggregated statistics
CREATE TABLE CacheStatistics (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Date DATE NOT NULL,
    Hour INTEGER NOT NULL,
    HitCount INTEGER DEFAULT 0,
    MissCount INTEGER DEFAULT 0,
    TotalResponseTime REAL DEFAULT 0,
    RequestCount INTEGER DEFAULT 0
);
```

## =Ä License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/jhbrunoK/Athena.Cache/blob/main/LICENSE.txt) file for details.

## = Issues & Support

- **Repository**: [https://github.com/jhbrunoK/Athena.Cache](https://github.com/jhbrunoK/Athena.Cache)
- **Issues**: [https://github.com/jhbrunoK/Athena.Cache/issues](https://github.com/jhbrunoK/Athena.Cache/issues)
- **Documentation**: [https://github.com/jhbrunoK/Athena.Cache](https://github.com/jhbrunoK/Athena.Cache)