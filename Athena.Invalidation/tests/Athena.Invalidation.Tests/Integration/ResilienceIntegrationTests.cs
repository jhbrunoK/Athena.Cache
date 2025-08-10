using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Core.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Athena.Invalidation.Tests.Integration;

public class ResilienceIntegrationTests : IAsyncLifetime
{
    private ServiceProvider _serviceProvider = null!;
    private RetryInvalidationEngine _retryEngine = null!;
    private CircuitBreakerInvalidationEngine _circuitBreakerEngine = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        
        // 로깅
        services.AddLogging(builder => builder.AddConsole());
        
        // 재시도 옵션
        services.Configure<RetryOptions>(options =>
        {
            options.MaxRetryAttempts = 3;
            options.BaseDelay = TimeSpan.FromMilliseconds(50);
            options.DelayStrategy = RetryDelayStrategy.Linear;
        });
        
        // 서킷 브레이커 옵션
        services.Configure<CircuitBreakerOptions>(options =>
        {
            options.FailureThreshold = 3;
            options.OpenDuration = TimeSpan.FromMilliseconds(500);
            options.MinSuccessfulCallsInHalfOpen = 2;
            options.SlidingWindowSize = 10;
            options.MinCallsInSlidingWindow = 3;
            options.HealthCheckInterval = TimeSpan.FromMilliseconds(200);
        });
        
        _serviceProvider = services.BuildServiceProvider();

        await Task.CompletedTask;
    }

    [Fact]
    public async Task RetryEngine_ShouldRetryFailedOperations()
    {
        // Arrange
        var faultyEngine = new IntermittentlyFaultyMockEngine(failureRate: 0.5); // 50% 실패율
        _retryEngine = new RetryInvalidationEngine(
            faultyEngine,
            _serviceProvider.GetRequiredService<ILogger<RetryInvalidationEngine>>(),
            Options.Create(new RetryOptions { MaxRetryAttempts = 5, BaseDelay = TimeSpan.FromMilliseconds(10) }));

        // Act & Assert - 재시도로 인해 결국 성공해야 함
        await _retryEngine.InvalidateByTableAsync("RetryTest");
        
        // 최소한 하나의 시도는 있어야 함
        Assert.True(faultyEngine.AttemptCount > 0);
        Assert.Contains("RetryTest", faultyEngine.SuccessfulTables);
    }

    [Fact]
    public async Task RetryEngine_ShouldFailAfterMaxAttempts()
    {
        // Arrange
        var alwaysFaultyEngine = new AlwaysFaultyMockEngine();
        _retryEngine = new RetryInvalidationEngine(
            alwaysFaultyEngine,
            _serviceProvider.GetRequiredService<ILogger<RetryInvalidationEngine>>(),
            Options.Create(new RetryOptions { MaxRetryAttempts = 3, BaseDelay = TimeSpan.FromMilliseconds(10) }));

        // Act & Assert
        var aggregateException = await Assert.ThrowsAsync<AggregateException>(
            () => _retryEngine.InvalidateByTableAsync("AlwaysFailTest"));
        
        // 3번의 시도가 모두 기록되어야 함
        Assert.Equal(3, aggregateException.InnerExceptions.Count);
        Assert.Equal(3, alwaysFaultyEngine.AttemptCount);
    }

    [Fact]
    public async Task CircuitBreaker_ShouldOpenAfterConsecutiveFailures()
    {
        // Arrange
        var alwaysFaultyEngine = new AlwaysFaultyMockEngine();
        _circuitBreakerEngine = new CircuitBreakerInvalidationEngine(
            alwaysFaultyEngine,
            _serviceProvider.GetRequiredService<ILogger<CircuitBreakerInvalidationEngine>>(),
            Options.Create(new CircuitBreakerOptions 
            { 
                FailureThreshold = 2, 
                OpenDuration = TimeSpan.FromSeconds(1) 
            }));

        // Act - 연속 실패로 서킷 브레이커 열기
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _circuitBreakerEngine.InvalidateByTableAsync("Test1"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _circuitBreakerEngine.InvalidateByTableAsync("Test2"));

        // 서킷이 열리면 CircuitBreakerOpenException이 발생해야 함
        var exception = await Assert.ThrowsAsync<CircuitBreakerOpenException>(
            () => _circuitBreakerEngine.InvalidateByTableAsync("Test3"));

        Assert.Contains("Circuit breaker is open", exception.Message);

        // 서킷 상태 확인
        var circuitStates = _circuitBreakerEngine.GetCircuitBreakerStates();
        var tableCircuitState = circuitStates["table"];
        Assert.Equal(CircuitState.Open, tableCircuitState.State);
    }

    [Fact]
    public async Task CircuitBreaker_ShouldTransitionToHalfOpenAfterTimeout()
    {
        // Arrange
        var recoveringEngine = new RecoveringMockEngine();
        _circuitBreakerEngine = new CircuitBreakerInvalidationEngine(
            recoveringEngine,
            _serviceProvider.GetRequiredService<ILogger<CircuitBreakerInvalidationEngine>>(),
            Options.Create(new CircuitBreakerOptions 
            { 
                FailureThreshold = 2, 
                OpenDuration = TimeSpan.FromMilliseconds(200),
                MinSuccessfulCallsInHalfOpen = 1
            }));

        // 서킷 브레이커를 열기 위해 연속 실패
        recoveringEngine.ShouldFail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _circuitBreakerEngine.InvalidateByTableAsync("Fail1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _circuitBreakerEngine.InvalidateByTableAsync("Fail2"));

        // 서킷이 열려있는지 확인
        await Assert.ThrowsAsync<CircuitBreakerOpenException>(() => _circuitBreakerEngine.InvalidateByTableAsync("Fail3"));

        // OpenDuration 대기
        await Task.Delay(300);

        // 엔진이 회복되도록 설정
        recoveringEngine.ShouldFail = false;

        // Act - Half-Open에서 성공하면 닫혀야 함
        await _circuitBreakerEngine.InvalidateByTableAsync("Success");

        // Assert
        var circuitStates = _circuitBreakerEngine.GetCircuitBreakerStates();
        var tableCircuitState = circuitStates["table"];
        Assert.Equal(CircuitState.Closed, tableCircuitState.State);
        Assert.Contains("Success", recoveringEngine.SuccessfulTables);
    }

    [Fact]
    public async Task CombinedResiliencePatterns_ShouldWorkTogether()
    {
        // Arrange - 재시도 + 서킷 브레이커 조합
        var intermittentEngine = new IntermittentlyFaultyMockEngine(failureRate: 0.3);
        
        var retryEngine = new RetryInvalidationEngine(
            intermittentEngine,
            _serviceProvider.GetRequiredService<ILogger<RetryInvalidationEngine>>(),
            Options.Create(new RetryOptions { MaxRetryAttempts = 2, BaseDelay = TimeSpan.FromMilliseconds(10) }));
        
        var circuitBreakerEngine = new CircuitBreakerInvalidationEngine(
            retryEngine,
            _serviceProvider.GetRequiredService<ILogger<CircuitBreakerInvalidationEngine>>(),
            Options.Create(new CircuitBreakerOptions 
            { 
                FailureThreshold = 5,
                OpenDuration = TimeSpan.FromMilliseconds(200)
            }));

        // Act - 여러 번 호출하여 조합된 복원력 패턴 테스트
        var successCount = 0;
        var attempts = 20;
        
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                await circuitBreakerEngine.InvalidateByTableAsync($"Test{i}");
                successCount++;
            }
            catch (Exception)
            {
                // 예상되는 실패들
            }
        }

        // Assert - 일부는 성공해야 함 (재시도와 서킷 브레이커가 작동)
        Assert.True(successCount > 0, "At least some operations should succeed with retry + circuit breaker");
        Assert.True(intermittentEngine.AttemptCount >= attempts, "Should have attempted at least the number of calls");
    }

    [Fact]
    public async Task CircuitBreakerStatistics_ShouldTrackOperations()
    {
        // Arrange
        var trackingEngine = new StatisticsTrackingMockEngine();
        _circuitBreakerEngine = new CircuitBreakerInvalidationEngine(
            trackingEngine,
            _serviceProvider.GetRequiredService<ILogger<CircuitBreakerInvalidationEngine>>(),
            _serviceProvider.GetRequiredService<IOptions<CircuitBreakerOptions>>());

        // Act
        await _circuitBreakerEngine.InvalidateByTableAsync("Stats1");
        await _circuitBreakerEngine.InvalidateByPatternAsync("pattern:*");
        
        try
        {
            trackingEngine.ShouldFail = true;
            await _circuitBreakerEngine.InvalidateByKeyAsync("failkey");
        }
        catch
        {
            // 예상된 실패
        }

        // Assert
        var circuitStates = _circuitBreakerEngine.GetCircuitBreakerStates();
        
        // 다양한 타입의 서킷 상태가 기록되어야 함
        Assert.True(circuitStates.ContainsKey("table"));
        Assert.True(circuitStates.ContainsKey("pattern"));
        Assert.True(circuitStates.ContainsKey("key"));

        var tableState = circuitStates["table"];
        var keyState = circuitStates["key"];
        
        Assert.True(tableState.TotalCalls > 0);
        Assert.True(tableState.SuccessCount > 0);
        Assert.True(keyState.FailureCount > 0);
    }

    public async Task DisposeAsync()
    {
        if (_retryEngine != null)
            await _retryEngine.DisposeAsync();
        if (_circuitBreakerEngine != null)
            await _circuitBreakerEngine.DisposeAsync();
        _serviceProvider?.Dispose();
    }
}

/// <summary>
/// 간헐적으로 실패하는 Mock 엔진
/// </summary>
public class IntermittentlyFaultyMockEngine : IInvalidationEngine
{
    private readonly double _failureRate;
    private readonly Random _random = new();
    
    public int AttemptCount { get; private set; }
    public List<string> SuccessfulTables { get; } = new();

    public IntermittentlyFaultyMockEngine(double failureRate)
    {
        _failureRate = failureRate;
    }

    public Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        AttemptCount++;
        
        if (_random.NextDouble() < _failureRate)
        {
            throw new InvalidOperationException($"Simulated failure for table {tableName}");
        }
        
        SuccessfulTables.Add(tableName);
        return Task.CompletedTask;
    }

    // 나머지 메서드들은 기본 구현
    public Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
    public Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new InvalidationEngineStatus());
}

/// <summary>
/// 항상 실패하는 Mock 엔진
/// </summary>
public class AlwaysFaultyMockEngine : IInvalidationEngine
{
    public int AttemptCount { get; private set; }

    public Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        AttemptCount++;
        throw new InvalidOperationException($"Always fails for table {tableName}");
    }

    // 나머지 메서드들은 기본 구현
    public Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Always fails");
    public Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Always fails");
    public Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Always fails");
    public Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Always fails");
    public Task InvalidateOnCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : class => throw new InvalidOperationException("Always fails");
    public Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : class => throw new InvalidOperationException("Always fails");
    public Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default) where TReadModel : class => throw new InvalidOperationException("Always fails");
    public Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default) where TProjection : class => throw new InvalidOperationException("Always fails");
    public Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Always fails");
    public Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Always fails");
    public Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Always fails");
    public Task RegisterInvalidationRuleAsync(IInvalidationRule rule, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Always fails");
    public Task UnregisterInvalidationRuleAsync(string ruleId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Always fails");
    public Task<IEnumerable<IInvalidationRule>> GetInvalidationRulesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Always fails");
    public IInvalidationContext CreateContext(InvalidationTrigger trigger, object? metadata = null) => throw new InvalidOperationException("Always fails");
    public Task ClearAllAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Always fails");
    public Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Always fails");
}

/// <summary>
/// 회복 가능한 Mock 엔진
/// </summary>
public class RecoveringMockEngine : IInvalidationEngine
{
    public bool ShouldFail { get; set; } = false;
    public List<string> SuccessfulTables { get; } = new();

    public Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException($"Currently failing for table {tableName}");
        }
        
        SuccessfulTables.Add(tableName);
        return Task.CompletedTask;
    }

    // 나머지 메서드들은 기본 구현
    public Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default) => ShouldFail ? throw new InvalidOperationException("Currently failing") : Task.CompletedTask;
    public Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default) => ShouldFail ? throw new InvalidOperationException("Currently failing") : Task.CompletedTask;
    public Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default) => ShouldFail ? throw new InvalidOperationException("Currently failing") : Task.CompletedTask;
    public Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default) => ShouldFail ? throw new InvalidOperationException("Currently failing") : Task.CompletedTask;
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
    public Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new InvalidationEngineStatus { IsHealthy = !ShouldFail });
}

/// <summary>
/// 통계 추적용 Mock 엔진
/// </summary>
public class StatisticsTrackingMockEngine : IInvalidationEngine
{
    public bool ShouldFail { get; set; } = false;

    public Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default) => 
        ShouldFail ? throw new InvalidOperationException("Tracking failure") : Task.CompletedTask;

    public Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default) => 
        ShouldFail ? throw new InvalidOperationException("Tracking failure") : Task.CompletedTask;

    public Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default) => 
        ShouldFail ? throw new InvalidOperationException("Tracking failure") : Task.CompletedTask;

    // 나머지 메서드들은 기본 구현
    public Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
    public Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new InvalidationEngineStatus { IsHealthy = !ShouldFail });
}