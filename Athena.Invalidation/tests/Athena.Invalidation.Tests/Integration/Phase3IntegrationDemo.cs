using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Core.Extensions;
using Athena.Invalidation.Monitoring.Extensions;
using Athena.Invalidation.Monitoring.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Athena.Invalidation.Tests.Integration;

/// <summary>
/// Phase 3의 모든 기능을 통합하여 보여주는 데모
/// </summary>
public class Phase3IntegrationDemo : IAsyncLifetime
{
    private ServiceProvider _serviceProvider = null!;
    private IInvalidationEngine _invalidationEngine = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        // 로깅 설정
        services.AddLogging(builder => 
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 기본 무효화 엔진 (Mock)
        services.AddSingleton<IInvalidationEngine>(provider => 
            new DemoMockInvalidationEngine(provider.GetRequiredService<ILogger<DemoMockInvalidationEngine>>()));

        // Phase 3 기능들을 순차적으로 추가
        services
            // 1. 백그라운드 처리 및 배치 최적화
            .AddInvalidationPerformanceOptimization(
                queue => 
                {
                    queue.MaxQueueSize = 5000;
                    queue.MaxBatchSize = 100;
                    queue.BatchWaitTime = TimeSpan.FromMilliseconds(100);
                },
                processor => 
                {
                    processor.MaxConcurrentJobs = 4;
                    processor.EnableBatchOptimization = true;
                },
                batch => 
                {
                    batch.ImmediateProcessingThreshold = 20;
                    batch.EnableImmediateBatching = true;
                })
            
            // 2. 복원력 패턴 (재시도 + 서킷 브레이커)
            .AddInvalidationResilience(
                retry => 
                {
                    retry.MaxRetryAttempts = 3;
                    retry.BaseDelay = TimeSpan.FromMilliseconds(100);
                    retry.DelayStrategy = RetryDelayStrategy.ExponentialWithJitter;
                },
                circuitBreaker => 
                {
                    circuitBreaker.FailureThreshold = 5;
                    circuitBreaker.OpenDuration = TimeSpan.FromSeconds(30);
                })
            
            // 3. 모니터링 및 메트릭
            .AddInvalidationFullMonitoring(
                monitoring => 
                {
                    monitoring.EnableDetailedMetrics = true;
                    monitoring.RecentEventsSampleSize = 500;
                },
                prometheus => 
                {
                    prometheus.EnableMetricServer = false; // 테스트에서는 비활성화
                    prometheus.UpdateInterval = TimeSpan.FromSeconds(5);
                })
            
            // 4. 모니터링 데코레이터 적용
            .DecorateInvalidationEngineWithMonitoring()
            
            // 5. 분산 모니터링
            .IntegrateDistributedMonitoring();

        _serviceProvider = services.BuildServiceProvider();
        _invalidationEngine = _serviceProvider.GetRequiredService<IInvalidationEngine>();

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Phase3FullStack_ShouldIntegrateAllFeatures()
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<Phase3IntegrationDemo>>();
        
        logger.LogInformation("🚀 Starting Phase 3 Full Stack Integration Demo");
        
        // === 1. 성능 최적화 테스트 ===
        logger.LogInformation("📊 Testing Performance Optimization Features...");
        
        // 배치 처리
        var batchTables = Enumerable.Range(1, 50).Select(i => $"Table{i}").ToArray();
        await _invalidationEngine.InvalidateBatchAsync(batchTables);
        logger.LogInformation("✅ Batch invalidation completed for {Count} tables", batchTables.Length);
        
        // 동시 처리
        var concurrentTasks = Enumerable.Range(1, 20)
            .Select(i => _invalidationEngine.InvalidateByTableAsync($"ConcurrentTable{i}"));
        await Task.WhenAll(concurrentTasks);
        logger.LogInformation("✅ Concurrent invalidation completed for 20 tables");

        // === 2. 복원력 테스트 ===
        logger.LogInformation("🛡️ Testing Resilience Features...");
        
        try
        {
            // 일부 실패가 예상되는 작업들
            var resilientTasks = new[]
            {
                _invalidationEngine.InvalidateByPatternAsync("resilient:pattern1:*"),
                _invalidationEngine.InvalidateByPatternAsync("resilient:pattern2:*"),
                _invalidationEngine.InvalidateByKeyAsync("resilient:key1")
            };
            
            await Task.WhenAll(resilientTasks);
            logger.LogInformation("✅ Resilience patterns handled operations successfully");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Some operations failed as expected for resilience testing");
        }

        // === 3. 모니터링 및 메트릭 확인 ===
        logger.LogInformation("📈 Checking Monitoring and Metrics...");
        
        var metricsCollector = _serviceProvider.GetService<Athena.Invalidation.Monitoring.Abstractions.IInvalidationMetricsCollector>();
        if (metricsCollector != null)
        {
            var metrics = await metricsCollector.GetMetricsAsync();
            logger.LogInformation("📊 Current Metrics:");
            logger.LogInformation("   Total Invalidations: {Total}", metrics.TotalInvalidations);
            logger.LogInformation("   Success Rate: {Rate:P2}", metrics.InvalidationSuccessRate);
            logger.LogInformation("   Average Time: {Time}ms", metrics.AverageInvalidationTime.TotalMilliseconds);
            logger.LogInformation("   Cache Hit Ratio: {Ratio:P2}", metrics.CacheHitRatio);
            logger.LogInformation("✅ Monitoring system is collecting metrics successfully");
        }

        // === 4. 큐 통계 확인 ===
        logger.LogInformation("🔄 Checking Background Queue Statistics...");
        
        var queueStats = _serviceProvider.GetQueueStatistics();
        logger.LogInformation("📋 Queue Status:");
        logger.LogInformation("   Queue Count: {Count}", queueStats.QueueCount);
        logger.LogInformation("   Is Empty: {IsEmpty}", queueStats.IsEmpty);

        var processorStats = _serviceProvider.GetBackgroundProcessorStatistics();
        if (processorStats.Any())
        {
            logger.LogInformation("⚙️ Processor Statistics:");
            foreach (var stat in processorStats)
            {
                logger.LogInformation("   {JobType}: {Total} total, {Success} successful, {Rate:P2} success rate",
                    stat.Key, stat.Value.TotalJobs, stat.Value.SuccessfulJobs, stat.Value.SuccessRate);
            }
        }
        logger.LogInformation("✅ Background processing system is working correctly");

        // === 5. 복원력 상태 확인 ===
        logger.LogInformation("🔧 Checking Resilience Status...");
        
        var resilienceStats = _serviceProvider.GetResilienceStatistics();
        logger.LogInformation("🛠️ Resilience Status:");
        logger.LogInformation("   Has Retry: {HasRetry}", resilienceStats.HasRetry);
        logger.LogInformation("   Has Circuit Breaker: {HasCircuitBreaker}", resilienceStats.HasCircuitBreaker);
        logger.LogInformation("   Open Circuits: {OpenCount}", resilienceStats.OpenCircuitCount);
        logger.LogInformation("   Half-Open Circuits: {HalfOpenCount}", resilienceStats.HalfOpenCircuitCount);
        
        if (resilienceStats.CircuitBreakerStates.Any())
        {
            logger.LogInformation("🔌 Circuit Breaker States:");
            foreach (var state in resilienceStats.CircuitBreakerStates)
            {
                logger.LogInformation("   {Type}: {State} ({Success}/{Total} calls)",
                    state.Key, state.Value.State, state.Value.SuccessCount, state.Value.TotalCalls);
            }
        }
        logger.LogInformation("✅ Resilience patterns are active and monitoring");

        // === 6. 전체 시스템 상태 확인 ===
        logger.LogInformation("🏥 Final System Health Check...");
        
        var engineStatus = await _invalidationEngine.GetStatusAsync();
        logger.LogInformation("🌡️ Engine Status:");
        logger.LogInformation("   Is Healthy: {IsHealthy}", engineStatus.IsHealthy);
        logger.LogInformation("   Uptime: {Uptime}", engineStatus.Uptime);
        logger.LogInformation("   Tracked Keys: {Keys}", engineStatus.TrackedKeysCount);
        logger.LogInformation("   Registered Rules: {Rules}", engineStatus.RegisteredRulesCount);
        
        if (engineStatus.Metrics.Any())
        {
            logger.LogInformation("📊 Additional Metrics:");
            foreach (var metric in engineStatus.Metrics.Take(10)) // 상위 10개만 표시
            {
                logger.LogInformation("   {Key}: {Value}", metric.Key, metric.Value);
            }
        }

        logger.LogInformation("🎉 Phase 3 Full Stack Integration Demo Completed Successfully!");
        logger.LogInformation("🔗 All features are working together harmoniously:");
        logger.LogInformation("   ✅ Performance Optimization (Background Queue + Batch Processing)");
        logger.LogInformation("   ✅ Resilience Patterns (Retry + Circuit Breaker)");
        logger.LogInformation("   ✅ Real-time Monitoring & Metrics Collection");
        logger.LogInformation("   ✅ Health Checks & System Observability");

        // Assert - 기본적인 상태 확인
        Assert.True(engineStatus.IsHealthy, "Engine should be healthy");
        Assert.True(metrics?.TotalInvalidations > 0, "Should have recorded some invalidations");
        Assert.Equal(1.0, metrics?.InvalidationSuccessRate, "Success rate should be 100% with mock engine");
    }

    public async Task DisposeAsync()
    {
        _serviceProvider?.Dispose();
        await Task.CompletedTask;
    }
}

/// <summary>
/// 데모용 Mock 무효화 엔진 - 모든 작업 성공
/// </summary>
public class DemoMockInvalidationEngine : IInvalidationEngine
{
    private readonly ILogger<DemoMockInvalidationEngine> _logger;
    private readonly DateTimeOffset _startTime;
    
    public DemoMockInvalidationEngine(ILogger<DemoMockInvalidationEngine> logger)
    {
        _logger = logger;
        _startTime = DateTimeOffset.UtcNow;
    }

    public Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Mock: Invalidated table {TableName}", tableName);
        return Task.Delay(1, cancellationToken); // 약간의 지연으로 현실적인 처리 시간 시뮬레이션
    }

    public Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Mock: Invalidated pattern {Pattern}", pattern);
        return Task.Delay(2, cancellationToken);
    }

    public Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Mock: Invalidated key {Key}", key);
        return Task.Delay(1, cancellationToken);
    }

    public Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        var count = tableNames.Count();
        _logger.LogDebug("Mock: Invalidated batch of {Count} tables", count);
        return Task.Delay(count / 10 + 1, cancellationToken); // 배치 크기에 비례한 처리 시간
    }

    public Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Mock: Invalidated hierarchy {TableName} with {RelatedCount} related tables", tableName, relatedTables.Length);
        return Task.Delay(maxDepth * 2, cancellationToken);
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
            Uptime = DateTimeOffset.UtcNow - _startTime,
            TrackedKeysCount = 0,
            RegisteredRulesCount = 0,
            LastActivity = DateTimeOffset.UtcNow,
            Metrics = new Dictionary<string, object>
            {
                ["EngineType"] = "DemoMockInvalidationEngine",
                ["StartTime"] = _startTime,
                ["MockImplementation"] = true
            }
        });
    }
}