namespace Athena.Invalidation.Core.Resilience;

/// <summary>
/// 재시도 패턴을 적용한 무효화 엔진 데코레이터
/// </summary>
public class RetryInvalidationEngine(
    IInvalidationEngine innerEngine,
    ILogger<RetryInvalidationEngine> logger,
    IOptions<RetryOptions> options)
    : IInvalidationEngine, IAsyncDisposable
{
    private readonly IInvalidationEngine _innerEngine = innerEngine ?? throw new ArgumentNullException(nameof(innerEngine));
    private readonly ILogger<RetryInvalidationEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly RetryOptions _options = options.Value ?? new RetryOptions();
    
    private volatile bool _disposed = false;

    public async Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RetryInvalidationEngine));
        
        await ExecuteWithRetry($"InvalidateByTable({tableName})", 
            () => _innerEngine.InvalidateByTableAsync(tableName, cancellationToken));
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RetryInvalidationEngine));
        
        await ExecuteWithRetry($"InvalidateByPattern({pattern})", 
            () => _innerEngine.InvalidateByPatternAsync(pattern, cancellationToken));
    }

    public async Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RetryInvalidationEngine));
        
        await ExecuteWithRetry($"InvalidateByKey({key})", 
            () => _innerEngine.InvalidateByKeyAsync(key, cancellationToken));
    }

    public async Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RetryInvalidationEngine));
        
        var tableArray = tableNames.ToArray();
        await ExecuteWithRetry($"InvalidateBatch({tableArray.Length} tables)", 
            () => _innerEngine.InvalidateBatchAsync(tableArray, cancellationToken));
    }

    public async Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RetryInvalidationEngine));
        
        await ExecuteWithRetry($"InvalidateHierarchy({tableName})", 
            () => _innerEngine.InvalidateHierarchyAsync(tableName, relatedTables, maxDepth, cancellationToken));
    }

    public async Task InvalidateOnCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) 
        where TCommand : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RetryInvalidationEngine));
        
        await ExecuteWithRetry($"InvalidateOnCommand({typeof(TCommand).Name})", 
            () => _innerEngine.InvalidateOnCommandAsync(command, cancellationToken));
    }

    public async Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) 
        where TEvent : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RetryInvalidationEngine));
        
        await ExecuteWithRetry($"InvalidateOnEvent({typeof(TEvent).Name})", 
            () => _innerEngine.InvalidateOnEventAsync(domainEvent, cancellationToken));
    }

    private async Task ExecuteWithRetry(string operationName, Func<Task> operation)
    {
        var attempt = 0;
        var exceptions = new List<Exception>();

        while (attempt < _options.MaxRetryAttempts)
        {
            try
            {
                await operation();
                
                if (attempt > 0)
                {
                    _logger.LogInformation("Operation {OperationName} succeeded after {AttemptCount} attempts", 
                        operationName, attempt + 1);
                }
                
                return; // 성공
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt))
            {
                attempt++;
                exceptions.Add(ex);
                
                _logger.LogWarning(ex, "Operation {OperationName} failed on attempt {AttemptNumber}/{MaxAttempts}. Retrying after {Delay}ms", 
                    operationName, attempt, _options.MaxRetryAttempts, GetRetryDelay(attempt).TotalMilliseconds);

                if (attempt < _options.MaxRetryAttempts)
                {
                    var delay = GetRetryDelay(attempt);
                    await Task.Delay(delay);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Operation {OperationName} failed with non-retryable exception on attempt {AttemptNumber}", 
                    operationName, attempt + 1);
                throw;
            }
        }

        // 모든 재시도가 실패한 경우
        var aggregateException = new AggregateException($"Operation {operationName} failed after {_options.MaxRetryAttempts} attempts", exceptions);
        _logger.LogError(aggregateException, "Operation {OperationName} ultimately failed after {MaxAttempts} attempts", 
            operationName, _options.MaxRetryAttempts);
        
        throw aggregateException;
    }

    private bool ShouldRetry(Exception exception, int currentAttempt)
    {
        // 최대 재시도 횟수 확인
        if (currentAttempt >= _options.MaxRetryAttempts)
            return false;

        // 재시도하지 않을 예외 타입 확인
        if (_options.NonRetryableExceptions.Any(type => type.IsInstanceOfType(exception)))
        {
            _logger.LogDebug("Exception {ExceptionType} is non-retryable", exception.GetType().Name);
            return false;
        }

        // 재시도할 예외 타입 확인 (지정된 경우만)
        if (_options.RetryableExceptions.Count > 0 && 
            !_options.RetryableExceptions.Any(type => type.IsInstanceOfType(exception)))
        {
            _logger.LogDebug("Exception {ExceptionType} is not in retryable exceptions list", exception.GetType().Name);
            return false;
        }

        // 사용자 정의 재시도 조건 확인
        if (_options.RetryPredicate != null && !_options.RetryPredicate(exception))
        {
            _logger.LogDebug("Custom retry predicate returned false for exception {ExceptionType}", exception.GetType().Name);
            return false;
        }

        return true;
    }

    private TimeSpan GetRetryDelay(int attemptNumber)
    {
        return _options.DelayStrategy switch
        {
            RetryDelayStrategy.Fixed => _options.BaseDelay,
            RetryDelayStrategy.Linear => TimeSpan.FromMilliseconds(_options.BaseDelay.TotalMilliseconds * attemptNumber),
            RetryDelayStrategy.Exponential => TimeSpan.FromMilliseconds(
                _options.BaseDelay.TotalMilliseconds * Math.Pow(2, attemptNumber - 1)),
            RetryDelayStrategy.ExponentialWithJitter => GetExponentialWithJitterDelay(attemptNumber),
            _ => _options.BaseDelay
        };
    }

    private TimeSpan GetExponentialWithJitterDelay(int attemptNumber)
    {
        var exponentialDelay = _options.BaseDelay.TotalMilliseconds * Math.Pow(2, attemptNumber - 1);
        var maxJitter = exponentialDelay * _options.JitterFactor;
        var jitter = Random.Shared.NextDouble() * maxJitter;
        
        var delayMs = Math.Min(exponentialDelay + jitter, _options.MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(delayMs);
    }

    // 나머지 메서드들은 재시도 없이 바로 위임
    public Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default) 
        where TReadModel : class
    {
        return _innerEngine.InvalidateReadModelAsync<TReadModel>(modelId, cancellationToken);
    }

    public Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default) 
        where TProjection : class
    {
        return _innerEngine.InvalidateProjectionAsync<TProjection>(projectionId, cancellationToken);
    }

    public Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default)
    {
        return _innerEngine.TrackCacheKeyAsync(tableName, cacheKey, cancellationToken);
    }

    public Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default)
    {
        return _innerEngine.TrackCacheKeyAsync(tableNames, cacheKey, cancellationToken);
    }

    public Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default)
    {
        return _innerEngine.GetTrackedKeysAsync(tableName, cancellationToken);
    }

    public Task RegisterInvalidationRuleAsync(IInvalidationRule rule, CancellationToken cancellationToken = default)
    {
        return _innerEngine.RegisterInvalidationRuleAsync(rule, cancellationToken);
    }

    public Task UnregisterInvalidationRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        return _innerEngine.UnregisterInvalidationRuleAsync(ruleId, cancellationToken);
    }

    public Task<IEnumerable<IInvalidationRule>> GetInvalidationRulesAsync(CancellationToken cancellationToken = default)
    {
        return _innerEngine.GetInvalidationRulesAsync(cancellationToken);
    }

    public IInvalidationContext CreateContext(InvalidationTrigger trigger, object? metadata = null)
    {
        return _innerEngine.CreateContext(trigger, metadata);
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RetryInvalidationEngine));
        
        await ExecuteWithRetry("ClearAll", 
            () => _innerEngine.ClearAllAsync(cancellationToken));
    }

    public Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return _innerEngine.GetStatusAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            if (_innerEngine is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_innerEngine is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during RetryInvalidationEngine disposal");
        }
    }
}

/// <summary>
/// 재시도 지연 전략
/// </summary>
public enum RetryDelayStrategy
{
    Fixed,                    // 고정 지연
    Linear,                   // 선형 증가
    Exponential,              // 지수 증가
    ExponentialWithJitter     // 지수 증가 + 지터
}

/// <summary>
/// 재시도 옵션
/// </summary>
public class RetryOptions
{
    /// <summary>최대 재시도 횟수</summary>
    public int MaxRetryAttempts { get; set; } = 3;
    
    /// <summary>기본 지연 시간</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    
    /// <summary>최대 지연 시간</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>지연 전략</summary>
    public RetryDelayStrategy DelayStrategy { get; set; } = RetryDelayStrategy.ExponentialWithJitter;
    
    /// <summary>지터 팩터 (0.0 ~ 1.0)</summary>
    public double JitterFactor { get; set; } = 0.2;
    
    /// <summary>재시도 가능한 예외 타입들 (비어있으면 모든 예외 재시도)</summary>
    public List<Type> RetryableExceptions { get; set; } = [];
    
    /// <summary>재시도하지 않을 예외 타입들</summary>
    public List<Type> NonRetryableExceptions { get; set; } =
    [
        typeof(ArgumentException),
        typeof(ArgumentNullException),
        typeof(NotSupportedException),
        typeof(ObjectDisposedException)
    ];
    
    /// <summary>커스텀 재시도 조건</summary>
    public Func<Exception, bool>? RetryPredicate { get; set; }
}

/// <summary>
/// 일반적인 재시도 정책들
/// </summary>
public static class RetryPolicies
{
    /// <summary>네트워크 관련 재시도 정책</summary>
    public static RetryOptions NetworkRetry => new()
    {
        MaxRetryAttempts = 3,
        BaseDelay = TimeSpan.FromMilliseconds(500),
        MaxDelay = TimeSpan.FromSeconds(10),
        DelayStrategy = RetryDelayStrategy.ExponentialWithJitter,
        RetryableExceptions =
        [
            typeof(HttpRequestException),
            typeof(SocketException),
            typeof(TimeoutException),
            typeof(TaskCanceledException)
        ]
    };

    /// <summary>데이터베이스 관련 재시도 정책</summary>
    public static RetryOptions DatabaseRetry => new()
    {
        MaxRetryAttempts = 5,
        BaseDelay = TimeSpan.FromMilliseconds(200),
        MaxDelay = TimeSpan.FromSeconds(5),
        DelayStrategy = RetryDelayStrategy.Exponential,
        RetryPredicate = ex => 
        {
            // SQL 일시적 오류나 연결 문제만 재시도
            var message = ex.Message.ToLowerInvariant();
            return message.Contains("timeout") || 
                   message.Contains("connection") || 
                   message.Contains("deadlock") ||
                   message.Contains("lock timeout");
        }
    };

    /// <summary>빠른 재시도 정책 (고성능 환경용)</summary>
    public static RetryOptions FastRetry => new()
    {
        MaxRetryAttempts = 2,
        BaseDelay = TimeSpan.FromMilliseconds(50),
        MaxDelay = TimeSpan.FromMilliseconds(500),
        DelayStrategy = RetryDelayStrategy.Linear
    };

    /// <summary>관대한 재시도 정책 (안정성 우선)</summary>
    public static RetryOptions RobustRetry => new()
    {
        MaxRetryAttempts = 10,
        BaseDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromMinutes(2),
        DelayStrategy = RetryDelayStrategy.ExponentialWithJitter,
        JitterFactor = 0.3
    };
}
