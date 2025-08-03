---
layout: default
title: Error Handling
---

# Error Handling

Robust error handling ensures your application remains stable even when cache operations fail. Athena.Cache provides multiple strategies for handling errors gracefully.

## Error Handling Strategies

Configure how your application responds to cache failures.

### Basic Error Configuration

```csharp
// Program.cs
builder.Services.AddAthenaCacheComplete(options =>
{
    // Error handling strategy
    options.ErrorHandling.OnCacheError = CacheErrorAction.LogAndContinue;
    options.ErrorHandling.OnSerializationError = CacheErrorAction.LogAndThrow;
    options.ErrorHandling.OnConnectionError = CacheErrorAction.LogAndFallback;
    
    // Timeout settings
    options.ErrorHandling.OperationTimeout = TimeSpan.FromSeconds(30);
    options.ErrorHandling.ConnectionTimeout = TimeSpan.FromSeconds(10);
    
    // Retry configuration
    options.ErrorHandling.EnableRetry = true;
    options.ErrorHandling.MaxRetryAttempts = 3;
    options.ErrorHandling.RetryDelayMs = 1000;
    options.ErrorHandling.BackoffMultiplier = 2.0;
});
```

### Error Action Types

```csharp
public enum CacheErrorAction
{
    LogAndContinue,    // Log error, return null, continue execution
    LogAndThrow,       // Log error and throw exception
    LogAndFallback,    // Log error, attempt fallback mechanism
    Silent,            // Ignore error silently (not recommended)
    Custom             // Use custom error handler
}
```

## Exception Types and Handling

Understand different cache exception types and how to handle them.

### Cache-Specific Exceptions

```csharp
public class CacheOperationException : Exception
{
    public string CacheKey { get; }
    public CacheOperation Operation { get; }
    public TimeSpan OperationDuration { get; }
    
    public CacheOperationException(string cacheKey, CacheOperation operation, Exception innerException)
        : base($"Cache operation '{operation}' failed for key '{cacheKey}'", innerException)
    {
        CacheKey = cacheKey;
        Operation = operation;
    }
}

public class CacheSerializationException : CacheOperationException
{
    public Type ObjectType { get; }
    
    public CacheSerializationException(string cacheKey, Type objectType, Exception innerException)
        : base(cacheKey, CacheOperation.Serialize, innerException)
    {
        ObjectType = objectType;
    }
}

public class CacheConnectionException : CacheOperationException
{
    public string ConnectionString { get; }
    
    public CacheConnectionException(string connectionString, Exception innerException)
        : base("connection", CacheOperation.Connect, innerException)
    {
        ConnectionString = connectionString;
    }
}
```

### Global Exception Handler

```csharp
public class GlobalCacheExceptionHandler : ICacheExceptionHandler
{
    private readonly ILogger<GlobalCacheExceptionHandler> _logger;
    private readonly IMetricsCollector _metrics;
    private readonly IAlertService _alertService;

    public GlobalCacheExceptionHandler(
        ILogger<GlobalCacheExceptionHandler> logger,
        IMetricsCollector metrics,
        IAlertService alertService)
    {
        _logger = logger;
        _metrics = metrics;
        _alertService = alertService;
    }

    public async Task<CacheErrorHandlingResult> HandleExceptionAsync(CacheErrorContext context)
    {
        var exception = context.Exception;
        var operation = context.Operation;
        var cacheKey = context.CacheKey;

        // Log the error with appropriate level
        LogError(exception, operation, cacheKey);
        
        // Record metrics
        await _metrics.RecordCacheErrorAsync(operation, exception.GetType().Name);
        
        // Determine handling strategy based on exception type
        var strategy = DetermineHandlingStrategy(exception, operation);
        
        // Check if alert should be triggered
        if (ShouldTriggerAlert(exception, operation))
        {
            await _alertService.TriggerAlertAsync($"Cache error: {exception.Message}", AlertSeverity.Warning);
        }

        return new CacheErrorHandlingResult
        {
            Strategy = strategy,
            ShouldRetry = ShouldRetry(exception, context.AttemptNumber),
            FallbackValue = GetFallbackValue(context),
            ContinueExecution = strategy != CacheErrorStrategy.Throw
        };
    }

    private void LogError(Exception exception, CacheOperation operation, string cacheKey)
    {
        switch (exception)
        {
            case CacheConnectionException:
                _logger.LogError(exception, "Cache connection failed for operation {Operation}", operation);
                break;
            case CacheSerializationException serEx:
                _logger.LogError(exception, "Serialization failed for key {CacheKey}, type {ObjectType}", 
                    cacheKey, serEx.ObjectType.Name);
                break;
            case TimeoutException:
                _logger.LogWarning(exception, "Cache operation {Operation} timed out for key {CacheKey}", 
                    operation, cacheKey);
                break;
            default:
                _logger.LogError(exception, "Cache operation {Operation} failed for key {CacheKey}", 
                    operation, cacheKey);
                break;
        }
    }

    private CacheErrorStrategy DetermineHandlingStrategy(Exception exception, CacheOperation operation)
    {
        return exception switch
        {
            CacheConnectionException => CacheErrorStrategy.Fallback,
            TimeoutException => CacheErrorStrategy.Continue,
            CacheSerializationException => CacheErrorStrategy.Throw,
            ArgumentException => CacheErrorStrategy.Throw,
            _ => CacheErrorStrategy.Continue
        };
    }

    private bool ShouldRetry(Exception exception, int attemptNumber)
    {
        if (attemptNumber >= 3) return false;
        
        return exception switch
        {
            CacheConnectionException => true,
            TimeoutException => true,
            CacheSerializationException => false,
            ArgumentException => false,
            _ => true
        };
    }

    private object GetFallbackValue(CacheErrorContext context)
    {
        // Return appropriate fallback value based on context
        if (context.Operation == CacheOperation.Get)
        {
            return null; // Cache miss fallback
        }
        
        return context.DefaultValue;
    }

    private bool ShouldTriggerAlert(Exception exception, CacheOperation operation)
    {
        return exception switch
        {
            CacheConnectionException => true,
            CacheSerializationException => true,
            _ when operation == CacheOperation.Critical => true,
            _ => false
        };
    }
}
```

## Fallback Mechanisms

Implement fallback strategies when cache operations fail.

### Memory Cache Fallback

```csharp
// Automatic fallback to memory cache when Redis fails
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions =>
    {
        athenaOptions.Resilience.EnableFallbackToMemory = true;
        athenaOptions.Resilience.FallbackToMemoryOnError = true;
        athenaOptions.Resilience.MemoryFallbackMaxItems = 1000;
        athenaOptions.Resilience.MemoryFallbackExpirationMinutes = 15;
    },
    redisOptions => { /* Redis configuration */ });

public class FallbackCacheService : ICacheService
{
    private readonly IDistributedCache _primaryCache;
    private readonly IMemoryCache _fallbackCache;
    private readonly ILogger<FallbackCacheService> _logger;

    public async Task<T> GetAsync<T>(string key)
    {
        try
        {
            // Try primary cache first
            var result = await _primaryCache.GetAsync<T>(key);
            if (result != null) return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary cache failed for key {Key}, trying fallback", key);
            
            // Try fallback cache
            if (_fallbackCache.TryGetValue(key, out T fallbackValue))
            {
                return fallbackValue;
            }
        }

        return default(T);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        try
        {
            // Try to set in primary cache
            await _primaryCache.SetAsync(key, value, expiration);
            
            // Also set in fallback cache for future failures
            var fallbackExpiration = expiration ?? TimeSpan.FromMinutes(15);
            _fallbackCache.Set(key, value, fallbackExpiration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary cache set failed for key {Key}, using fallback only", key);
            
            // At least store in fallback cache
            var fallbackExpiration = expiration ?? TimeSpan.FromMinutes(15);
            _fallbackCache.Set(key, value, fallbackExpiration);
        }
    }
}
```

### Database Fallback

```csharp
public class CacheWithDatabaseFallback<T> where T : class
{
    private readonly ICacheService _cache;
    private readonly Func<string, Task<T>> _databaseFetcher;
    private readonly ILogger<CacheWithDatabaseFallback<T>> _logger;

    public CacheWithDatabaseFallback(
        ICacheService cache, 
        Func<string, Task<T>> databaseFetcher,
        ILogger<CacheWithDatabaseFallback<T>> logger)
    {
        _cache = cache;
        _databaseFetcher = databaseFetcher;
        _logger = logger;
    }

    public async Task<T> GetAsync(string key, TimeSpan? cacheExpiration = null)
    {
        try
        {
            // Try cache first
            var cached = await _cache.GetAsync<T>(key);
            if (cached != null)
            {
                return cached;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache failed for key {Key}, falling back to database", key);
        }

        // Fallback to database
        try
        {
            var data = await _databaseFetcher(key);
            
            if (data != null)
            {
                // Try to cache the result for next time
                try
                {
                    await _cache.SetAsync(key, data, cacheExpiration ?? TimeSpan.FromMinutes(30));
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Failed to cache database result for key {Key}", key);
                }
            }
            
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Both cache and database failed for key {Key}", key);
            throw new DataAccessException($"Unable to retrieve data for key {key}", ex);
        }
    }
}

// Usage example
public class ProductService
{
    private readonly CacheWithDatabaseFallback<ProductDto> _productCache;

    public ProductService(ICacheService cache, IProductRepository repository, ILogger<ProductService> logger)
    {
        _productCache = new CacheWithDatabaseFallback<ProductDto>(
            cache,
            async key =>
            {
                var id = int.Parse(key.Split('_')[1]); // Extract ID from cache key
                var product = await repository.GetByIdAsync(id);
                return product?.ToDto();
            },
            logger);
    }

    public async Task<ProductDto> GetProductAsync(int id)
    {
        return await _productCache.GetAsync($"product_{id}", TimeSpan.FromMinutes(60));
    }
}
```

## Circuit Breaker Pattern

Prevent cascading failures with circuit breaker implementation.

### Circuit Breaker Configuration

```csharp
public class CacheCircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 5;
    public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan HalfOpenTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public int SuccessThreshold { get; set; } = 3;
}

public class CacheCircuitBreaker
{
    private readonly CacheCircuitBreakerOptions _options;
    private readonly ILogger<CacheCircuitBreaker> _logger;
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount = 0;
    private int _successCount = 0;
    private DateTime _lastFailureTime = DateTime.MinValue;
    private readonly object _lock = new object();

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, T fallbackValue = default(T))
    {
        if (ShouldBlock())
        {
            _logger.LogWarning("Circuit breaker is open, returning fallback value");
            return fallbackValue;
        }

        try
        {
            var result = await operation();
            RecordSuccess();
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure();
            
            if (_state == CircuitBreakerState.Open)
            {
                _logger.LogWarning(ex, "Circuit breaker opened due to repeated failures");
                return fallbackValue;
            }
            
            throw;
        }
    }

    private bool ShouldBlock()
    {
        lock (_lock)
        {
            switch (_state)
            {
                case CircuitBreakerState.Closed:
                    return false;

                case CircuitBreakerState.Open:
                    if (DateTime.UtcNow - _lastFailureTime >= _options.OpenTimeout)
                    {
                        _state = CircuitBreakerState.HalfOpen;
                        _successCount = 0;
                        _logger.LogInformation("Circuit breaker moving to half-open state");
                        return false;
                    }
                    return true;

                case CircuitBreakerState.HalfOpen:
                    return false;

                default:
                    return false;
            }
        }
    }

    private void RecordSuccess()
    {
        lock (_lock)
        {
            _failureCount = 0;
            
            if (_state == CircuitBreakerState.HalfOpen)
            {
                _successCount++;
                if (_successCount >= _options.SuccessThreshold)
                {
                    _state = CircuitBreakerState.Closed;
                    _logger.LogInformation("Circuit breaker closed after successful operations");
                }
            }
        }
    }

    private void RecordFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;
            
            if (_state == CircuitBreakerState.HalfOpen || _failureCount >= _options.FailureThreshold)
            {
                _state = CircuitBreakerState.Open;
                _logger.LogWarning("Circuit breaker opened. Failure count: {FailureCount}", _failureCount);
            }
        }
    }
}

public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}
```

### Using Circuit Breaker with Cache

```csharp
public class ResilientCacheService : ICacheService
{
    private readonly ICacheService _innerCache;
    private readonly CacheCircuitBreaker _circuitBreaker;
    private readonly ILogger<ResilientCacheService> _logger;

    public async Task<T> GetAsync<T>(string key)
    {
        return await _circuitBreaker.ExecuteAsync(
            async () => await _innerCache.GetAsync<T>(key),
            default(T));
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        await _circuitBreaker.ExecuteAsync(
            async () =>
            {
                await _innerCache.SetAsync(key, value, expiration);
                return true; // Success indicator
            },
            false); // Failure indicator
    }
}
```

## Retry Logic with Exponential Backoff

Implement intelligent retry mechanisms for transient failures.

### Retry Policy Configuration

```csharp
public class RetryPolicy
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    public double BackoffMultiplier { get; set; } = 2.0;
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
    public Func<Exception, bool> ShouldRetry { get; set; } = DefaultShouldRetry;

    private static bool DefaultShouldRetry(Exception ex)
    {
        return ex switch
        {
            TimeoutException => true,
            CacheConnectionException => true,
            SocketException => true,
            HttpRequestException => true,
            _ => false
        };
    }
}

public class RetryService
{
    private readonly RetryPolicy _policy;
    private readonly ILogger<RetryService> _logger;

    public async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName = null)
    {
        var attempt = 0;
        var delay = _policy.InitialDelay;

        while (true)
        {
            attempt++;
            
            try
            {
                var result = await operation();
                
                if (attempt > 1)
                {
                    _logger.LogInformation("Operation {OperationName} succeeded on attempt {Attempt}", 
                        operationName, attempt);
                }
                
                return result;
            }
            catch (Exception ex) when (attempt < _policy.MaxAttempts && _policy.ShouldRetry(ex))
            {
                _logger.LogWarning(ex, "Operation {OperationName} failed on attempt {Attempt}, retrying in {Delay}ms", 
                    operationName, attempt, delay.TotalMilliseconds);

                await Task.Delay(delay);
                
                // Exponential backoff with jitter
                delay = TimeSpan.FromMilliseconds(Math.Min(
                    delay.TotalMilliseconds * _policy.BackoffMultiplier * (0.8 + Random.Shared.NextDouble() * 0.4),
                    _policy.MaxDelay.TotalMilliseconds));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Operation {OperationName} failed after {Attempt} attempts", 
                    operationName, attempt);
                throw;
            }
        }
    }
}

// Usage with cache operations
public class ResilientCacheOperations
{
    private readonly ICacheService _cache;
    private readonly RetryService _retryService;

    public async Task<T> GetWithRetryAsync<T>(string key)
    {
        return await _retryService.ExecuteWithRetryAsync(
            async () => await _cache.GetAsync<T>(key),
            $"GetCache:{key}");
    }

    public async Task SetWithRetryAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        await _retryService.ExecuteWithRetryAsync(
            async () =>
            {
                await _cache.SetAsync(key, value, expiration);
                return true;
            },
            $"SetCache:{key}");
    }
}
```

## Error Logging and Monitoring

Comprehensive error tracking and monitoring.

### Structured Error Logging

```csharp
public class CacheErrorLogger : ICacheErrorLogger
{
    private readonly ILogger<CacheErrorLogger> _logger;
    private readonly IMetricsCollector _metrics;

    public void LogCacheError(CacheErrorContext context)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CacheKey"] = context.CacheKey,
            ["Operation"] = context.Operation.ToString(),
            ["AttemptNumber"] = context.AttemptNumber,
            ["ErrorType"] = context.Exception.GetType().Name,
            ["Duration"] = context.OperationDuration.TotalMilliseconds
        });

        var errorData = new
        {
            CacheKey = context.CacheKey,
            Operation = context.Operation.ToString(),
            ErrorType = context.Exception.GetType().Name,
            ErrorMessage = context.Exception.Message,
            Duration = context.OperationDuration.TotalMilliseconds,
            AttemptNumber = context.AttemptNumber,
            StackTrace = context.Exception.StackTrace,
            InnerException = context.Exception.InnerException?.Message
        };

        switch (GetErrorSeverity(context.Exception))
        {
            case ErrorSeverity.Critical:
                _logger.LogCritical(context.Exception, "Critical cache error: {@ErrorData}", errorData);
                break;
            case ErrorSeverity.Error:
                _logger.LogError(context.Exception, "Cache error: {@ErrorData}", errorData);
                break;
            case ErrorSeverity.Warning:
                _logger.LogWarning(context.Exception, "Cache warning: {@ErrorData}", errorData);
                break;
            case ErrorSeverity.Information:
                _logger.LogInformation("Cache operation completed with issues: {@ErrorData}", errorData);
                break;
        }

        // Record metrics
        _metrics.IncrementCounter("cache_errors_total", new Dictionary<string, string>
        {
            ["operation"] = context.Operation.ToString(),
            ["error_type"] = context.Exception.GetType().Name,
            ["cache_key_pattern"] = ExtractKeyPattern(context.CacheKey)
        });
    }

    private ErrorSeverity GetErrorSeverity(Exception exception)
    {
        return exception switch
        {
            ArgumentException => ErrorSeverity.Critical,
            CacheSerializationException => ErrorSeverity.Error,
            CacheConnectionException => ErrorSeverity.Warning,
            TimeoutException => ErrorSeverity.Warning,
            _ => ErrorSeverity.Error
        };
    }

    private string ExtractKeyPattern(string cacheKey)
    {
        // Extract pattern from cache key for better metrics grouping
        // Example: "user_123_profile" -> "user_{id}_profile"
        return System.Text.RegularExpressions.Regex.Replace(cacheKey, @"\d+", "{id}");
    }
}
```

### Error Dashboard

```csharp
[HttpGet("errors/dashboard")]
public async Task<IActionResult> GetErrorDashboard([FromServices] ICacheErrorAnalyzer analyzer)
{
    var analysis = await analyzer.AnalyzeErrorsAsync(TimeSpan.FromHours(24));
    
    return Ok(new
    {
        Summary = new
        {
            TotalErrors = analysis.TotalErrors,
            ErrorRate = analysis.ErrorRate,
            MostCommonErrors = analysis.MostCommonErrors.Take(10),
            CriticalErrors = analysis.CriticalErrors,
            TrendDirection = analysis.TrendDirection
        },
        
        ErrorsByType = analysis.ErrorsByType,
        ErrorsByOperation = analysis.ErrorsByOperation,
        ErrorsByTimeOfDay = analysis.ErrorsByTimeOfDay,
        
        AffectedKeys = analysis.MostAffectedKeys.Take(20),
        RecoveryMetrics = new
        {
            AverageRecoveryTime = analysis.AverageRecoveryTime,
            CircuitBreakerActivations = analysis.CircuitBreakerActivations,
            FallbackUsage = analysis.FallbackUsageCount
        },
        
        Recommendations = GenerateErrorRecommendations(analysis)
    });
}

private List<string> GenerateErrorRecommendations(CacheErrorAnalysis analysis)
{
    var recommendations = new List<string>();

    if (analysis.ErrorRate > 5.0)
    {
        recommendations.Add("High error rate detected. Consider implementing circuit breaker pattern.");
    }

    if (analysis.TimeoutErrors > analysis.TotalErrors * 0.3)
    {
        recommendations.Add("High timeout rate. Consider increasing timeout values or optimizing cache operations.");
    }

    if (analysis.SerializationErrors > 0)
    {
        recommendations.Add("Serialization errors found. Review object types being cached for serialization compatibility.");
    }

    if (analysis.ConnectionErrors > analysis.TotalErrors * 0.2)
    {
        recommendations.Add("Connection issues detected. Review Redis configuration and network connectivity.");
    }

    return recommendations;
}
```

## Best Practices

### 1. Fail Fast for Invalid Input

```csharp
public async Task<T> GetAsync<T>(string key)
{
    // Validate input immediately
    if (string.IsNullOrEmpty(key))
        throw new ArgumentException("Cache key cannot be null or empty", nameof(key));
    
    if (key.Length > 250)
        throw new ArgumentException("Cache key too long (max 250 characters)", nameof(key));
    
    try
    {
        return await _cache.GetAsync<T>(key);
    }
    catch (Exception ex)
    {
        // Handle cache-specific errors
        throw new CacheOperationException(key, CacheOperation.Get, ex);
    }
}
```

### 2. Use Appropriate Error Levels

```csharp
// Critical: System cannot function
_logger.LogCritical("Cache completely unavailable - all operations failing");

// Error: Operation failed but system can continue
_logger.LogError("Failed to cache user data for ID {UserId}", userId);

// Warning: Potential issue but operation succeeded
_logger.LogWarning("Cache operation took {Duration}ms - slower than expected", duration);

// Information: Normal operation with noteworthy event
_logger.LogInformation("Cache hit rate dropped to {HitRate}% - below target", hitRate);
```

### 3. Implement Health Checks

```csharp
public class CacheHealthCheck : IHealthCheck
{
    private readonly ICacheService _cache;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var testKey = $"health_check_{Guid.NewGuid()}";
            var testValue = "health_check_value";
            
            // Test write
            await _cache.SetAsync(testKey, testValue, TimeSpan.FromMinutes(1));
            
            // Test read
            var retrievedValue = await _cache.GetAsync<string>(testKey);
            
            // Test delete
            await _cache.RemoveAsync(testKey);
            
            return retrievedValue == testValue 
                ? HealthCheckResult.Healthy("Cache is functioning normally")
                : HealthCheckResult.Degraded("Cache read/write test failed");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cache health check failed", ex);
        }
    }
}
```

For related topics:
- **[Troubleshooting]({{ site.baseurl }}/advanced/troubleshooting)** - Common issues and solutions
- **[High Availability]({{ site.baseurl }}/distributed/high-availability)** - Resilience patterns
- **[Performance Metrics]({{ site.baseurl }}/monitoring/performance-metrics)** - Error monitoring
- **[Production Tuning]({{ site.baseurl }}/performance/production-tuning)** - Optimize error handling