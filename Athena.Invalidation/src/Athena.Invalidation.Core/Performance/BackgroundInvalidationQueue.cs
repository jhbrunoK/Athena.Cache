using System.Threading.Channels;
using Athena.Invalidation.Core.Abstractions;

namespace Athena.Invalidation.Core.Performance;

/// <summary>
/// 백그라운드 무효화 작업 큐
/// </summary>
public class BackgroundInvalidationQueue : IBackgroundInvalidationQueue, IDisposable
{
    private readonly Channel<InvalidationJob> _channel;
    private readonly ChannelWriter<InvalidationJob> _writer;
    private readonly ChannelReader<InvalidationJob> _reader;
    private readonly ILogger<BackgroundInvalidationQueue> _logger;
    private readonly BackgroundQueueOptions _options;
    
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private volatile bool _disposed = false;

    public BackgroundInvalidationQueue(
        ILogger<BackgroundInvalidationQueue> logger,
        IOptions<BackgroundQueueOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new BackgroundQueueOptions();

        var channelOptions = new BoundedChannelOptions(_options.MaxQueueSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };

        _channel = Channel.CreateBounded<InvalidationJob>(channelOptions);
        _writer = _channel.Writer;
        _reader = _channel.Reader;
    }

    public async ValueTask EnqueueAsync(InvalidationJob job, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BackgroundInvalidationQueue));

        try
        {
            await _writer.WriteAsync(job, cancellationToken);
            _logger.LogTrace("Enqueued invalidation job {JobId} of type {JobType}", job.Id, job.Type);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Failed to enqueue job {JobId} - operation was cancelled", job.Id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue invalidation job {JobId}", job.Id);
            throw;
        }
    }

    public async ValueTask<InvalidationJob> DequeueAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BackgroundInvalidationQueue));

        try
        {
            var job = await _reader.ReadAsync(cancellationToken);
            _logger.LogTrace("Dequeued invalidation job {JobId} of type {JobType}", job.Id, job.Type);
            return job;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Dequeue operation was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dequeue invalidation job");
            throw;
        }
    }

    public async ValueTask<IEnumerable<InvalidationJob>> DequeueBatchAsync(
        int maxBatchSize, 
        TimeSpan maxWaitTime, 
        CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BackgroundInvalidationQueue));

        var jobs = new List<InvalidationJob>();
        var deadline = DateTimeOffset.UtcNow.Add(maxWaitTime);

        try
        {
            // 첫 번째 작업은 대기해서 가져옴
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(maxWaitTime);

            var firstJob = await _reader.ReadAsync(timeoutCts.Token);
            jobs.Add(firstJob);

            // 나머지 작업들은 즉시 사용 가능한 것들만 가져옴
            while (jobs.Count < maxBatchSize && 
                   DateTimeOffset.UtcNow < deadline &&
                   _reader.TryRead(out var additionalJob))
            {
                jobs.Add(additionalJob);
            }

            _logger.LogTrace("Dequeued batch of {Count} invalidation jobs", jobs.Count);
            return jobs;
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("Batch dequeue operation was cancelled after collecting {Count} jobs", jobs.Count);
            return jobs; // 수집된 작업이 있다면 반환
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dequeue batch of invalidation jobs");
            throw;
        }
    }

    public bool TryEnqueue(InvalidationJob job)
    {
        if (_disposed) return false;

        if (_writer.TryWrite(job))
        {
            _logger.LogTrace("Successfully enqueued invalidation job {JobId} synchronously", job.Id);
            return true;
        }

        _logger.LogDebug("Failed to enqueue invalidation job {JobId} - queue is full", job.Id);
        return false;
    }

    public bool TryDequeue(out InvalidationJob job)
    {
        job = default!;
        if (_disposed) return false;

        if (_reader.TryRead(out job!))
        {
            _logger.LogTrace("Successfully dequeued invalidation job {JobId} synchronously", job.Id);
            return true;
        }

        return false;
    }

    public int Count => _reader.CanCount ? _reader.Count : -1;

    public bool IsEmpty => Count == 0;

    public async Task CompleteAsync()
    {
        if (_disposed) return;

        await _processingLock.WaitAsync();
        try
        {
            _writer.TryComplete();
            _logger.LogInformation("Background invalidation queue marked as complete");
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _writer.TryComplete();
        _processingLock.Dispose();
        _disposed = true;
        
        _logger.LogDebug("Background invalidation queue disposed");
    }
}

/// <summary>
/// 무효화 작업 정보
/// </summary>
public class InvalidationJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public InvalidationJobType Type { get; init; }
    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;
    public int Priority { get; init; } = 0; // 높을수록 우선순위
    public Dictionary<string, object> Parameters { get; init; } = new();
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
    
    // 편의 메서드들
    public static InvalidationJob CreateTableInvalidation(string tableName, int priority = 0)
    {
        return new InvalidationJob
        {
            Type = InvalidationJobType.Table,
            Priority = priority,
            Parameters = { ["tableName"] = tableName }
        };
    }
    
    public static InvalidationJob CreatePatternInvalidation(string pattern, int priority = 0)
    {
        return new InvalidationJob
        {
            Type = InvalidationJobType.Pattern,
            Priority = priority,
            Parameters = { ["pattern"] = pattern }
        };
    }
    
    public static InvalidationJob CreateKeyInvalidation(string key, int priority = 0)
    {
        return new InvalidationJob
        {
            Type = InvalidationJobType.Key,
            Priority = priority,
            Parameters = { ["key"] = key }
        };
    }
    
    public static InvalidationJob CreateBatchInvalidation(string[] tableNames, int priority = 0)
    {
        return new InvalidationJob
        {
            Type = InvalidationJobType.Batch,
            Priority = priority,
            Parameters = { ["tableNames"] = tableNames }
        };
    }
}

/// <summary>
/// 무효화 작업 타입
/// </summary>
public enum InvalidationJobType
{
    Table,
    Pattern,
    Key,
    Batch,
    Hierarchy,
    Command,
    Event,
    Custom
}

/// <summary>
/// 백그라운드 큐 인터페이스
/// </summary>
public interface IBackgroundInvalidationQueue
{
    /// <summary>작업을 큐에 추가</summary>
    ValueTask EnqueueAsync(InvalidationJob job, CancellationToken cancellationToken = default);
    
    /// <summary>큐에서 작업을 하나 가져옴</summary>
    ValueTask<InvalidationJob> DequeueAsync(CancellationToken cancellationToken = default);
    
    /// <summary>큐에서 배치로 작업들을 가져옴</summary>
    ValueTask<IEnumerable<InvalidationJob>> DequeueBatchAsync(
        int maxBatchSize, 
        TimeSpan maxWaitTime, 
        CancellationToken cancellationToken = default);
    
    /// <summary>동기적으로 작업을 큐에 추가 시도</summary>
    bool TryEnqueue(InvalidationJob job);
    
    /// <summary>동기적으로 작업을 큐에서 가져오기 시도</summary>
    bool TryDequeue(out InvalidationJob job);
    
    /// <summary>큐에 있는 작업 개수</summary>
    int Count { get; }
    
    /// <summary>큐가 비어있는지 여부</summary>
    bool IsEmpty { get; }
    
    /// <summary>큐 완료 표시</summary>
    Task CompleteAsync();
}

/// <summary>
/// 백그라운드 큐 옵션
/// </summary>
public class BackgroundQueueOptions
{
    /// <summary>최대 큐 크기</summary>
    public int MaxQueueSize { get; set; } = 10000;
    
    /// <summary>배치 처리 최대 크기</summary>
    public int MaxBatchSize { get; set; } = 100;
    
    /// <summary>배치 대기 시간</summary>
    public TimeSpan BatchWaitTime { get; set; } = TimeSpan.FromSeconds(1);
    
    /// <summary>작업 타임아웃</summary>
    public TimeSpan JobTimeout { get; set; } = TimeSpan.FromMinutes(5);
}