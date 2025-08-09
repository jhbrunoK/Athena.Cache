using Athena.Invalidation.Core.Abstractions;
using System.Collections.Concurrent;

namespace Athena.Invalidation.Core.Resilience;

/// <summary>
/// 서킷 브레이커 패턴을 적용한 무효화 엔진 데코레이터
/// </summary>
public class CircuitBreakerInvalidationEngine : IInvalidationEngine, IAsyncDisposable
{
    private readonly IInvalidationEngine _innerEngine;
    private readonly ILogger<CircuitBreakerInvalidationEngine> _logger;
    private readonly CircuitBreakerOptions _options;
    
    private readonly ConcurrentDictionary<string, CircuitBreakerState> _circuitStates = new();
    private readonly Timer _healthCheckTimer;
    
    private volatile bool _disposed = false;

    public CircuitBreakerInvalidationEngine(
        IInvalidationEngine innerEngine,
        ILogger<CircuitBreakerInvalidationEngine> logger,
        IOptions<CircuitBreakerOptions> options)
    {
        _innerEngine = innerEngine ?? throw new ArgumentNullException(nameof(innerEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new CircuitBreakerOptions();
        
        // 주기적 헬스체크 타이머
        _healthCheckTimer = new Timer(PerformHealthCheck, null, 
            _options.HealthCheckInterval, _options.HealthCheckInterval);
    }

    public async Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CircuitBreakerInvalidationEngine));
        
        await ExecuteWithCircuitBreaker("table", 
            () => _innerEngine.InvalidateByTableAsync(tableName, cancellationToken));
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CircuitBreakerInvalidationEngine));
        
        await ExecuteWithCircuitBreaker("pattern", 
            () => _innerEngine.InvalidateByPatternAsync(pattern, cancellationToken));
    }

    public async Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CircuitBreakerInvalidationEngine));
        
        await ExecuteWithCircuitBreaker("key", 
            () => _innerEngine.InvalidateByKeyAsync(key, cancellationToken));
    }

    public async Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CircuitBreakerInvalidationEngine));
        
        await ExecuteWithCircuitBreaker("batch", 
            () => _innerEngine.InvalidateBatchAsync(tableNames, cancellationToken));
    }

    public async Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CircuitBreakerInvalidationEngine));
        
        await ExecuteWithCircuitBreaker("hierarchy", 
            () => _innerEngine.InvalidateHierarchyAsync(tableName, relatedTables, maxDepth, cancellationToken));
    }

    public async Task InvalidateOnCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) 
        where TCommand : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CircuitBreakerInvalidationEngine));
        
        await ExecuteWithCircuitBreaker("command", 
            () => _innerEngine.InvalidateOnCommandAsync(command, cancellationToken));
    }

    public async Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) 
        where TEvent : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CircuitBreakerInvalidationEngine));
        
        await ExecuteWithCircuitBreaker("event", 
            () => _innerEngine.InvalidateOnEventAsync(domainEvent, cancellationToken));
    }

    private async Task ExecuteWithCircuitBreaker(string operationType, Func<Task> operation)
    {
        var circuitState = GetOrCreateCircuitState(operationType);
        
        // 서킷이 열려있는 경우
        if (circuitState.State == CircuitState.Open)
        {
            if (DateTimeOffset.UtcNow < circuitState.NextRetryAt)
            {
                _logger.LogWarning("Circuit breaker is OPEN for {OperationType}. Failing fast.", operationType);
                throw new CircuitBreakerOpenException($"Circuit breaker is open for {operationType}");
            }
            
            // Half-Open 상태로 전환
            circuitState.State = CircuitState.HalfOpen;
            circuitState.HalfOpenAttempts = 0;
            _logger.LogInformation("Circuit breaker transitioning to HALF-OPEN for {OperationType}", operationType);
        }

        var startTime = DateTimeOffset.UtcNow;
        var success = false;
        Exception? lastException = null;

        try
        {
            await operation();
            success = true;
            
            // 성공 시 서킷 상태 업데이트
            OnOperationSuccess(circuitState, operationType);
        }
        catch (Exception ex)
        {
            lastException = ex;
            
            // 실패 시 서킷 상태 업데이트
            OnOperationFailure(circuitState, operationType, ex);
            
            throw;
        }
        finally
        {
            var duration = DateTimeOffset.UtcNow - startTime;
            RecordOperationResult(circuitState, success, duration);
        }
    }

    private CircuitBreakerState GetOrCreateCircuitState(string operationType)
    {
        return _circuitStates.GetOrAdd(operationType, _ => new CircuitBreakerState
        {
            State = CircuitState.Closed,
            OperationType = operationType
        });
    }

    private void OnOperationSuccess(CircuitBreakerState circuitState, string operationType)
    {
        lock (circuitState.Lock)
        {
            circuitState.SuccessCount++;
            circuitState.ConsecutiveFailures = 0;
            circuitState.LastSuccessAt = DateTimeOffset.UtcNow;

            if (circuitState.State == CircuitState.HalfOpen)
            {
                circuitState.HalfOpenAttempts++;
                
                // Half-Open에서 충분한 성공을 확인했으면 닫힘
                if (circuitState.HalfOpenAttempts >= _options.MinSuccessfulCallsInHalfOpen)
                {
                    circuitState.State = CircuitState.Closed;
                    circuitState.FailureCount = 0;
                    circuitState.HalfOpenAttempts = 0;
                    
                    _logger.LogInformation("Circuit breaker CLOSED for {OperationType} after successful recovery", operationType);
                }
            }
        }
    }

    private void OnOperationFailure(CircuitBreakerState circuitState, string operationType, Exception exception)
    {
        lock (circuitState.Lock)
        {
            circuitState.FailureCount++;
            circuitState.ConsecutiveFailures++;
            circuitState.LastFailureAt = DateTimeOffset.UtcNow;
            circuitState.LastException = exception;

            // 실패 임계값 도달 시 서킷 열기
            if (circuitState.ConsecutiveFailures >= _options.FailureThreshold && 
                circuitState.State == CircuitState.Closed)
            {
                circuitState.State = CircuitState.Open;
                circuitState.NextRetryAt = DateTimeOffset.UtcNow.Add(_options.OpenDuration);
                
                _logger.LogWarning("Circuit breaker OPENED for {OperationType} after {FailureCount} consecutive failures. Next retry at {NextRetryAt}", 
                    operationType, circuitState.ConsecutiveFailures, circuitState.NextRetryAt);
            }
            else if (circuitState.State == CircuitState.HalfOpen)
            {
                // Half-Open에서 실패하면 다시 열림
                circuitState.State = CircuitState.Open;
                circuitState.NextRetryAt = DateTimeOffset.UtcNow.Add(_options.OpenDuration);
                circuitState.HalfOpenAttempts = 0;
                
                _logger.LogWarning("Circuit breaker back to OPEN for {OperationType} after failure in half-open state", operationType);
            }
        }
    }

    private void RecordOperationResult(CircuitBreakerState circuitState, bool success, TimeSpan duration)
    {
        lock (circuitState.Lock)
        {
            circuitState.TotalCalls++;
            circuitState.RecentCallResults.Enqueue(new CallResult 
            { 
                Success = success, 
                Timestamp = DateTimeOffset.UtcNow, 
                Duration = duration 
            });

            // 최근 호출 결과 큐 크기 제한
            while (circuitState.RecentCallResults.Count > _options.SlidingWindowSize)
            {
                circuitState.RecentCallResults.Dequeue();
            }
        }
    }

    private async void PerformHealthCheck(object? state)
    {
        if (_disposed) return;

        try
        {
            foreach (var kvp in _circuitStates)
            {
                var circuitState = kvp.Value;
                var operationType = kvp.Key;

                lock (circuitState.Lock)
                {
                    // 오래된 실패 기록 정리
                    var cutoffTime = DateTimeOffset.UtcNow.Subtract(_options.SlidingWindowDuration);
                    while (circuitState.RecentCallResults.Count > 0 && 
                           circuitState.RecentCallResults.Peek().Timestamp < cutoffTime)
                    {
                        circuitState.RecentCallResults.Dequeue();
                    }

                    // 슬라이딩 윈도우 기반 실패율 계산
                    if (circuitState.RecentCallResults.Count >= _options.MinCallsInSlidingWindow)
                    {
                        var recentCalls = circuitState.RecentCallResults.ToArray();
                        var failureRate = recentCalls.Count(c => !c.Success) / (double)recentCalls.Length;
                        
                        circuitState.CurrentFailureRate = failureRate;

                        // 실패율이 임계값을 초과하고 서킷이 닫혀있으면 열기
                        if (failureRate >= _options.FailureRateThreshold && 
                            circuitState.State == CircuitState.Closed)
                        {
                            circuitState.State = CircuitState.Open;
                            circuitState.NextRetryAt = DateTimeOffset.UtcNow.Add(_options.OpenDuration);
                            
                            _logger.LogWarning("Circuit breaker OPENED for {OperationType} due to high failure rate: {FailureRate:P2}", 
                                operationType, failureRate);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during circuit breaker health check");
        }
    }

    // 나머지 메서드들은 서킷 브레이커 없이 바로 위임
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
        if (_disposed) throw new ObjectDisposedException(nameof(CircuitBreakerInvalidationEngine));
        
        await ExecuteWithCircuitBreaker("clear_all", 
            () => _innerEngine.ClearAllAsync(cancellationToken));
    }

    public async Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await _innerEngine.GetStatusAsync(cancellationToken);
        
        // 서킷 브레이커 상태 정보 추가
        foreach (var kvp in _circuitStates)
        {
            var circuitState = kvp.Value;
            status.Metrics[$"CircuitBreaker_{kvp.Key}_State"] = circuitState.State.ToString();
            status.Metrics[$"CircuitBreaker_{kvp.Key}_FailureRate"] = circuitState.CurrentFailureRate;
            status.Metrics[$"CircuitBreaker_{kvp.Key}_TotalCalls"] = circuitState.TotalCalls;
            status.Metrics[$"CircuitBreaker_{kvp.key}_FailureCount"] = circuitState.FailureCount;
        }
        
        return status;
    }

    /// <summary>서킷 브레이커 상태 조회</summary>
    public Dictionary<string, CircuitBreakerState> GetCircuitBreakerStates()
    {
        return new Dictionary<string, CircuitBreakerState>(_circuitStates);
    }

    /// <summary>특정 타입의 서킷 브레이커 수동 리셋</summary>
    public void ResetCircuitBreaker(string operationType)
    {
        if (_circuitStates.TryGetValue(operationType, out var circuitState))
        {
            lock (circuitState.Lock)
            {
                circuitState.State = CircuitState.Closed;
                circuitState.FailureCount = 0;
                circuitState.ConsecutiveFailures = 0;
                circuitState.HalfOpenAttempts = 0;
                circuitState.RecentCallResults.Clear();
                circuitState.LastException = null;
                
                _logger.LogInformation("Circuit breaker manually reset for {OperationType}", operationType);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            _healthCheckTimer?.Dispose();

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
            _logger.LogError(ex, "Error during CircuitBreakerInvalidationEngine disposal");
        }
    }
}

/// <summary>
/// 서킷 브레이커 상태
/// </summary>
public class CircuitBreakerState
{
    public CircuitState State { get; set; } = CircuitState.Closed;
    public string OperationType { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public int SuccessCount { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int TotalCalls { get; set; }
    public int HalfOpenAttempts { get; set; }
    public double CurrentFailureRate { get; set; }
    public DateTimeOffset NextRetryAt { get; set; }
    public DateTimeOffset LastFailureAt { get; set; }
    public DateTimeOffset LastSuccessAt { get; set; }
    public Exception? LastException { get; set; }
    public Queue<CallResult> RecentCallResults { get; set; } = new();
    public object Lock { get; } = new();
}

/// <summary>
/// 서킷 상태
/// </summary>
public enum CircuitState
{
    Closed,   // 정상 동작
    Open,     // 실패로 인해 호출 차단
    HalfOpen  // 복구 시도 중
}

/// <summary>
/// 호출 결과
/// </summary>
public class CallResult
{
    public bool Success { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// 서킷 브레이커 열림 예외
/// </summary>
public class CircuitBreakerOpenException : InvalidOperationException
{
    public CircuitBreakerOpenException(string message) : base(message) { }
    public CircuitBreakerOpenException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// 서킷 브레이커 옵션
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>연속 실패 임계값</summary>
    public int FailureThreshold { get; set; } = 5;
    
    /// <summary>실패율 임계값 (0.0 ~ 1.0)</summary>
    public double FailureRateThreshold { get; set; } = 0.5;
    
    /// <summary>서킷 열림 지속 시간</summary>
    public TimeSpan OpenDuration { get; set; } = TimeSpan.FromMinutes(1);
    
    /// <summary>Half-Open에서 성공해야 하는 최소 호출 수</summary>
    public int MinSuccessfulCallsInHalfOpen { get; set; } = 3;
    
    /// <summary>슬라이딩 윈도우 크기</summary>
    public int SlidingWindowSize { get; set; } = 100;
    
    /// <summary>슬라이딩 윈도우 기간</summary>
    public TimeSpan SlidingWindowDuration { get; set; } = TimeSpan.FromMinutes(10);
    
    /// <summary>슬라이딩 윈도우에서 최소 호출 수</summary>
    public int MinCallsInSlidingWindow { get; set; } = 10;
    
    /// <summary>헬스체크 간격</summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
}