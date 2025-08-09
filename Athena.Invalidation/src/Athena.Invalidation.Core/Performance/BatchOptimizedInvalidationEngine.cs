using Athena.Invalidation.Core.Abstractions;
using System.Collections.Concurrent;

namespace Athena.Invalidation.Core.Performance;

/// <summary>
/// 배치 최적화된 무효화 엔진 데코레이터
/// </summary>
public class BatchOptimizedInvalidationEngine : IInvalidationEngine, IAsyncDisposable
{
    private readonly IInvalidationEngine _innerEngine;
    private readonly IBackgroundInvalidationQueue _queue;
    private readonly ILogger<BatchOptimizedInvalidationEngine> _logger;
    private readonly BatchOptimizationOptions _options;
    
    // 즉시 처리용 배치 버퍼
    private readonly ConcurrentQueue<InvalidationJob> _immediateBuffer = new();
    private readonly Timer _batchTimer;
    private readonly SemaphoreSlim _batchLock = new(1, 1);
    
    private volatile bool _disposed = false;

    public BatchOptimizedInvalidationEngine(
        IInvalidationEngine innerEngine,
        IBackgroundInvalidationQueue queue,
        ILogger<BatchOptimizedInvalidationEngine> logger,
        IOptions<BatchOptimizationOptions> options)
    {
        _innerEngine = innerEngine ?? throw new ArgumentNullException(nameof(innerEngine));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new BatchOptimizationOptions();
        
        // 배치 처리 타이머 시작
        _batchTimer = new Timer(ProcessImmediateBatch, null, _options.ImmediateBatchInterval, _options.ImmediateBatchInterval);
    }

    public async Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BatchOptimizedInvalidationEngine));

        var job = InvalidationJob.CreateTableInvalidation(tableName, GetPriority());
        
        if (ShouldProcessImmediately(job))
        {
            await _innerEngine.InvalidateByTableAsync(tableName, cancellationToken);
        }
        else if (_options.EnableImmediateBatching)
        {
            _immediateBuffer.Enqueue(job);
        }
        else
        {
            await _queue.EnqueueAsync(job, cancellationToken);
        }
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BatchOptimizedInvalidationEngine));

        var job = InvalidationJob.CreatePatternInvalidation(pattern, GetPriority());
        
        if (ShouldProcessImmediately(job))
        {
            await _innerEngine.InvalidateByPatternAsync(pattern, cancellationToken);
        }
        else
        {
            // 패턴 무효화는 배치 최적화가 어려우므로 항상 큐에 추가
            await _queue.EnqueueAsync(job, cancellationToken);
        }
    }

    public async Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BatchOptimizedInvalidationEngine));

        var job = InvalidationJob.CreateKeyInvalidation(key, GetPriority());
        
        if (ShouldProcessImmediately(job))
        {
            await _innerEngine.InvalidateByKeyAsync(key, cancellationToken);
        }
        else
        {
            await _queue.EnqueueAsync(job, cancellationToken);
        }
    }

    public async Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BatchOptimizedInvalidationEngine));

        var tableArray = tableNames.ToArray();
        
        // 배치 무효화는 이미 최적화된 형태이므로 바로 처리하거나 큐에 추가
        if (tableArray.Length <= _options.ImmediateProcessingThreshold)
        {
            await _innerEngine.InvalidateBatchAsync(tableArray, cancellationToken);
        }
        else
        {
            var job = InvalidationJob.CreateBatchInvalidation(tableArray, GetHighPriority());
            await _queue.EnqueueAsync(job, cancellationToken);
        }
    }

    public async Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BatchOptimizedInvalidationEngine));

        // 계층적 무효화는 복잡하므로 큐에서 처리
        var job = new InvalidationJob
        {
            Type = InvalidationJobType.Hierarchy,
            Priority = GetHighPriority(),
            Parameters = 
            {
                ["rootTable"] = tableName,
                ["relatedTables"] = relatedTables,
                ["maxDepth"] = maxDepth
            }
        };
        
        await _queue.EnqueueAsync(job, cancellationToken);
    }

    public async Task InvalidateOnCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) 
        where TCommand : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BatchOptimizedInvalidationEngine));

        // CQRS 명령은 즉시 처리
        await _innerEngine.InvalidateOnCommandAsync(command, cancellationToken);
    }

    public async Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) 
        where TEvent : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BatchOptimizedInvalidationEngine));

        // CQRS 이벤트는 즉시 처리
        await _innerEngine.InvalidateOnEventAsync(domainEvent, cancellationToken);
    }

    // 나머지 메서드들은 내부 엔진에 위임
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
        if (_disposed) throw new ObjectDisposedException(nameof(BatchOptimizedInvalidationEngine));

        // ClearAll은 즉시 처리
        await _innerEngine.ClearAllAsync(cancellationToken);
    }

    public Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return _innerEngine.GetStatusAsync(cancellationToken);
    }

    private bool ShouldProcessImmediately(InvalidationJob job)
    {
        // 높은 우선순위 작업은 즉시 처리
        if (job.Priority >= _options.HighPriorityThreshold)
            return true;
            
        // 큐가 거의 차있으면 즉시 처리하여 백프레셔 방지
        if (_queue.Count >= _options.ImmediateProcessingThreshold)
            return true;
            
        return false;
    }

    private int GetPriority()
    {
        // 큐 상태에 따른 동적 우선순위
        var queueLoad = _queue.Count / (double)_options.ImmediateProcessingThreshold;
        
        return queueLoad switch
        {
            < 0.3 => 1, // 낮은 부하
            < 0.7 => 3, // 중간 부하  
            _ => 5      // 높은 부하
        };
    }
    
    private int GetHighPriority() => 8;

    private async void ProcessImmediateBatch(object? state)
    {
        if (_disposed || _immediateBuffer.IsEmpty) return;

        await _batchLock.WaitAsync();
        try
        {
            var jobs = new List<InvalidationJob>();
            
            // 버퍼에서 작업들을 가져옴
            while (jobs.Count < _options.ImmediateBatchSize && _immediateBuffer.TryDequeue(out var job))
            {
                jobs.Add(job);
            }

            if (jobs.Count == 0) return;

            // 테이블 무효화 작업들을 그룹화하여 배치 처리
            var tableJobs = jobs.Where(j => j.Type == InvalidationJobType.Table).ToList();
            if (tableJobs.Count > 0)
            {
                var tableNames = tableJobs
                    .Select(j => j.Parameters["tableName"].ToString()!)
                    .Distinct()
                    .ToArray();

                try
                {
                    await _innerEngine.InvalidateBatchAsync(tableNames, CancellationToken.None);
                    _logger.LogTrace("Processed immediate batch of {Count} table invalidations", tableNames.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process immediate batch of table invalidations");
                    
                    // 실패한 작업들을 큐에 다시 추가
                    foreach (var job in tableJobs)
                    {
                        if (_queue.TryEnqueue(job))
                        {
                            _logger.LogTrace("Re-queued failed job {JobId}", job.Id);
                        }
                    }
                }
            }

            // 다른 타입의 작업들은 큐에 추가
            var otherJobs = jobs.Where(j => j.Type != InvalidationJobType.Table).ToList();
            foreach (var job in otherJobs)
            {
                if (!_queue.TryEnqueue(job))
                {
                    _logger.LogWarning("Failed to re-queue job {JobId} of type {JobType}", job.Id, job.Type);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing immediate batch");
        }
        finally
        {
            _batchLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            _batchTimer?.Dispose();
            _batchLock?.Dispose();

            // 버퍼에 남은 작업들을 큐에 추가
            while (_immediateBuffer.TryDequeue(out var job))
            {
                _queue.TryEnqueue(job);
            }

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
            _logger.LogError(ex, "Error during BatchOptimizedInvalidationEngine disposal");
        }
    }
}

/// <summary>
/// 배치 최적화 옵션
/// </summary>
public class BatchOptimizationOptions
{
    /// <summary>즉시 처리 임계값</summary>
    public int ImmediateProcessingThreshold { get; set; } = 100;
    
    /// <summary>높은 우선순위 임계값</summary>
    public int HighPriorityThreshold { get; set; } = 7;
    
    /// <summary>즉시 배치 처리 활성화</summary>
    public bool EnableImmediateBatching { get; set; } = true;
    
    /// <summary>즉시 배치 크기</summary>
    public int ImmediateBatchSize { get; set; } = 20;
    
    /// <summary>즉시 배치 간격</summary>
    public TimeSpan ImmediateBatchInterval { get; set; } = TimeSpan.FromMilliseconds(500);
}