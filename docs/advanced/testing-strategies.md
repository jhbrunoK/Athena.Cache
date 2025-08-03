---
layout: default
title: Testing Strategies
---

# Testing Strategies

Comprehensive testing approaches for Athena.Cache implementations, from unit tests to performance testing and integration scenarios.

## Unit Testing Cache Logic

Test cache behavior in isolation with mocked dependencies.

### Basic Cache Tests

```csharp
public class CacheServiceTests
{
    private readonly Mock<ICacheProvider> _mockProvider;
    private readonly Mock<ICacheSerializer> _mockSerializer;
    private readonly Mock<ILogger<CacheService>> _mockLogger;
    private readonly CacheService _cacheService;

    public CacheServiceTests()
    {
        _mockProvider = new Mock<ICacheProvider>();
        _mockSerializer = new Mock<ICacheSerializer>();
        _mockLogger = new Mock<ILogger<CacheService>>();
        
        _cacheService = new CacheService(_mockProvider.Object, _mockSerializer.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetAsync_WhenKeyExists_ReturnsValue()
    {
        // Arrange
        var key = "test-key";
        var expectedValue = "test-value";
        var serializedData = Encoding.UTF8.GetBytes(expectedValue);
        
        _mockProvider.Setup(p => p.GetAsync<byte[]>(key, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(serializedData);
        _mockSerializer.Setup(s => s.Deserialize<string>(serializedData))
                      .Returns(expectedValue);

        // Act
        var result = await _cacheService.GetAsync<string>(key);

        // Assert
        Assert.Equal(expectedValue, result);
        _mockProvider.Verify(p => p.GetAsync<byte[]>(key, It.IsAny<CancellationToken>()), Times.Once);
        _mockSerializer.Verify(s => s.Deserialize<string>(serializedData), Times.Once);
    }

    [Fact]
    public async Task GetAsync_WhenKeyDoesNotExist_ReturnsDefault()
    {
        // Arrange
        var key = "non-existent-key";
        
        _mockProvider.Setup(p => p.GetAsync<byte[]>(key, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((byte[])null);

        // Act
        var result = await _cacheService.GetAsync<string>(key);

        // Assert
        Assert.Null(result);
        _mockProvider.Verify(p => p.GetAsync<byte[]>(key, It.IsAny<CancellationToken>()), Times.Once);
        _mockSerializer.Verify(s => s.Deserialize<string>(It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task SetAsync_WhenCalled_StoresValue()
    {
        // Arrange
        var key = "test-key";
        var value = "test-value";
        var expiration = TimeSpan.FromMinutes(30);
        var serializedData = Encoding.UTF8.GetBytes(value);
        
        _mockSerializer.Setup(s => s.Serialize(value))
                      .Returns(serializedData);
        _mockProvider.Setup(p => p.SetAsync(key, serializedData, expiration, It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        // Act
        await _cacheService.SetAsync(key, value, expiration);

        // Assert
        _mockSerializer.Verify(s => s.Serialize(value), Times.Once);
        _mockProvider.Verify(p => p.SetAsync(key, serializedData, expiration, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_WhenSerializationFails_ThrowsException()
    {
        // Arrange
        var key = "test-key";
        var value = "test-value";
        var expectedException = new SerializationException("Serialization failed");
        
        _mockSerializer.Setup(s => s.Serialize(value))
                      .Throws(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SerializationException>(
            () => _cacheService.SetAsync(key, value));
        
        Assert.Equal(expectedException.Message, exception.Message);
        _mockProvider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task GetAsync_WhenKeyIsInvalid_ThrowsArgumentException(string invalidKey)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _cacheService.GetAsync<string>(invalidKey));
    }
}
```

### Cache Attribute Tests

```csharp
public class CacheAttributeTests
{
    private readonly Mock<ICacheService> _mockCacheService;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly CacheActionFilter _cacheFilter;

    public CacheAttributeTests()
    {
        _mockCacheService = new Mock<ICacheService>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(ICacheService)))
                           .Returns(_mockCacheService.Object);
        
        _cacheFilter = new CacheActionFilter(_mockServiceProvider.Object);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenCacheHit_SkipsActionExecution()
    {
        // Arrange
        var context = CreateActionExecutingContext();
        var cacheKey = "test-cache-key";
        var cachedResult = new TestResult { Value = "cached" };
        
        _mockCacheService.Setup(cs => cs.GetAsync<TestResult>(cacheKey, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(cachedResult);

        // Act
        await _cacheFilter.OnActionExecutionAsync(context, () => Task.FromResult(CreateActionExecutedContext()));

        // Assert
        Assert.IsType<ObjectResult>(context.Result);
        var objectResult = (ObjectResult)context.Result;
        Assert.Equal(cachedResult, objectResult.Value);
        
        _mockCacheService.Verify(cs => cs.GetAsync<TestResult>(cacheKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenCacheMiss_ExecutesActionAndCaches()
    {
        // Arrange
        var context = CreateActionExecutingContext();
        var cacheKey = "test-cache-key";
        var actionResult = new TestResult { Value = "fresh" };
        var executedContext = CreateActionExecutedContext(actionResult);
        
        _mockCacheService.Setup(cs => cs.GetAsync<TestResult>(cacheKey, It.IsAny<CancellationToken>()))
                        .ReturnsAsync((TestResult)null);

        // Act
        await _cacheFilter.OnActionExecutionAsync(context, () => Task.FromResult(executedContext));

        // Assert
        _mockCacheService.Verify(cs => cs.GetAsync<TestResult>(cacheKey, It.IsAny<CancellationToken>()), Times.Once);
        _mockCacheService.Verify(cs => cs.SetAsync(cacheKey, actionResult, It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private ActionExecutingContext CreateActionExecutingContext()
    {
        var actionContext = new ActionContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = _mockServiceProvider.Object },
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor
            {
                ControllerName = "Test",
                ActionName = "TestAction"
            }
        };

        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object>(), null);
    }

    private ActionExecutedContext CreateActionExecutedContext(object result = null)
    {
        var actionContext = new ActionContext();
        var context = new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), null)
        {
            Result = result != null ? new ObjectResult(result) : new OkResult()
        };
        return context;
    }
}

public class TestResult
{
    public string Value { get; set; }
}
```

## Integration Testing

Test cache behavior with real dependencies and infrastructure.

### In-Memory Integration Tests

```csharp
public class CacheIntegrationTests : IClassFixture<TestServerFixture>
{
    private readonly TestServerFixture _fixture;
    private readonly HttpClient _client;

    public CacheIntegrationTests(TestServerFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
    }

    [Fact]
    public async Task CachedEndpoint_FirstCall_ShouldCacheMiss()
    {
        // Act
        var response = await _client.GetAsync("/api/test/cached-data");

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.Contains("X-Athena-Cache"));
        Assert.Equal("MISS", response.Headers.GetValues("X-Athena-Cache").First());
    }

    [Fact]
    public async Task CachedEndpoint_SecondCall_ShouldCacheHit()
    {
        // Arrange - First call to populate cache
        await _client.GetAsync("/api/test/cached-data");

        // Act - Second call should hit cache
        var response = await _client.GetAsync("/api/test/cached-data");

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.Contains("X-Athena-Cache"));
        Assert.Equal("HIT", response.Headers.GetValues("X-Athena-Cache").First());
    }

    [Fact]
    public async Task CacheInvalidation_ShouldClearRelatedCache()
    {
        // Arrange - Populate cache
        var getResponse1 = await _client.GetAsync("/api/test/cached-data");
        Assert.Equal("MISS", getResponse1.Headers.GetValues("X-Athena-Cache").First());

        // Act - Trigger invalidation
        var postResponse = await _client.PostAsync("/api/test/invalidate-cache", new StringContent(""));
        postResponse.EnsureSuccessStatusCode();

        // Assert - Cache should miss after invalidation
        var getResponse2 = await _client.GetAsync("/api/test/cached-data");
        Assert.Equal("MISS", getResponse2.Headers.GetValues("X-Athena-Cache").First());
    }
}

public class TestServerFixture : IDisposable
{
    public TestServer Server { get; }
    public HttpClient Client { get; }

    public TestServerFixture()
    {
        var builder = new WebApplicationBuilder();
        
        // Configure test services
        builder.Services.AddControllers();
        builder.Services.AddAthenaCacheComplete(options =>
        {
            options.Namespace = "TestApp";
            options.DefaultExpirationMinutes = 5;
        });

        var app = builder.Build();
        
        app.UseRouting();
        app.UseAthenaCache();
        app.MapControllers();

        Server = new TestServer(app);
        Client = Server.CreateClient();
    }

    public void Dispose()
    {
        Client?.Dispose();
        Server?.Dispose();
    }
}

[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    [HttpGet("cached-data")]
    [AthenaCache(ExpirationMinutes = 10)]
    [CacheInvalidateOn("TestData")]
    public async Task<IActionResult> GetCachedData()
    {
        // Simulate some work
        await Task.Delay(100);
        return Ok(new { Data = "Test Data", Timestamp = DateTimeOffset.UtcNow });
    }

    [HttpPost("invalidate-cache")]
    [CacheInvalidateOn("TestData")]
    public async Task<IActionResult> InvalidateCache()
    {
        // This endpoint just triggers cache invalidation
        return Ok();
    }
}
```

### Redis Integration Tests

```csharp
public class RedisCacheIntegrationTests : IClassFixture<RedisFixture>
{
    private readonly RedisFixture _redisFixture;
    private readonly ICacheService _cacheService;

    public RedisCacheIntegrationTests(RedisFixture redisFixture)
    {
        _redisFixture = redisFixture;
        
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddAthenaCacheRedisComplete(
            athenaOptions => { athenaOptions.Namespace = "IntegrationTest"; },
            redisOptions => { redisOptions.ConnectionString = _redisFixture.ConnectionString; }
        );
        
        var serviceProvider = serviceCollection.BuildServiceProvider();
        _cacheService = serviceProvider.GetRequiredService<ICacheService>();
    }

    [Fact]
    public async Task Redis_SetAndGet_ShouldWorkCorrectly()
    {
        // Arrange
        var key = $"test-key-{Guid.NewGuid()}";
        var value = new TestData { Id = 1, Name = "Test" };

        // Act
        await _cacheService.SetAsync(key, value, TimeSpan.FromMinutes(5));
        var retrieved = await _cacheService.GetAsync<TestData>(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(value.Id, retrieved.Id);
        Assert.Equal(value.Name, retrieved.Name);
    }

    [Fact]
    public async Task Redis_Expiration_ShouldWorkCorrectly()
    {
        // Arrange
        var key = $"expiring-key-{Guid.NewGuid()}";
        var value = "test-value";

        // Act
        await _cacheService.SetAsync(key, value, TimeSpan.FromMilliseconds(100));
        var immediate = await _cacheService.GetAsync<string>(key);
        
        await Task.Delay(200); // Wait for expiration
        
        var afterExpiration = await _cacheService.GetAsync<string>(key);

        // Assert
        Assert.Equal(value, immediate);
        Assert.Null(afterExpiration);
    }

    [Fact]
    public async Task Redis_RemoveByPattern_ShouldWorkCorrectly()
    {
        // Arrange
        var prefix = $"pattern-test-{Guid.NewGuid()}";
        var keys = new[] { $"{prefix}-1", $"{prefix}-2", $"{prefix}-3" };
        
        foreach (var key in keys)
        {
            await _cacheService.SetAsync(key, $"value-{key}", TimeSpan.FromMinutes(5));
        }

        // Act
        await _cacheService.RemoveByPatternAsync($"{prefix}*");

        // Assert
        foreach (var key in keys)
        {
            var value = await _cacheService.GetAsync<string>(key);
            Assert.Null(value);
        }
    }
}

public class RedisFixture : IDisposable
{
    private readonly IContainer _redisContainer;
    
    public string ConnectionString { get; }

    public RedisFixture()
    {
        // Start Redis container for testing
        _redisContainer = new ContainerBuilder()
            .WithImage("redis:7-alpine")
            .WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379))
            .Build();

        _redisContainer.StartAsync().Wait();
        
        ConnectionString = $"localhost:{_redisContainer.GetMappedPublicPort(6379)}";
    }

    public void Dispose()
    {
        _redisContainer?.DisposeAsync().AsTask().Wait();
    }
}

public class TestData
{
    public int Id { get; set; }
    public string Name { get; set; }
}
```

## Performance Testing

Measure cache performance under various load conditions.

### Load Testing with NBomber

```csharp
public class CachePerformanceTests
{
    [Fact]
    public void CacheLoadTest_ShouldHandleHighThroughput()
    {
        var scenario = Scenario.Create("cache_load_test", async context =>
        {
            using var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("http://localhost:5000");
            
            var userId = Random.Shared.Next(1, 1000);
            var response = await httpClient.GetAsync($"/api/users/{userId}");
            
            return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
        })
        .WithLoadSimulations(
            Simulation.InjectPerSec(rate: 100, during: TimeSpan.FromMinutes(2)),
            Simulation.KeepConstant(copies: 50, during: TimeSpan.FromMinutes(3))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        // Assert performance criteria
        Assert.True(stats.AllOkCount > 0);
        Assert.True(stats.AllFailCount < stats.AllOkCount * 0.01); // Less than 1% failure rate
        Assert.True(stats.ScenarioStats[0].Ok.Response.Mean < 100); // Average response time under 100ms
    }

    [Fact]
    public void CacheMissVsHitPerformance_ShouldShowSignificantDifference()
    {
        var cacheMissScenario = Scenario.Create("cache_miss", async context =>
        {
            using var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("http://localhost:5000");
            
            // Use unique key to ensure cache miss
            var uniqueId = Guid.NewGuid();
            var response = await httpClient.GetAsync($"/api/data/{uniqueId}");
            
            return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
        })
        .WithLoadSimulations(Simulation.InjectPerSec(rate: 10, during: TimeSpan.FromMinutes(1)));

        var cacheHitScenario = Scenario.Create("cache_hit", async context =>
        {
            using var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("http://localhost:5000");
            
            // Use fixed key to ensure cache hit after first call
            var response = await httpClient.GetAsync("/api/data/fixed-key");
            
            return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
        })
        .WithLoadSimulations(Simulation.InjectPerSec(rate: 10, during: TimeSpan.FromMinutes(1)));

        var missStats = NBomberRunner.RegisterScenarios(cacheMissScenario).Run();
        var hitStats = NBomberRunner.RegisterScenarios(cacheHitScenario).Run();

        // Cache hits should be significantly faster
        var missResponseTime = missStats.ScenarioStats[0].Ok.Response.Mean;
        var hitResponseTime = hitStats.ScenarioStats[0].Ok.Response.Mean;
        
        Assert.True(hitResponseTime < missResponseTime * 0.5); // Cache hits should be at least 50% faster
    }
}
```

### Memory and Resource Testing

```csharp
public class CacheResourceTests
{
    [Fact]
    public async Task LargeCacheOperations_ShouldNotCauseMemoryLeak()
    {
        // Arrange
        var cacheService = CreateCacheService();
        var initialMemory = GC.GetTotalMemory(true);

        // Act - Perform many cache operations
        for (int i = 0; i < 10000; i++)
        {
            var key = $"test-key-{i}";
            var value = new string('x', 1000); // 1KB string
            
            await cacheService.SetAsync(key, value, TimeSpan.FromMinutes(1));
            
            if (i % 1000 == 0)
            {
                // Periodically check memory doesn't grow excessively
                var currentMemory = GC.GetTotalMemory(false);
                var memoryGrowth = currentMemory - initialMemory;
                
                // Allow for reasonable growth but detect leaks
                Assert.True(memoryGrowth < 100_000_000); // Less than 100MB growth
            }
        }

        // Clean up and force GC
        await cacheService.ClearAsync();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Assert - Memory should return close to initial level
        var finalMemory = GC.GetTotalMemory(true);
        var netGrowth = finalMemory - initialMemory;
        
        Assert.True(netGrowth < 10_000_000); // Less than 10MB net growth
    }

    [Fact]
    public async Task ConcurrentCacheOperations_ShouldBeThreadSafe()
    {
        // Arrange
        var cacheService = CreateCacheService();
        var concurrentTasks = 100;
        var operationsPerTask = 100;

        // Act - Run concurrent operations
        var tasks = Enumerable.Range(0, concurrentTasks)
            .Select(taskId => Task.Run(async () =>
            {
                for (int i = 0; i < operationsPerTask; i++)
                {
                    var key = $"concurrent-key-{taskId}-{i}";
                    var value = $"value-{taskId}-{i}";
                    
                    await cacheService.SetAsync(key, value, TimeSpan.FromMinutes(1));
                    var retrieved = await cacheService.GetAsync<string>(key);
                    
                    Assert.Equal(value, retrieved);
                }
            }))
            .ToArray();

        // Assert - All tasks complete without exceptions
        await Task.WhenAll(tasks);
        
        // Verify final cache state
        for (int taskId = 0; taskId < concurrentTasks; taskId++)
        {
            for (int i = 0; i < operationsPerTask; i++)
            {
                var key = $"concurrent-key-{taskId}-{i}";
                var expectedValue = $"value-{taskId}-{i}";
                var actualValue = await cacheService.GetAsync<string>(key);
                
                Assert.Equal(expectedValue, actualValue);
            }
        }
    }

    private ICacheService CreateCacheService()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddAthenaCacheComplete();
        var serviceProvider = serviceCollection.BuildServiceProvider();
        return serviceProvider.GetRequiredService<ICacheService>();
    }
}
```

## Mock Implementations for Testing

Create mock implementations for isolated testing.

### In-Memory Mock Cache

```csharp
public class MockCacheProvider : ICacheProvider
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    
    public Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (!entry.IsExpired)
            {
                return Task.FromResult((T)entry.Value);
            }
            
            _cache.TryRemove(key, out _);
        }
        
        return Task.FromResult(default(T));
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var entry = new CacheEntry
        {
            Value = value,
            ExpiresAt = expiration.HasValue ? DateTimeOffset.UtcNow.Add(expiration.Value) : null
        };
        
        _cache.AddOrUpdate(key, entry, (k, existing) => entry);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var regex = new Regex(pattern.Replace("*", ".*"));
        var keysToRemove = _cache.Keys.Where(key => regex.IsMatch(key)).ToList();
        
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
        
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (!entry.IsExpired)
            {
                return Task.FromResult(true);
            }
            
            _cache.TryRemove(key, out _);
        }
        
        return Task.FromResult(false);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _cache.Clear();
        return Task.CompletedTask;
    }

    public Task<IEnumerable<string>> GetKeysAsync(string pattern = "*", CancellationToken cancellationToken = default)
    {
        var regex = new Regex(pattern.Replace("*", ".*"));
        var matchingKeys = _cache.Keys.Where(key => regex.IsMatch(key));
        return Task.FromResult(matchingKeys);
    }

    private class CacheEntry
    {
        public object Value { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        
        public bool IsExpired => ExpiresAt.HasValue && DateTimeOffset.UtcNow > ExpiresAt.Value;
    }
}
```

### Failure Simulation Mock

```csharp
public class FailureSimulationCacheProvider : ICacheProvider
{
    private readonly ICacheProvider _innerProvider;
    private readonly FailureSimulationOptions _options;
    private readonly Random _random = new();

    public FailureSimulationCacheProvider(ICacheProvider innerProvider, FailureSimulationOptions options)
    {
        _innerProvider = innerProvider;
        _options = options;
    }

    public async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        SimulateFailure("GET");
        await SimulateLatency();
        return await _innerProvider.GetAsync<T>(key, cancellationToken);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        SimulateFailure("SET");
        await SimulateLatency();
        await _innerProvider.SetAsync(key, value, expiration, cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        SimulateFailure("REMOVE");
        await SimulateLatency();
        await _innerProvider.RemoveAsync(key, cancellationToken);
    }

    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        SimulateFailure("REMOVE_PATTERN");
        await SimulateLatency();
        await _innerProvider.RemoveByPatternAsync(pattern, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        SimulateFailure("EXISTS");
        await SimulateLatency();
        return await _innerProvider.ExistsAsync(key, cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        SimulateFailure("CLEAR");
        await SimulateLatency();
        await _innerProvider.ClearAsync(cancellationToken);
    }

    public async Task<IEnumerable<string>> GetKeysAsync(string pattern = "*", CancellationToken cancellationToken = default)
    {
        SimulateFailure("GET_KEYS");
        await SimulateLatency();
        return await _innerProvider.GetKeysAsync(pattern, cancellationToken);
    }

    private void SimulateFailure(string operation)
    {
        if (_random.NextDouble() < _options.FailureRate)
        {
            throw new InvalidOperationException($"Simulated failure for operation: {operation}");
        }
    }

    private async Task SimulateLatency()
    {
        if (_options.SimulateLatency)
        {
            var latency = _random.Next(_options.MinLatencyMs, _options.MaxLatencyMs);
            await Task.Delay(latency);
        }
    }
}

public class FailureSimulationOptions
{
    public double FailureRate { get; set; } = 0.05; // 5% failure rate
    public bool SimulateLatency { get; set; } = true;
    public int MinLatencyMs { get; set; } = 10;
    public int MaxLatencyMs { get; set; } = 100;
}
```

## Test Configuration and Helpers

Utilities to simplify testing setup and execution.

### Test Configuration Builder

```csharp
public class TestCacheConfigurationBuilder
{
    private readonly List<Action<IServiceCollection>> _serviceConfigurations = new();
    private readonly Dictionary<string, object> _configurationValues = new();

    public TestCacheConfigurationBuilder WithMemoryCache()
    {
        _serviceConfigurations.Add(services =>
        {
            services.AddAthenaCacheComplete(options =>
            {
                options.Namespace = "Test";
                ApplyConfiguration(options);
            });
        });
        return this;
    }

    public TestCacheConfigurationBuilder WithRedisCache(string connectionString)
    {
        _serviceConfigurations.Add(services =>
        {
            services.AddAthenaCacheRedisComplete(
                athenaOptions =>
                {
                    athenaOptions.Namespace = "Test";
                    ApplyConfiguration(athenaOptions);
                },
                redisOptions =>
                {
                    redisOptions.ConnectionString = connectionString;
                });
        });
        return this;
    }

    public TestCacheConfigurationBuilder WithMockCache()
    {
        _serviceConfigurations.Add(services =>
        {
            services.AddSingleton<ICacheProvider, MockCacheProvider>();
            services.AddSingleton<ICacheSerializer, JsonCacheSerializer>();
            services.AddSingleton<ICacheService, CacheService>();
        });
        return this;
    }

    public TestCacheConfigurationBuilder WithConfiguration(string key, object value)
    {
        _configurationValues[key] = value;
        return this;
    }

    public TestCacheConfigurationBuilder WithFailureSimulation(FailureSimulationOptions options)
    {
        _serviceConfigurations.Add(services =>
        {
            services.Decorate<ICacheProvider>(provider =>
                new FailureSimulationCacheProvider(provider, options));
        });
        return this;
    }

    public IServiceProvider Build()
    {
        var services = new ServiceCollection();
        
        foreach (var configuration in _serviceConfigurations)
        {
            configuration(services);
        }

        return services.BuildServiceProvider();
    }

    private void ApplyConfiguration(AthenaCacheOptions options)
    {
        foreach (var kvp in _configurationValues)
        {
            var property = typeof(AthenaCacheOptions).GetProperty(kvp.Key);
            property?.SetValue(options, kvp.Value);
        }
    }
}

// Usage example
public class CacheTestBase
{
    protected IServiceProvider CreateTestServices(Action<TestCacheConfigurationBuilder> configure = null)
    {
        var builder = new TestCacheConfigurationBuilder();
        configure?.Invoke(builder);
        return builder.Build();
    }
}
```

### Test Data Factories

```csharp
public static class TestDataFactory
{
    public static TestUser CreateTestUser(int id = 1)
    {
        return new TestUser
        {
            Id = id,
            Name = $"Test User {id}",
            Email = $"user{id}@test.com",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static List<TestUser> CreateTestUsers(int count)
    {
        return Enumerable.Range(1, count)
            .Select(CreateTestUser)
            .ToList();
    }

    public static string CreateLargeString(int sizeInKB)
    {
        var size = sizeInKB * 1024;
        return new string('x', size);
    }

    public static ComplexTestObject CreateComplexTestObject()
    {
        return new ComplexTestObject
        {
            Id = Guid.NewGuid(),
            Users = CreateTestUsers(10),
            Metadata = new Dictionary<string, object>
            {
                ["key1"] = "value1",
                ["key2"] = 42,
                ["key3"] = DateTimeOffset.UtcNow
            },
            NestedObject = new NestedTestObject
            {
                Data = CreateLargeString(1), // 1KB
                Numbers = Enumerable.Range(1, 100).ToList()
            }
        };
    }
}

public class TestUser
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ComplexTestObject
{
    public Guid Id { get; set; }
    public List<TestUser> Users { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    public NestedTestObject NestedObject { get; set; }
}

public class NestedTestObject
{
    public string Data { get; set; }
    public List<int> Numbers { get; set; }
}
```

## Best Practices for Cache Testing

### 1. Test Cache Boundaries

```csharp
[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("   ")]
public async Task InvalidKeys_ShouldThrowArgumentException(string invalidKey)
{
    var cacheService = CreateCacheService();
    await Assert.ThrowsAsync<ArgumentException>(() => cacheService.GetAsync<string>(invalidKey));
}
```

### 2. Test Expiration Behavior

```csharp
[Fact]
public async Task Expiration_ShouldRemoveItemsAfterTimeout()
{
    var cacheService = CreateCacheService();
    var key = "expiring-key";
    var value = "test-value";
    
    await cacheService.SetAsync(key, value, TimeSpan.FromMilliseconds(50));
    
    var immediate = await cacheService.GetAsync<string>(key);
    Assert.Equal(value, immediate);
    
    await Task.Delay(100);
    
    var afterExpiration = await cacheService.GetAsync<string>(key);
    Assert.Null(afterExpiration);
}
```

### 3. Test Error Scenarios

```csharp
[Fact]
public async Task SerializationError_ShouldPropagateException()
{
    var mockSerializer = new Mock<ICacheSerializer>();
    mockSerializer.Setup(s => s.Serialize(It.IsAny<object>()))
                  .Throws<SerializationException>();
    
    var cacheService = new CacheService(Mock.Of<ICacheProvider>(), mockSerializer.Object, Mock.Of<ILogger<CacheService>>());
    
    await Assert.ThrowsAsync<SerializationException>(() => cacheService.SetAsync("key", "value"));
}
```

For related testing topics:
- **[Custom Providers]({{ site.baseurl }}/advanced/custom-providers)** - Test custom implementations
- **[Performance Metrics]({{ site.baseurl }}/monitoring/performance-metrics)** - Monitor test environments
- **[Error Handling]({{ site.baseurl }}/fundamentals/error-handling)** - Test error scenarios