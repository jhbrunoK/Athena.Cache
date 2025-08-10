using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Core.Performance;
using Athena.Invalidation.Distributed.Core;
using Athena.Invalidation.Distributed.Implementations;
using Athena.Invalidation.Monitoring.Decorators;
using Athena.Invalidation.Monitoring.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Athena.Invalidation.Tests.Benchmarks;

/// <summary>
/// Phase 3 기능들의 성능 벤치마크
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class Phase3PerformanceBenchmarks
{
    private IInvalidationEngine _baseEngine = null!;
    private BatchOptimizedInvalidationEngine _batchEngine = null!;
    private MonitoredInvalidationEngine _monitoredEngine = null!;
    private DistributedInvalidationEngine _distributedEngine = null!;
    private IBackgroundInvalidationQueue _queue = null!;
    private IServiceProvider _serviceProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        
        // 기본 Mock 엔진
        _baseEngine = new BenchmarkMockInvalidationEngine();
        
        // 백그라운드 큐
        services.Configure<BackgroundQueueOptions>(options =>
        {
            options.MaxQueueSize = 10000;
            options.MaxBatchSize = 100;
        });
        
        // 배치 최적화 옵션
        services.Configure<BatchOptimizationOptions>(options =>
        {
            options.ImmediateProcessingThreshold = 50;
            options.ImmediateBatchSize = 25;
            options.EnableImmediateBatching = true;
        });
        
        // 모니터링 옵션
        services.Configure<MonitoringOptions>(options =>
        {
            options.RecentEventsSampleSize = 1000;
            options.EnableDetailedMetrics = true;
        });
        
        // 분산 옵션
        services.Configure<DistributedInvalidationOptions>(options =>
        {
            options.NodeId = "benchmark-node";
            options.PublishEvents = false; // 벤치마크에서는 비활성화
        });

        _serviceProvider = services.BuildServiceProvider();
        
        // 큐 초기화
        _queue = new BackgroundInvalidationQueue(
            _serviceProvider.GetRequiredService<ILogger<BackgroundInvalidationQueue>>(),
            _serviceProvider.GetRequiredService<IOptions<BackgroundQueueOptions>>());
        
        // 배치 최적화 엔진
        _batchEngine = new BatchOptimizedInvalidationEngine(
            _baseEngine,
            _queue,
            _serviceProvider.GetRequiredService<ILogger<BatchOptimizedInvalidationEngine>>(),
            _serviceProvider.GetRequiredService<IOptions<BatchOptimizationOptions>>());
        
        // 모니터링 엔진
        var metricsCollector = new DefaultInvalidationMetricsCollector(
            _serviceProvider.GetRequiredService<ILogger<DefaultInvalidationMetricsCollector>>(),
            _serviceProvider.GetRequiredService<IOptions<MonitoringOptions>>());
        metricsCollector.StartAsync().Wait();
        
        _monitoredEngine = new MonitoredInvalidationEngine(
            _baseEngine,
            metricsCollector,
            _serviceProvider.GetRequiredService<ILogger<MonitoredInvalidationEngine>>());
        
        // 분산 엔진 (실제 이벤트 버스 없이) - 임시 비활성화
        // var mockEventBus = new MockDistributedEventBus();
        // _distributedEngine = new DistributedInvalidationEngine(
        //     _baseEngine,
        //     mockEventBus,
        //     _serviceProvider.GetRequiredService<ILogger<DistributedInvalidationEngine>>(),
        //     _serviceProvider.GetRequiredService<IOptions<DistributedInvalidationOptions>>());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _batchEngine?.DisposeAsync().AsTask().Wait();
        _monitoredEngine?.DisposeAsync().AsTask().Wait();
        _distributedEngine?.DisposeAsync().AsTask().Wait();
        if (_queue is IDisposable disposableQueue) 
            disposableQueue.Dispose();
        if (_serviceProvider is IDisposable disposableProvider)
            disposableProvider.Dispose();
    }

    [Benchmark(Baseline = true)]
    public async Task BaseEngine_SingleTableInvalidation()
    {
        await _baseEngine.InvalidateByTableAsync("Users");
    }

    [Benchmark]
    public async Task BatchEngine_SingleTableInvalidation()
    {
        await _batchEngine.InvalidateByTableAsync("Users");
    }

    [Benchmark]
    public async Task MonitoredEngine_SingleTableInvalidation()
    {
        await _monitoredEngine.InvalidateByTableAsync("Users");
    }

    // [Benchmark] - Temporarily disabled until DistributedEngine dependencies are available
    public async Task DistributedEngine_SingleTableInvalidation()
    {
        // await _distributedEngine.InvalidateByTableAsync("Users");
        await Task.CompletedTask;
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    [Arguments(1000)]
    public async Task BaseEngine_BatchInvalidation(int tableCount)
    {
        var tableNames = GenerateTableNames(tableCount);
        await _baseEngine.InvalidateBatchAsync(tableNames);
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    [Arguments(1000)]
    public async Task BatchEngine_BatchInvalidation(int tableCount)
    {
        var tableNames = GenerateTableNames(tableCount);
        await _batchEngine.InvalidateBatchAsync(tableNames);
    }

    // [Benchmark] - Temporarily disabled until DistributedEngine dependencies are available
    [Arguments(10)]
    [Arguments(100)]
    [Arguments(1000)]
    public async Task DistributedEngine_BatchInvalidation(int tableCount)
    {
        // var tableNames = GenerateTableNames(tableCount);
        // await _distributedEngine.InvalidateBatchAsync(tableNames);
        await Task.CompletedTask;
    }

    [Benchmark]
    [Arguments(1000)]
    [Arguments(5000)]
    [Arguments(10000)]
    public async Task BackgroundQueue_EnqueueDequeue(int operationCount)
    {
        // Enqueue
        var jobs = new InvalidationJob[operationCount];
        for (int i = 0; i < operationCount; i++)
        {
            jobs[i] = InvalidationJob.CreateTableInvalidation($"Table{i}");
            await _queue.EnqueueAsync(jobs[i]);
        }

        // Dequeue
        for (int i = 0; i < operationCount; i++)
        {
            await _queue.DequeueAsync();
        }
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(500)]
    [Arguments(1000)]
    public async Task BackgroundQueue_BatchDequeue(int jobCount)
    {
        // Enqueue jobs
        for (int i = 0; i < jobCount; i++)
        {
            var job = InvalidationJob.CreateTableInvalidation($"Table{i}");
            await _queue.EnqueueAsync(job);
        }

        // Batch dequeue
        while (!_queue.IsEmpty)
        {
            await _queue.DequeueBatchAsync(50, TimeSpan.FromMilliseconds(10));
        }
    }

    [Benchmark]
    [Arguments(1000)]
    [Arguments(5000)]
    public async Task MonitoredEngine_ConcurrentInvalidations(int concurrentCount)
    {
        var tasks = new Task[concurrentCount];
        for (int i = 0; i < concurrentCount; i++)
        {
            int tableIndex = i;
            tasks[i] = _monitoredEngine.InvalidateByTableAsync($"Table{tableIndex}");
        }
        
        await Task.WhenAll(tasks);
    }

    // [Benchmark] - Temporarily disabled until DistributedEngine dependencies are available
    [Arguments(100)]
    [Arguments(500)]
    [Arguments(1000)]
    public async Task DistributedEngine_MixedOperations(int operationCount)
    {
        // var tasks = new Task[operationCount];
        // for (int i = 0; i < operationCount; i++)
        // {
        //     int index = i;
        //     tasks[i] = (index % 4) switch
        //     {
        //         0 => _distributedEngine.InvalidateByTableAsync($"Table{index}"),
        //         1 => _distributedEngine.InvalidateByPatternAsync($"pattern:{index}:*"),
        //         2 => _distributedEngine.InvalidateByKeyAsync($"key:{index}"),
        //         _ => _distributedEngine.InvalidateBatchAsync(new[] { $"Batch{index}_1", $"Batch{index}_2" })
        //     };
        // }
        // 
        // await Task.WhenAll(tasks);
        await Task.CompletedTask;
    }

    // [Benchmark] - Temporarily disabled until all dependencies are available
    public async Task CompleteStack_Integration()
    {
        // 전체 스택을 통합한 벤치마크
        var tableNames = GenerateTableNames(50);
        
        // 배치 + 모니터링 + 분산 처리
        // await _distributedEngine.InvalidateBatchAsync(tableNames);
        await _monitoredEngine.InvalidateByPatternAsync("integrated:*");
        
        // 백그라운드 큐 작업 - 임시 비활성화
        // for (int i = 0; i < 10; i++)
        // {
        //     var job = InvalidationJob.CreateTableInvalidation($"Background{i}");
        //     await _queue.EnqueueAsync(job);
        // }
        // 
        // var jobs = await _queue.DequeueBatchAsync(10, TimeSpan.FromMilliseconds(50));
    }

    private static string[] GenerateTableNames(int count)
    {
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = $"Table{i}";
        }
        return names;
    }
}

/// <summary>
/// 벤치마크용 고성능 Mock 엔진
/// </summary>
public class BenchmarkMockInvalidationEngine : IInvalidationEngine
{
    public Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

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
        => new BenchmarkInvalidationContext();

    public Task ClearAllAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new InvalidationEngineStatus
        {
            IsHealthy = true,
            Uptime = TimeSpan.FromMinutes(1),
            TrackedKeysCount = 0,
            RegisteredRulesCount = 0,
            Metrics = new Dictionary<string, object>()
        });
    }
}

/*
/// <summary>
/// 벤치마크용 Mock 분산 이벤트 버스 - 분산 이벤트 타입들이 구현되면 활성화
/// </summary>
public class MockDistributedEventBus : IDistributedEventBus
{
    public Task PublishInvalidationEventAsync<T>(T invalidationEvent, CancellationToken cancellationToken = default)
        where T : class, IDistributedInvalidationEvent
        => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Subscribe<T>(IDistributedInvalidationEventHandler<T> handler)
        where T : class, IDistributedInvalidationEvent
    {
        // No-op for benchmark
    }

    public void Unsubscribe<T>(IDistributedInvalidationEventHandler<T> handler)
        where T : class, IDistributedInvalidationEvent
    {
        // No-op for benchmark
    }

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<EventBusStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new EventBusStatistics
        {
            IsConnected = true,
            PublishedEvents = 0,
            ConsumedEvents = 0,
            FailedEvents = 0,
            AverageProcessingTime = TimeSpan.Zero
        });
    }
}
*/

/// <summary>
/// 벤치마크용 Mock 무효화 컨텍스트
/// </summary>
public class BenchmarkInvalidationContext : BaseInvalidationContext
{
    public BenchmarkInvalidationContext() : base(InvalidationTrigger.Manual("Benchmark", "Test"))
    {
    }

    public override IInvalidationContext Clone()
    {
        var clone = new BenchmarkInvalidationContext();
        CopyPropertiesTo(clone);
        return clone;
    }
}

/// <summary>
/// 벤치마크 설정
/// </summary>
public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddJob(Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithWarmupCount(1)
            .WithIterationCount(3));
    }
}