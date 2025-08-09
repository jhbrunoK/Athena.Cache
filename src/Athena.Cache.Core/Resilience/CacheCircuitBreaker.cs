using System.Collections.Concurrent;

namespace Athena.Cache.Core.Resilience;

/// <summary>
/// 캐시 작업을 위한 Circuit Breaker 패턴 구현
/// 캐시 오류가 임계치를 초과하면 자동으로 캐시를 우회하여 시스템 안정성 보장
/// </summary>
public class CacheCircuitBreaker : IDisposable
{
    private readonly ILogger<CacheCircuitBreaker> _logger;
    private readonly CacheCircuitBreakerOptions _options;
    
    private volatile CircuitBreakerState _state = CircuitBreakerState.Closed;
    private volatile int _failureCount = 0;
    private long _lastFailureTimeTicks = DateTime.MinValue.Ticks;
    private long _lastSuccessTimeTicks = DateTime.UtcNow.Ticks;
    
    private readonly object _stateLock = new();
    private readonly Timer _healthCheckTimer;
    private readonly ConcurrentDictionary<string, OperationMetrics> _operationMetrics = new();

    public CircuitBreakerState State => _state;
    public int FailureCount => _failureCount;
    public DateTime LastFailureTime => new DateTime(Interlocked.Read(ref _lastFailureTimeTicks));
    public DateTime LastSuccessTime => new DateTime(Interlocked.Read(ref _lastSuccessTimeTicks));
    public bool IsOpen => _state == CircuitBreakerState.Open;
    public bool IsHalfOpen => _state == CircuitBreakerState.HalfOpen;
    public bool IsClosed => _state == CircuitBreakerState.Closed;

    public event EventHandler<CircuitBreakerStateChangedEventArgs>? StateChanged;

    public CacheCircuitBreaker(
        CacheCircuitBreakerOptions options,
        ILogger<CacheCircuitBreaker> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 주기적으로 상태 확인 및 정리
        _healthCheckTimer = new Timer(PerformHealthCheck, null, 
            _options.HealthCheckInterval, _options.HealthCheckInterval);
        
        _logger.LogInformation("CacheCircuitBreaker initialized with threshold: {Threshold}, timeout: {Timeout}",
            _options.FailureThreshold, _options.Timeout);
    }

    #region Circuit Breaker Core Logic

    /// <summary>
    /// 캐시 작업을 Circuit Breaker로 보호하여 실행
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        string operationName,
        Func<Task<T>> operation,
        Func<Task<T>>? fallback = null,
        CancellationToken cancellationToken = default)
    {
        // Circuit이 Open 상태인지 확인
        if (!CanExecute())
        {
            _logger.LogWarning("Circuit breaker is open for operation: {Operation}", operationName);
            return await ExecuteFallback(operationName, fallback, cancellationToken);
        }

        var metrics = GetOrCreateOperationMetrics(operationName);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await operation().ConfigureAwait(false);
            stopwatch.Stop();
            
            // 성공 기록
            RecordSuccess(operationName, stopwatch.Elapsed);
            metrics.RecordSuccess(stopwatch.Elapsed);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            // 실패 기록
            RecordFailure(operationName, ex, stopwatch.Elapsed);
            metrics.RecordFailure(ex, stopwatch.Elapsed);
            
            // Fallback 실행
            if (fallback != null)
            {
                _logger.LogWarning(ex, "Cache operation failed, executing fallback for: {Operation}", operationName);
                return await ExecuteFallback(operationName, fallback, cancellationToken);
            }
            
            throw;
        }
    }

    /// <summary>
    /// 동기 버전의 Circuit Breaker 실행
    /// </summary>
    public T Execute<T>(
        string operationName,
        Func<T> operation,
        Func<T>? fallback = null)
    {
        if (!CanExecute())
        {
            _logger.LogWarning("Circuit breaker is open for operation: {Operation}", operationName);
            return ExecuteFallbackSync(operationName, fallback);
        }

        var metrics = GetOrCreateOperationMetrics(operationName);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = operation();
            stopwatch.Stop();
            
            RecordSuccess(operationName, stopwatch.Elapsed);
            metrics.RecordSuccess(stopwatch.Elapsed);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            RecordFailure(operationName, ex, stopwatch.Elapsed);
            metrics.RecordFailure(ex, stopwatch.Elapsed);
            
            if (fallback != null)
            {
                _logger.LogWarning(ex, "Cache operation failed, executing fallback for: {Operation}", operationName);
                return ExecuteFallbackSync(operationName, fallback);
            }
            
            throw;
        }
    }

    private bool CanExecute()
    {
        switch (_state)
        {
            case CircuitBreakerState.Closed:
                return true;
            
            case CircuitBreakerState.Open:
                // Timeout이 지났는지 확인
                var lastFailureTime = new DateTime(Interlocked.Read(ref _lastFailureTimeTicks));
                if (DateTime.UtcNow - lastFailureTime >= _options.Timeout)
                {
                    ChangeState(CircuitBreakerState.HalfOpen);
                    return true;
                }
                return false; 
            
            case CircuitBreakerState.HalfOpen:
                return true;
            
            default:
                return false;
        }
    }

    #endregion

    #region State Management

    private void RecordSuccess(string operationName, TimeSpan duration)
    {
        Interlocked.Exchange(ref _lastSuccessTimeTicks, DateTime.UtcNow.Ticks);
        
        lock (_stateLock)
        {
            if (_state == CircuitBreakerState.HalfOpen)
            {
                // Half-Open에서 성공하면 Closed로 변경
                _failureCount = 0;
                ChangeState(CircuitBreakerState.Closed);
            }
            else if (_state == CircuitBreakerState.Closed)
            {
                // 연속 실패 카운터 리셋
                if (_failureCount > 0)
                {
                    _failureCount = Math.Max(0, _failureCount - 1);
                }
            }
        }
        
        _logger.LogDebug("Cache operation succeeded: {Operation} in {Duration}ms", 
            operationName, duration.TotalMilliseconds);
    }

    private void RecordFailure(string operationName, Exception exception, TimeSpan duration)
    {
        Interlocked.Exchange(ref _lastFailureTimeTicks, DateTime.UtcNow.Ticks);
        
        lock (_stateLock)
        {
            _failureCount++;
            
            if (_state == CircuitBreakerState.HalfOpen)
            {
                // Half-Open에서 실패하면 바로 Open으로
                ChangeState(CircuitBreakerState.Open);
            }
            else if (_state == CircuitBreakerState.Closed && _failureCount >= _options.FailureThreshold)
            {
                // 실패 임계치 초과 시 Open으로
                ChangeState(CircuitBreakerState.Open);
            }
        }
        
        _logger.LogWarning(exception, "Cache operation failed: {Operation} in {Duration}ms (Failure #{Count})", 
            operationName, duration.TotalMilliseconds, _failureCount);
    }

    private void ChangeState(CircuitBreakerState newState)
    {
        var oldState = _state;
        _state = newState;
        
        _logger.LogInformation("Circuit breaker state changed from {OldState} to {NewState}", oldState, newState);
        
        StateChanged?.Invoke(this, new CircuitBreakerStateChangedEventArgs
        {
            OldState = oldState,
            NewState = newState,
            FailureCount = _failureCount,
            Timestamp = DateTime.UtcNow
        });
    }

    #endregion

    #region Fallback Execution

    private async Task<T> ExecuteFallback<T>(
        string operationName,
        Func<Task<T>>? fallback,
        CancellationToken cancellationToken)
    {
        if (fallback == null)
        {
            throw new CacheCircuitBreakerOpenException(
                $"Circuit breaker is open for operation '{operationName}' and no fallback provided");
        }

        try
        {
            var result = await fallback().ConfigureAwait(false); 
            _logger.LogDebug("Fallback executed successfully for operation: {Operation}", operationName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallback failed for operation: {Operation}", operationName);
            throw new CacheCircuitBreakerFallbackException(
                $"Both primary operation and fallback failed for '{operationName}'", ex);
        }
    }

    private T ExecuteFallbackSync<T>(string operationName, Func<T>? fallback)
    {
        if (fallback == null)
        {
            throw new CacheCircuitBreakerOpenException(
                $"Circuit breaker is open for operation '{operationName}' and no fallback provided");
        }

        try
        {
            var result = fallback();
            _logger.LogDebug("Fallback executed successfully for operation: {Operation}", operationName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallback failed for operation: {Operation}", operationName);
            throw new CacheCircuitBreakerFallbackException(
                $"Both primary operation and fallback failed for '{operationName}'", ex);
        }
    }

    #endregion

    #region Metrics and Health Check

    private OperationMetrics GetOrCreateOperationMetrics(string operationName)
    {
        return _operationMetrics.GetOrAdd(operationName, _ => new OperationMetrics(operationName));
    }

    public CircuitBreakerStatistics GetStatistics()
    {
        var operationStats = _operationMetrics.Values
            .Select(m => m.GetSnapshot())
            .ToArray();

        return new CircuitBreakerStatistics
        {
            State = _state,
            FailureCount = _failureCount,
            LastFailureTime = new DateTime(Interlocked.Read(ref _lastFailureTimeTicks)),
            LastSuccessTime = new DateTime(Interlocked.Read(ref _lastSuccessTimeTicks)),
            OperationMetrics = operationStats,
            TotalOperations = operationStats.Sum(s => s.TotalOperations),
            TotalFailures = operationStats.Sum(s => s.FailureCount),
            AverageResponseTime = operationStats.Any() 
                ? TimeSpan.FromMilliseconds(operationStats.Average(s => s.AverageResponseTime.TotalMilliseconds))
                : TimeSpan.Zero
        };
    }

    private void PerformHealthCheck(object? state)
    {
        try
        {
            // 오래된 메트릭 정리
            var cutoff = DateTime.UtcNow.Subtract(_options.MetricRetentionPeriod);
            var expiredOperations = _operationMetrics
                .Where(kvp => kvp.Value.LastAccess < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var operation in expiredOperations)
            {
                _operationMetrics.TryRemove(operation, out _);
            }

            // 자동 복구 체크 (추가 안전장치)
            if (_state == CircuitBreakerState.Open)
            {
                var lastFailureTime = new DateTime(Interlocked.Read(ref _lastFailureTimeTicks));
                var timeSinceLastFailure = DateTime.UtcNow - lastFailureTime;
                if (timeSinceLastFailure >= _options.Timeout * 2) // Timeout의 2배가 지나면 강제 Half-Open
                {
                    _logger.LogInformation("Auto-recovery triggered: changing to Half-Open state");
                    ChangeState(CircuitBreakerState.HalfOpen);
                }
            }

            _logger.LogDebug("Circuit breaker health check completed. State: {State}, Failures: {Failures}", 
                _state, _failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during circuit breaker health check");
        }
    }

    #endregion

    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        _operationMetrics.Clear();
        _logger.LogInformation("CacheCircuitBreaker disposed");
    }
}

#region Configuration and Models

public class CacheCircuitBreakerOptions
{
    /// <summary>Circuit을 Open하기 위한 연속 실패 임계치</summary>
    public int FailureThreshold { get; set; } = 5;
    
    /// <summary>Open 상태에서 Half-Open으로 전환하기 위한 대기 시간</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);
    
    /// <summary>헬스 체크 주기</summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>메트릭 보존 기간</summary>
    public TimeSpan MetricRetentionPeriod { get; set; } = TimeSpan.FromHours(1);
}

public enum CircuitBreakerState
{
    /// <summary>정상 상태 - 모든 요청 허용</summary>
    Closed,
    
    /// <summary>차단 상태 - 모든 요청 거부</summary>
    Open,
    
    /// <summary>반열림 상태 - 제한적 요청 허용</summary>
    HalfOpen
}

public class CircuitBreakerStateChangedEventArgs : EventArgs
{
    public CircuitBreakerState OldState { get; init; }
    public CircuitBreakerState NewState { get; init; }
    public int FailureCount { get; init; }
    public DateTime Timestamp { get; init; }
}

public class CircuitBreakerStatistics
{
    public CircuitBreakerState State { get; init; }
    public int FailureCount { get; init; }
    public DateTime LastFailureTime { get; init; }
    public DateTime LastSuccessTime { get; init; }
    public OperationMetricSnapshot[] OperationMetrics { get; init; } = [];
    public long TotalOperations { get; init; }
    public long TotalFailures { get; init; }
    public TimeSpan AverageResponseTime { get; init; }
}

public class OperationMetrics(string operationName)
{
    private readonly object _lock = new();
    private long _totalOperations = 0;
    private long _failureCount = 0;
    private double _totalResponseTimeMs = 0;
    private DateTime _lastAccess = DateTime.UtcNow;

    public string OperationName { get; } = operationName;
    public DateTime LastAccess => _lastAccess;

    public void RecordSuccess(TimeSpan responseTime)
    {
        lock (_lock)
        {
            _totalOperations++;
            _totalResponseTimeMs += responseTime.TotalMilliseconds;
            _lastAccess = DateTime.UtcNow;
        }
    }

    public void RecordFailure(Exception exception, TimeSpan responseTime)
    {
        lock (_lock)
        {
            _totalOperations++;
            _failureCount++;
            _totalResponseTimeMs += responseTime.TotalMilliseconds;
            _lastAccess = DateTime.UtcNow;
        }
    }

    public OperationMetricSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new OperationMetricSnapshot
            {
                OperationName = OperationName,
                TotalOperations = _totalOperations,
                FailureCount = _failureCount,
                SuccessCount = _totalOperations - _failureCount,
                FailureRate = _totalOperations > 0 ? (double)_failureCount / _totalOperations : 0,
                AverageResponseTime = _totalOperations > 0 
                    ? TimeSpan.FromMilliseconds(_totalResponseTimeMs / _totalOperations)
                    : TimeSpan.Zero,
                LastAccess = _lastAccess
            };
        }
    }
}

public class OperationMetricSnapshot
{
    public string OperationName { get; init; } = string.Empty;
    public long TotalOperations { get; init; }
    public long FailureCount { get; init; }
    public long SuccessCount { get; init; }
    public double FailureRate { get; init; }
    public TimeSpan AverageResponseTime { get; init; }
    public DateTime LastAccess { get; init; }
}

#endregion

#region Exceptions

public class CacheCircuitBreakerOpenException : Exception
{
    public CacheCircuitBreakerOpenException(string message) : base(message) { }
    public CacheCircuitBreakerOpenException(string message, Exception innerException) : base(message, innerException) { }
}

public class CacheCircuitBreakerFallbackException : Exception
{
    public CacheCircuitBreakerFallbackException(string message) : base(message) { }
    public CacheCircuitBreakerFallbackException(string message, Exception innerException) : base(message, innerException) { }
}

#endregion