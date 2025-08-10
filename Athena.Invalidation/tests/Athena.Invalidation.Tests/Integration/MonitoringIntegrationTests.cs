using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Monitoring.Abstractions;
using Athena.Invalidation.Monitoring.Core;
using Athena.Invalidation.Monitoring.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Athena.Invalidation.Tests.Integration;

public class MonitoringIntegrationTests : IAsyncLifetime
{
    private ServiceProvider _serviceProvider = null!;
    private IInvalidationMetricsCollector _metricsCollector = null!;
    private MonitoredInvalidationEngine _monitoredEngine = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        
        // 로깅
        services.AddLogging(builder => builder.AddConsole());
        
        // 모니터링 옵션
        services.Configure<MonitoringOptions>(options =>
        {
            options.MeterName = "Test.Athena.Invalidation";
            options.RecentEventsSampleSize = 100;
            options.EnableDetailedMetrics = true;
        });
        
        // 메트릭 수집기
        services.AddSingleton<IInvalidationMetricsCollector, DefaultInvalidationMetricsCollector>();
        
        // Mock 엔진
        services.AddSingleton<IInvalidationEngine>(provider => 
            new MockInvalidationEngine("test-node", provider.GetRequiredService<ILogger<MockInvalidationEngine>>()));
        
        _serviceProvider = services.BuildServiceProvider();
        
        _metricsCollector = _serviceProvider.GetRequiredService<IInvalidationMetricsCollector>();
        await _metricsCollector.StartAsync();
        
        var innerEngine = _serviceProvider.GetRequiredService<IInvalidationEngine>();
        _monitoredEngine = new MonitoredInvalidationEngine(
            innerEngine,
            _metricsCollector,
            _serviceProvider.GetRequiredService<ILogger<MonitoredInvalidationEngine>>());
    }

    [Fact]
    public async Task InvalidateByTable_ShouldRecordMetrics()
    {
        // Arrange
        var tableName = "Users";
        
        // Act
        await _monitoredEngine.InvalidateByTableAsync(tableName);
        
        // Assert
        var metrics = await _metricsCollector.GetMetricsAsync();
        Assert.Equal(1, metrics.TotalInvalidations);
        Assert.Equal(1, metrics.SuccessfulInvalidations);
        Assert.Equal(0, metrics.FailedInvalidations);
        Assert.Equal(1.0, metrics.InvalidationSuccessRate);
        Assert.True(metrics.InvalidationsByType.ContainsKey(InvalidationType.Table));
        Assert.Equal(1, metrics.InvalidationsByType[InvalidationType.Table]);
    }

    [Fact]
    public async Task MultipleInvalidations_ShouldAccumulateMetrics()
    {
        // Act
        await _monitoredEngine.InvalidateByTableAsync("Users");
        await _monitoredEngine.InvalidateByPatternAsync("user:*");
        await _monitoredEngine.InvalidateByKeyAsync("user:123");
        await _monitoredEngine.InvalidateBatchAsync(new[] { "Orders", "Products" });
        
        // Assert
        var metrics = await _metricsCollector.GetMetricsAsync();
        Assert.Equal(4, metrics.TotalInvalidations); // 배치는 1개 작업으로 카운트
        Assert.Equal(4, metrics.SuccessfulInvalidations);
        Assert.Equal(1.0, metrics.InvalidationSuccessRate);
        
        // 타입별 통계 확인
        Assert.Equal(1, metrics.InvalidationsByType[InvalidationType.Table]);
        Assert.Equal(1, metrics.InvalidationsByType[InvalidationType.Pattern]);
        Assert.Equal(1, metrics.InvalidationsByType[InvalidationType.Key]);
        Assert.Equal(1, metrics.InvalidationsByType[InvalidationType.Batch]);
    }

    [Fact]
    public async Task FailedInvalidation_ShouldRecordFailureMetrics()
    {
        // Arrange
        var faultyEngine = new FaultyMockInvalidationEngine();
        var monitoredFaultyEngine = new MonitoredInvalidationEngine(
            faultyEngine,
            _metricsCollector,
            _serviceProvider.GetRequiredService<ILogger<MonitoredInvalidationEngine>>());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => monitoredFaultyEngine.InvalidateByTableAsync("FaultyTable"));
        
        var metrics = await _metricsCollector.GetMetricsAsync();
        Assert.True(metrics.FailedInvalidations > 0);
        Assert.True(metrics.InvalidationSuccessRate < 1.0);
    }

    [Fact]
    public async Task CacheAccess_ShouldRecordMetrics()
    {
        // Act
        _metricsCollector.RecordCacheAccess("TestCache", true);  // hit
        _metricsCollector.RecordCacheAccess("TestCache", false); // miss
        _metricsCollector.RecordCacheAccess("TestCache", true);  // hit
        
        // Assert
        var metrics = await _metricsCollector.GetMetricsAsync();
        Assert.Equal(3, metrics.TotalCacheAccesses);
        Assert.Equal(2, metrics.CacheHits);
        Assert.Equal(1, metrics.CacheMisses);
        Assert.Equal(2.0/3.0, metrics.CacheHitRatio, 2);
    }

    [Fact]
    public async Task DistributedEvent_ShouldRecordMetrics()
    {
        // Act
        _metricsCollector.RecordDistributedEvent("TableInvalidationEvent", "node1", TimeSpan.FromMilliseconds(50), true);
        _metricsCollector.RecordDistributedEvent("PatternInvalidationEvent", "node2", TimeSpan.FromMilliseconds(75), true);
        _metricsCollector.RecordDistributedEvent("BatchInvalidationEvent", "node1", TimeSpan.FromMilliseconds(100), false);
        
        // Assert
        var metrics = await _metricsCollector.GetMetricsAsync();
        Assert.Equal(3, metrics.TotalDistributedEvents);
        Assert.Equal(2, metrics.SuccessfulDistributedEvents);
        Assert.Equal(1, metrics.FailedDistributedEvents);
        
        Assert.Equal(2, metrics.DistributedEventsByType["TableInvalidationEvent"] + 
                        metrics.DistributedEventsByType.GetValueOrDefault("PatternInvalidationEvent", 0));
        Assert.Equal(2, metrics.DistributedEventsByNode["node1"] + 
                        metrics.DistributedEventsByNode.GetValueOrDefault("node2", 0));
    }

    [Fact]
    public async Task MetricsReset_ShouldClearAllCounters()
    {
        // Arrange
        await _monitoredEngine.InvalidateByTableAsync("Users");
        _metricsCollector.RecordCacheAccess("TestCache", true);
        _metricsCollector.RecordDistributedEvent("TestEvent", "node1", TimeSpan.FromMilliseconds(50), true);
        
        var metricsBeforeReset = await _metricsCollector.GetMetricsAsync();
        Assert.True(metricsBeforeReset.TotalInvalidations > 0);
        
        // Act
        await _metricsCollector.ResetMetricsAsync();
        
        // Assert
        var metricsAfterReset = await _metricsCollector.GetMetricsAsync();
        Assert.Equal(0, metricsAfterReset.TotalInvalidations);
        Assert.Equal(0, metricsAfterReset.TotalCacheAccesses);
        Assert.Equal(0, metricsAfterReset.TotalDistributedEvents);
        Assert.Empty(metricsAfterReset.InvalidationsByType);
        Assert.Empty(metricsAfterReset.DistributedEventsByType);
    }

    [Fact]
    public async Task AverageResponseTime_ShouldBeCalculatedCorrectly()
    {
        // Act - 다양한 처리 시간을 시뮬레이션하기 위해 delay 추가
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await Task.Delay(i * 10); // 0, 10, 20, ... 90ms 지연
                await _monitoredEngine.InvalidateByTableAsync($"Table{i}");
            }));
        }
        await Task.WhenAll(tasks);
        
        // Assert
        var metrics = await _metricsCollector.GetMetricsAsync();
        Assert.Equal(10, metrics.TotalInvalidations);
        Assert.True(metrics.AverageInvalidationTime.TotalMilliseconds > 0);
        
        // 평균 시간이 합리적인 범위에 있는지 확인
        Assert.True(metrics.AverageInvalidationTime.TotalMilliseconds < 1000); // 1초 미만
    }

    public async Task DisposeAsync()
    {
        if (_monitoredEngine != null)
            await _monitoredEngine.DisposeAsync();
        if (_metricsCollector != null)
            await _metricsCollector.StopAsync();
        _serviceProvider?.Dispose();
    }
}

/// <summary>
/// 항상 실패하는 테스트용 Mock 엔진
/// </summary>
public class FaultyMockInvalidationEngine : IInvalidationEngine
{
    public Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException($"Simulated failure for table {tableName}");
    }

    public Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException($"Simulated failure for pattern {pattern}");
    }

    public Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException($"Simulated failure for key {key}");
    }

    public Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated batch failure");
    }

    public Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException($"Simulated hierarchy failure for {tableName}");
    }

    // 나머지 메서드들은 기본 구현
    public Task InvalidateOnCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : class
        => Task.CompletedTask;

    public Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : class
        => Task.CompletedTask;

    public Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default) where TReadModel : class
        => Task.CompletedTask;

    public Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default) where TProjection : class
        => Task.CompletedTask;

    public Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default)
        => Task.FromResult(Enumerable.Empty<string>());

    public Task RegisterInvalidationRuleAsync(IInvalidationRule rule, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task UnregisterInvalidationRuleAsync(string ruleId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IEnumerable<IInvalidationRule>> GetInvalidationRulesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Enumerable.Empty<IInvalidationRule>());

    public IInvalidationContext CreateContext(InvalidationTrigger trigger, object? metadata = null)
        => new MockInvalidationContext();

    public Task ClearAllAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new InvalidationEngineStatus
        {
            IsHealthy = false,
            Uptime = TimeSpan.Zero,
            TrackedKeysCount = 0,
            RegisteredRulesCount = 0,
            Metrics = new Dictionary<string, object>
            {
                ["IsHealthy"] = false,
                ["Error"] = "This is a faulty engine for testing"
            }
        });
    }
}