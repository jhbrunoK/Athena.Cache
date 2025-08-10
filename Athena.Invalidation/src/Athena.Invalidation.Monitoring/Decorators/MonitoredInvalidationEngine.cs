using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Monitoring.Abstractions;
using Athena.Invalidation.Monitoring.Telemetry;

namespace Athena.Invalidation.Monitoring.Decorators;

/// <summary>
/// 모니터링 기능이 추가된 무효화 엔진 데코레이터
/// </summary>
public class MonitoredInvalidationEngine(
    IInvalidationEngine innerEngine,
    IInvalidationMetricsCollector metricsCollector,
    ILogger<MonitoredInvalidationEngine> logger)
    : IInvalidationEngine, IAsyncDisposable
{
    private readonly IInvalidationEngine _innerEngine = innerEngine ?? throw new ArgumentNullException(nameof(innerEngine));
    private readonly IInvalidationMetricsCollector _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
    private readonly ILogger<MonitoredInvalidationEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    
    private volatile bool _disposed = false;

    public async Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MonitoredInvalidationEngine));
        
        using var activity = InvalidationActivitySource.StartInvalidateByTable(tableName);
        var startTime = DateTimeOffset.UtcNow;
        var success = false;
        Exception? exception = null;

        try
        {
            await _innerEngine.InvalidateByTableAsync(tableName, cancellationToken);
            success = true;
            activity?.SetSuccess(true);
        }
        catch (Exception ex)
        {
            exception = ex;
            activity?.SetSuccess(false, ex);
            throw;
        }
        finally
        {
            var duration = DateTimeOffset.UtcNow - startTime;
            _metricsCollector.RecordInvalidationEvent(InvalidationType.Table, tableName, duration, success);
            
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Table invalidation {TableName} completed in {Duration}ms (Success: {Success})",
                    tableName, duration.TotalMilliseconds, success);
            }
        }
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MonitoredInvalidationEngine));
        
        using var activity = InvalidationActivitySource.StartInvalidateByPattern(pattern);
        var startTime = DateTimeOffset.UtcNow;
        var success = false;

        try
        {
            await _innerEngine.InvalidateByPatternAsync(pattern, cancellationToken);
            success = true;
            activity?.SetSuccess(true);
        }
        catch (Exception ex)
        {
            activity?.SetSuccess(false, ex);
            throw;
        }
        finally
        {
            var duration = DateTimeOffset.UtcNow - startTime;
            _metricsCollector.RecordInvalidationEvent(InvalidationType.Pattern, pattern, duration, success);
            
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Pattern invalidation {Pattern} completed in {Duration}ms (Success: {Success})",
                    pattern, duration.TotalMilliseconds, success);
            }
        }
    }

    public async Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MonitoredInvalidationEngine));
        
        using var activity = InvalidationActivitySource.StartInvalidateByKey(key);
        var startTime = DateTimeOffset.UtcNow;
        var success = false;

        try
        {
            await _innerEngine.InvalidateByKeyAsync(key, cancellationToken);
            success = true;
            activity?.SetSuccess(true);
        }
        catch (Exception ex)
        {
            activity?.SetSuccess(false, ex);
            throw;
        }
        finally
        {
            var duration = DateTimeOffset.UtcNow - startTime;
            _metricsCollector.RecordInvalidationEvent(InvalidationType.Key, key, duration, success);
            
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Key invalidation {Key} completed in {Duration}ms (Success: {Success})",
                    key, duration.TotalMilliseconds, success);
            }
        }
    }

    public async Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MonitoredInvalidationEngine));
        
        var tableArray = tableNames.ToArray();
        using var activity = InvalidationActivitySource.StartInvalidateBatch(tableArray.Length);
        var startTime = DateTimeOffset.UtcNow;
        var success = false;

        try
        {
            await _innerEngine.InvalidateBatchAsync(tableArray, cancellationToken);
            success = true;
            activity?.SetSuccess(true);
        }
        catch (Exception ex)
        {
            activity?.SetSuccess(false, ex);
            throw;
        }
        finally
        {
            var duration = DateTimeOffset.UtcNow - startTime;
            _metricsCollector.RecordBatchInvalidationEvent(tableArray.Length, duration, success);
            
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Batch invalidation for {Count} tables completed in {Duration}ms (Success: {Success})",
                    tableArray.Length, duration.TotalMilliseconds, success);
            }
        }
    }

    public async Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MonitoredInvalidationEngine));
        
        using var activity = InvalidationActivitySource.StartInvalidateHierarchy(tableName, maxDepth);
        var startTime = DateTimeOffset.UtcNow;
        var success = false;

        try
        {
            await _innerEngine.InvalidateHierarchyAsync(tableName, relatedTables, maxDepth, cancellationToken);
            success = true;
            activity?.SetSuccess(true);
        }
        catch (Exception ex)
        {
            activity?.SetSuccess(false, ex);
            throw;
        }
        finally
        {
            var duration = DateTimeOffset.UtcNow - startTime;
            _metricsCollector.RecordInvalidationEvent(InvalidationType.Hierarchy, tableName, duration, success);
            
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Hierarchical invalidation {TableName} (depth: {MaxDepth}) completed in {Duration}ms (Success: {Success})",
                    tableName, maxDepth, duration.TotalMilliseconds, success);
            }
        }
    }

    public async Task InvalidateOnCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) 
        where TCommand : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MonitoredInvalidationEngine));
        
        var commandType = typeof(TCommand).Name;
        using var activity = InvalidationActivitySource.StartInvalidateCommand(commandType);
        var startTime = DateTimeOffset.UtcNow;
        var success = false;

        try
        {
            await _innerEngine.InvalidateOnCommandAsync(command, cancellationToken);
            success = true;
            activity?.SetSuccess(true);
        }
        catch (Exception ex)
        {
            activity?.SetSuccess(false, ex);
            throw;
        }
        finally
        {
            var duration = DateTimeOffset.UtcNow - startTime;
            _metricsCollector.RecordInvalidationEvent(InvalidationType.Custom, commandType, duration, success);
            
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Command invalidation {CommandType} completed in {Duration}ms (Success: {Success})",
                    commandType, duration.TotalMilliseconds, success);
            }
        }
    }

    public async Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) 
        where TEvent : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MonitoredInvalidationEngine));
        
        var eventType = typeof(TEvent).Name;
        string? aggregateId = null;
        
        // AggregateId 추출 시도
        var aggregateIdProperty = typeof(TEvent).GetProperty("AggregateId");
        if (aggregateIdProperty != null)
        {
            aggregateId = aggregateIdProperty.GetValue(domainEvent)?.ToString();
        }
        
        using var activity = InvalidationActivitySource.StartInvalidateEvent(eventType, aggregateId);
        var startTime = DateTimeOffset.UtcNow;
        var success = false;

        try
        {
            await _innerEngine.InvalidateOnEventAsync(domainEvent, cancellationToken);
            success = true;
            activity?.SetSuccess(true);
        }
        catch (Exception ex)
        {
            activity?.SetSuccess(false, ex);
            throw;
        }
        finally
        {
            var duration = DateTimeOffset.UtcNow - startTime;
            _metricsCollector.RecordInvalidationEvent(InvalidationType.Custom, eventType, duration, success);
            
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Event invalidation {EventType} (AggregateId: {AggregateId}) completed in {Duration}ms (Success: {Success})",
                    eventType, aggregateId ?? "null", duration.TotalMilliseconds, success);
            }
        }
    }

    // 나머지 메서드들은 단순히 내부 엔진에 위임 (모니터링 없음)
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
        if (_disposed) throw new ObjectDisposedException(nameof(MonitoredInvalidationEngine));
        
        var startTime = DateTimeOffset.UtcNow;
        var success = false;

        try
        {
            await _innerEngine.ClearAllAsync(cancellationToken);
            success = true;
        }
        finally
        {
            var duration = DateTimeOffset.UtcNow - startTime;
            _metricsCollector.RecordInvalidationEvent(InvalidationType.Custom, "clear_all", duration, success);
            
            _logger.LogWarning("ClearAll operation completed in {Duration}ms (Success: {Success})",
                duration.TotalMilliseconds, success);
        }
    }

    public async Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await _innerEngine.GetStatusAsync(cancellationToken);
        
        // 모니터링 관련 메트릭 추가
        var metrics = await _metricsCollector.GetMetricsAsync(cancellationToken);
        status.Metrics["Monitoring_TotalInvalidations"] = metrics.TotalInvalidations;
        status.Metrics["Monitoring_SuccessRate"] = metrics.InvalidationSuccessRate;
        status.Metrics["Monitoring_AverageTime"] = metrics.AverageInvalidationTime.TotalMilliseconds;
        status.Metrics["Monitoring_CacheHitRatio"] = metrics.CacheHitRatio;
        
        return status;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

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
            _logger.LogError(ex, "Error disposing inner invalidation engine");
        }
        finally
        {
            _disposed = true;
        }
    }
}
