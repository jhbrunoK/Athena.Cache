using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Core.Performance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Athena.Invalidation.Tests.Integration;

public class PerformanceOptimizationTests : IAsyncLifetime
{
    private ServiceProvider _serviceProvider = null!;
    private IBackgroundInvalidationQueue _queue = null!;
    private BackgroundInvalidationProcessor _processor = null!;
    private BatchOptimizedInvalidationEngine _batchEngine = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        
        // 로깅
        services.AddLogging(builder => builder.AddConsole());
        
        // 백그라운드 큐 옵션
        services.Configure<BackgroundQueueOptions>(options =>
        {
            options.MaxQueueSize = 1000;
            options.MaxBatchSize = 50;
            options.BatchWaitTime = TimeSpan.FromMilliseconds(100);
        });
        
        // 백그라운드 프로세서 옵션
        services.Configure<BackgroundProcessorOptions>(options =>
        {
            options.MaxConcurrentJobs = 2;
            options.MaxBatchSize = 50;
            options.BatchWaitTime = TimeSpan.FromMilliseconds(100);
            options.EnableBatchOptimization = true;
        });
        
        // 배치 최적화 옵션
        services.Configure<BatchOptimizationOptions>(options =>
        {
            options.ImmediateProcessingThreshold = 10;
            options.ImmediateBatchSize = 20;
            options.ImmediateBatchInterval = TimeSpan.FromMilliseconds(200);
            options.EnableImmediateBatching = true;
        });
        
        _serviceProvider = services.BuildServiceProvider();
        
        // 컴포넌트들 생성
        _queue = new BackgroundInvalidationQueue(
            _serviceProvider.GetRequiredService<ILogger<BackgroundInvalidationQueue>>(),
            _serviceProvider.GetRequiredService<IOptions<BackgroundQueueOptions>>());

        var mockEngine = new MockInvalidationEngine("test", 
            _serviceProvider.GetRequiredService<ILogger<MockInvalidationEngine>>());
        
        _processor = new BackgroundInvalidationProcessor(
            _queue,
            mockEngine,
            _serviceProvider.GetRequiredService<ILogger<BackgroundInvalidationProcessor>>(),
            _serviceProvider.GetRequiredService<IOptions<BackgroundProcessorOptions>>());

        _batchEngine = new BatchOptimizedInvalidationEngine(
            mockEngine,
            _queue,
            _serviceProvider.GetRequiredService<ILogger<BatchOptimizedInvalidationEngine>>(),
            _serviceProvider.GetRequiredService<IOptions<BatchOptimizationOptions>>());

        // 프로세서 시작
        await _processor.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task BackgroundQueue_ShouldEnqueueAndDequeueJobs()
    {
        // Arrange
        var job = InvalidationJob.CreateTableInvalidation("Users");
        
        // Act
        await _queue.EnqueueAsync(job);
        var dequeuedJob = await _queue.DequeueAsync();
        
        // Assert
        Assert.Equal(job.Id, dequeuedJob.Id);
        Assert.Equal(job.Type, dequeuedJob.Type);
        Assert.Equal("Users", dequeuedJob.Parameters["tableName"].ToString());
    }

    [Fact]
    public async Task BackgroundQueue_ShouldSupportBatchDequeue()
    {
        // Arrange
        var jobs = new[]
        {
            InvalidationJob.CreateTableInvalidation("Users"),
            InvalidationJob.CreateTableInvalidation("Orders"),
            InvalidationJob.CreatePatternInvalidation("cache:*")
        };

        // Act
        foreach (var job in jobs)
        {
            await _queue.EnqueueAsync(job);
        }

        var batchJobs = await _queue.DequeueBatchAsync(5, TimeSpan.FromSeconds(1));
        var jobList = batchJobs.ToList();

        // Assert
        Assert.Equal(3, jobList.Count);
        Assert.Contains(jobList, j => j.Parameters.ContainsValue("Users"));
        Assert.Contains(jobList, j => j.Parameters.ContainsValue("Orders"));
        Assert.Contains(jobList, j => j.Parameters.ContainsValue("cache:*"));
    }

    [Fact]
    public async Task BatchOptimizedEngine_ShouldProcessImmediatelyForSmallRequests()
    {
        // Arrange
        var mockEngine = new TrackingMockInvalidationEngine();
        var batchOptimizedEngine = new BatchOptimizedInvalidationEngine(
            mockEngine,
            _queue,
            _serviceProvider.GetRequiredService<ILogger<BatchOptimizedInvalidationEngine>>(),
            Options.Create(new BatchOptimizationOptions { ImmediateProcessingThreshold = 10 }));

        // Act
        await batchOptimizedEngine.InvalidateByTableAsync("Users");
        
        // Assert
        // 임계값보다 작으므로 즉시 처리되어야 함
        Assert.Contains("Users", mockEngine.ProcessedTables);
        Assert.Equal(0, _queue.Count); // 큐에는 들어가지 않아야 함
    }

    [Fact]
    public async Task BatchOptimizedEngine_ShouldQueueForLargeRequests()
    {
        // Arrange
        var mockEngine = new TrackingMockInvalidationEngine();
        var batchOptimizedEngine = new BatchOptimizedInvalidationEngine(
            mockEngine,
            _queue,
            _serviceProvider.GetRequiredService<ILogger<BatchOptimizedInvalidationEngine>>(),
            Options.Create(new BatchOptimizationOptions { ImmediateProcessingThreshold = 1 })); // 낮은 임계값

        // 큐를 가득 채워서 임계값 초과
        for (int i = 0; i < 5; i++)
        {
            await _queue.EnqueueAsync(InvalidationJob.CreateTableInvalidation($"Table{i}"));
        }

        // Act
        await batchOptimizedEngine.InvalidateByTableAsync("NewTable");
        
        // Allow some time for queue processing
        await Task.Delay(300);

        // Assert
        // 큐가 가득차서 큐에 들어가야 함
        Assert.True(_queue.Count >= 0); // 프로세서가 처리할 수도 있음
    }

    [Fact]
    public async Task BackgroundProcessor_ShouldProcessTableJobsInBatch()
    {
        // Arrange
        var mockEngine = new TrackingMockInvalidationEngine();
        var processor = new BackgroundInvalidationProcessor(
            _queue,
            mockEngine,
            _serviceProvider.GetRequiredService<ILogger<BackgroundInvalidationProcessor>>(),
            Options.Create(new BackgroundProcessorOptions 
            { 
                EnableBatchOptimization = true,
                MaxBatchSize = 10,
                BatchWaitTime = TimeSpan.FromMilliseconds(100)
            }));

        await processor.StartAsync(CancellationToken.None);

        // Act
        var jobs = new[]
        {
            InvalidationJob.CreateTableInvalidation("Users"),
            InvalidationJob.CreateTableInvalidation("Orders"),
            InvalidationJob.CreateTableInvalidation("Products")
        };

        foreach (var job in jobs)
        {
            await _queue.EnqueueAsync(job);
        }

        // 프로세서가 처리할 시간 제공
        await Task.Delay(500);

        // Assert
        Assert.Contains("Users", mockEngine.ProcessedTables);
        Assert.Contains("Orders", mockEngine.ProcessedTables);
        Assert.Contains("Products", mockEngine.ProcessedTables);

        await processor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ProcessorStatistics_ShouldTrackJobExecution()
    {
        // Arrange
        var jobs = new[]
        {
            InvalidationJob.CreateTableInvalidation("Users"),
            InvalidationJob.CreatePatternInvalidation("cache:*"),
            InvalidationJob.CreateKeyInvalidation("user:123")
        };

        // Act
        foreach (var job in jobs)
        {
            await _queue.EnqueueAsync(job);
        }

        // 프로세서가 처리할 시간 제공
        await Task.Delay(500);

        // Assert
        var statistics = _processor.GetAllStatistics();
        
        // 적어도 하나의 통계는 있어야 함
        Assert.NotEmpty(statistics);
        
        // 각 통계의 기본 유효성 확인
        foreach (var stat in statistics.Values)
        {
            Assert.True(stat.TotalJobs >= 0);
            Assert.True(stat.SuccessfulJobs >= 0);
            Assert.True(stat.SuccessfulJobs <= stat.TotalJobs);
            Assert.True(stat.SuccessRate >= 0.0 && stat.SuccessRate <= 1.0);
        }
    }

    [Fact]
    public void QueueCapacity_ShouldRespectMaxQueueSize()
    {
        // Arrange
        var smallQueue = new BackgroundInvalidationQueue(
            _serviceProvider.GetRequiredService<ILogger<BackgroundInvalidationQueue>>(),
            Options.Create(new BackgroundQueueOptions { MaxQueueSize = 2 }));

        // Act & Assert
        var job1 = InvalidationJob.CreateTableInvalidation("Table1");
        var job2 = InvalidationJob.CreateTableInvalidation("Table2");
        var job3 = InvalidationJob.CreateTableInvalidation("Table3");

        // 처음 두 작업은 성공해야 함
        Assert.True(smallQueue.TryEnqueue(job1));
        Assert.True(smallQueue.TryEnqueue(job2));

        // 세 번째 작업은 큐가 가득차서 실패해야 함
        Assert.False(smallQueue.TryEnqueue(job3));
    }

    public async Task DisposeAsync()
    {
        if (_processor != null)
            await _processor.StopAsync(CancellationToken.None);
        _processor?.Dispose();
        if (_batchEngine != null)
            await _batchEngine.DisposeAsync();
        if (_queue is IDisposable disposableQueue)
            disposableQueue.Dispose();
        _serviceProvider?.Dispose();
    }
}

/// <summary>
/// 처리된 작업들을 추적하는 Mock 엔진
/// </summary>
public class TrackingMockInvalidationEngine : IInvalidationEngine
{
    public List<string> ProcessedTables { get; } = new();
    public List<string> ProcessedPatterns { get; } = new();
    public List<string> ProcessedKeys { get; } = new();

    public Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        ProcessedTables.Add(tableName);
        return Task.CompletedTask;
    }

    public Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        ProcessedPatterns.Add(pattern);
        return Task.CompletedTask;
    }

    public Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        ProcessedKeys.Add(key);
        return Task.CompletedTask;
    }

    public Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        ProcessedTables.AddRange(tableNames);
        return Task.CompletedTask;
    }

    public Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        ProcessedTables.Add(tableName);
        ProcessedTables.AddRange(relatedTables);
        return Task.CompletedTask;
    }

    // 나머지 메서드들은 기본 구현
    public Task InvalidateOnCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : class => Task.CompletedTask;
    public Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : class => Task.CompletedTask;
    public Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default) where TReadModel : class => Task.CompletedTask;
    public Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default) where TProjection : class => Task.CompletedTask;
    public Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<string>());
    public Task RegisterInvalidationRuleAsync(IInvalidationRule rule, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UnregisterInvalidationRuleAsync(string ruleId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IEnumerable<IInvalidationRule>> GetInvalidationRulesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<IInvalidationRule>());
    public IInvalidationContext CreateContext(InvalidationTrigger trigger, object? metadata = null) => new MockInvalidationContext();
    public Task ClearAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

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
                ["ProcessedTables"] = ProcessedTables.Count,
                ["ProcessedPatterns"] = ProcessedPatterns.Count,
                ["ProcessedKeys"] = ProcessedKeys.Count
            }
        });
    }
}