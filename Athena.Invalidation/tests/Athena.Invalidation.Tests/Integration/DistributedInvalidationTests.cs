using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Distributed.Abstractions;
using Athena.Invalidation.Distributed.Core;
using Athena.Invalidation.Distributed.Implementations;
using Athena.Invalidation.Distributed.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace Athena.Invalidation.Tests.Integration;

public class DistributedInvalidationTests : IAsyncLifetime
{
    private ServiceProvider _serviceProvider = null!;
    private IConnectionMultiplexer _redis = null!;
    private IInvalidationEngine _localEngine1 = null!;
    private IInvalidationEngine _localEngine2 = null!;
    private DistributedInvalidationEngine _distributedEngine1 = null!;
    private DistributedInvalidationEngine _distributedEngine2 = null!;

    public async Task InitializeAsync()
    {
        // Redis 연결
        _redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");

        var services = new ServiceCollection();
        
        // 로깅
        services.AddLogging(builder => builder.AddConsole());
        
        // Redis 등록
        services.AddSingleton(_redis);
        
        // 로컬 무효화 엔진들 (테스트용 Mock)
        services.AddSingleton<IInvalidationEngine>(provider => 
            new MockInvalidationEngine("node1", provider.GetRequiredService<ILogger<MockInvalidationEngine>>()));
        services.AddSingleton<IInvalidationEngine>(provider => 
            new MockInvalidationEngine("node2", provider.GetRequiredService<ILogger<MockInvalidationEngine>>()));
        
        // Redis 분산 이벤트 버스
        services.Configure<RedisEventBusOptions>(options =>
        {
            options.NodeId = "node1";
            options.ChannelPrefix = "test-athena-invalidation";
            options.LogEvents = true;
        });
        services.AddSingleton<IDistributedEventBus, RedisDistributedEventBus>();
        
        // 분산 무효화 옵션
        services.Configure<DistributedInvalidationOptions>(options =>
        {
            options.NodeId = "node1";
            options.PublishEvents = true;
            options.PublishCQRSEvents = true;
        });

        _serviceProvider = services.BuildServiceProvider();
        
        // 엔진들 초기화
        _localEngine1 = new MockInvalidationEngine("node1", 
            _serviceProvider.GetRequiredService<ILogger<MockInvalidationEngine>>());
        _localEngine2 = new MockInvalidationEngine("node2", 
            _serviceProvider.GetRequiredService<ILogger<MockInvalidationEngine>>());
        
        var eventBus1 = CreateEventBus("node1");
        var eventBus2 = CreateEventBus("node2");
        
        _distributedEngine1 = new DistributedInvalidationEngine(
            _localEngine1,
            eventBus1,
            _serviceProvider.GetRequiredService<ILogger<DistributedInvalidationEngine>>(),
            Options.Create(new DistributedInvalidationOptions { NodeId = "node1", PublishEvents = true }));
            
        _distributedEngine2 = new DistributedInvalidationEngine(
            _localEngine2,
            eventBus2,
            _serviceProvider.GetRequiredService<ILogger<DistributedInvalidationEngine>>(),
            Options.Create(new DistributedInvalidationOptions { NodeId = "node2", PublishEvents = true }));

        // 이벤트 버스 시작
        await eventBus1.StartAsync();
        await eventBus2.StartAsync();
    }

    private RedisDistributedEventBus CreateEventBus(string nodeId)
    {
        return new RedisDistributedEventBus(
            _redis,
            _serviceProvider.GetRequiredService<ILogger<RedisDistributedEventBus>>(),
            Options.Create(new RedisEventBusOptions { NodeId = nodeId, ChannelPrefix = "test-athena-invalidation", LogEvents = true }));
    }

    [Fact]
    public async Task TableInvalidation_ShouldPropagateAcrossNodes()
    {
        // Arrange
        var mockEngine2 = (MockInvalidationEngine)_localEngine2;
        var tableName = "Users";

        // Act
        await _distributedEngine1.InvalidateByTableAsync(tableName);
        
        // 분산 이벤트 전파를 위한 대기
        await Task.Delay(500);

        // Assert
        Assert.Contains(tableName, mockEngine2.InvalidatedTables);
    }

    [Fact]
    public async Task PatternInvalidation_ShouldPropagateAcrossNodes()
    {
        // Arrange
        var mockEngine2 = (MockInvalidationEngine)_localEngine2;
        var pattern = "user:*";

        // Act
        await _distributedEngine1.InvalidateByPatternAsync(pattern);
        
        // 분산 이벤트 전파를 위한 대기
        await Task.Delay(500);

        // Assert
        Assert.Contains(pattern, mockEngine2.InvalidatedPatterns);
    }

    [Fact]
    public async Task BatchInvalidation_ShouldPropagateAcrossNodes()
    {
        // Arrange
        var mockEngine2 = (MockInvalidationEngine)_localEngine2;
        var tableNames = new[] { "Users", "Orders", "Products" };

        // Act
        await _distributedEngine1.InvalidateBatchAsync(tableNames);
        
        // 분산 이벤트 전파를 위한 대기
        await Task.Delay(500);

        // Assert
        foreach (var tableName in tableNames)
        {
            Assert.Contains(tableName, mockEngine2.InvalidatedTables);
        }
    }

    [Fact]
    public async Task HierarchicalInvalidation_ShouldPropagateAcrossNodes()
    {
        // Arrange
        var mockEngine2 = (MockInvalidationEngine)_localEngine2;
        var rootTable = "Orders";
        var relatedTables = new[] { "OrderItems", "Payments" };

        // Act
        await _distributedEngine1.InvalidateHierarchyAsync(rootTable, relatedTables, 2);
        
        // 분산 이벤트 전파를 위한 대기
        await Task.Delay(500);

        // Assert
        Assert.Contains(rootTable, mockEngine2.InvalidatedHierarchies.Keys);
        var hierarchy = mockEngine2.InvalidatedHierarchies[rootTable];
        Assert.Equal(relatedTables, hierarchy.RelatedTables);
        Assert.Equal(2, hierarchy.MaxDepth);
    }

    [Fact]
    public async Task LoopbackPrevention_ShouldNotInvalidateOriginatingNode()
    {
        // Arrange
        var mockEngine1 = (MockInvalidationEngine)_localEngine1;
        var mockEngine2 = (MockInvalidationEngine)_localEngine2;
        var tableName = "LoopbackTest";

        // 초기 상태 확인
        mockEngine1.InvalidatedTables.Clear();
        mockEngine2.InvalidatedTables.Clear();

        // Act
        await _distributedEngine1.InvalidateByTableAsync(tableName);
        
        // 분산 이벤트 전파를 위한 대기
        await Task.Delay(500);

        // Assert
        // Node1에서 발행한 이벤트가 Node2로만 전파되어야 함
        Assert.Contains(tableName, mockEngine1.InvalidatedTables); // 로컬 무효화
        Assert.Contains(tableName, mockEngine2.InvalidatedTables); // 분산 전파
        
        // Node1에서 다시 처리되지 않아야 함 (루프백 방지)
        Assert.Equal(1, mockEngine1.InvalidatedTables.Count(t => t == tableName));
    }

    [Fact]
    public async Task EventBusStatistics_ShouldTrackPublishedAndConsumedEvents()
    {
        // Arrange
        var eventBus1 = CreateEventBus("test-node1");
        await eventBus1.StartAsync();

        // Act
        await _distributedEngine1.InvalidateByTableAsync("StatisticsTest");
        await Task.Delay(500);

        var statistics = await eventBus1.GetStatisticsAsync();

        // Assert
        Assert.True(statistics.PublishedEvents > 0);
        Assert.True(statistics.IsConnected);

        await eventBus1.StopAsync();
    }

    public async Task DisposeAsync()
    {
        await _distributedEngine1?.DisposeAsync();
        await _distributedEngine2?.DisposeAsync();
        _redis?.Dispose();
        _serviceProvider?.Dispose();
    }
}

/// <summary>
/// 테스트용 Mock 무효화 엔진
/// </summary>
public class MockInvalidationEngine : IInvalidationEngine
{
    private readonly string _nodeId;
    private readonly ILogger<MockInvalidationEngine> _logger;
    
    public List<string> InvalidatedTables { get; } = new();
    public List<string> InvalidatedPatterns { get; } = new();
    public List<string> InvalidatedKeys { get; } = new();
    public Dictionary<string, (string[] RelatedTables, int MaxDepth)> InvalidatedHierarchies { get; } = new();

    public MockInvalidationEngine(string nodeId, ILogger<MockInvalidationEngine> logger)
    {
        _nodeId = nodeId;
        _logger = logger;
    }

    public Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        InvalidatedTables.Add(tableName);
        _logger.LogInformation("Node {NodeId}: Invalidated table {TableName}", _nodeId, tableName);
        return Task.CompletedTask;
    }

    public Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        InvalidatedPatterns.Add(pattern);
        _logger.LogInformation("Node {NodeId}: Invalidated pattern {Pattern}", _nodeId, pattern);
        return Task.CompletedTask;
    }

    public Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        InvalidatedKeys.Add(key);
        _logger.LogInformation("Node {NodeId}: Invalidated key {Key}", _nodeId, key);
        return Task.CompletedTask;
    }

    public Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        var tables = tableNames.ToArray();
        InvalidatedTables.AddRange(tables);
        _logger.LogInformation("Node {NodeId}: Invalidated batch {Tables}", _nodeId, string.Join(", ", tables));
        return Task.CompletedTask;
    }

    public Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        InvalidatedHierarchies[tableName] = (relatedTables, maxDepth);
        _logger.LogInformation("Node {NodeId}: Invalidated hierarchy {TableName} with {RelatedCount} related tables", 
            _nodeId, tableName, relatedTables.Length);
        return Task.CompletedTask;
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
    {
        InvalidatedTables.Clear();
        InvalidatedPatterns.Clear(); 
        InvalidatedKeys.Clear();
        InvalidatedHierarchies.Clear();
        return Task.CompletedTask;
    }

    public Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new InvalidationEngineStatus
        {
            IsHealthy = true,
            Uptime = TimeSpan.FromMinutes(1),
            TrackedKeysCount = 0,
            RegisteredRulesCount = 0,
            LastActivity = DateTimeOffset.UtcNow,
            Metrics = new Dictionary<string, object>
            {
                ["NodeId"] = _nodeId,
                ["InvalidatedTables"] = InvalidatedTables.Count,
                ["InvalidatedPatterns"] = InvalidatedPatterns.Count,
                ["InvalidatedKeys"] = InvalidatedKeys.Count
            }
        });
    }
}

/// <summary>
/// 테스트용 Mock 무효화 컨텍스트
/// </summary>
public class MockInvalidationContext : IInvalidationContext
{
    public InvalidationTrigger Trigger { get; set; } = InvalidationTrigger.Manual;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}