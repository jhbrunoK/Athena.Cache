namespace Athena.Invalidation.Core.Performance;

/// <summary>
/// 백그라운드에서 무효화 작업을 처리하는 서비스
/// </summary>
public class BackgroundInvalidationProcessor : BackgroundService
{
    private readonly IBackgroundInvalidationQueue _queue;
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly ILogger<BackgroundInvalidationProcessor> _logger;
    private readonly BackgroundProcessorOptions _options;
    
    private readonly SemaphoreSlim _processingLock;
    private readonly ConcurrentDictionary<string, ProcessingStatistics> _statistics = new();

    public BackgroundInvalidationProcessor(
        IBackgroundInvalidationQueue queue,
        IInvalidationEngine invalidationEngine,
        ILogger<BackgroundInvalidationProcessor> logger,
        IOptions<BackgroundProcessorOptions> options)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _invalidationEngine = invalidationEngine ?? throw new ArgumentNullException(nameof(invalidationEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new BackgroundProcessorOptions();
        
        _processingLock = new SemaphoreSlim(_options.MaxConcurrentJobs, _options.MaxConcurrentJobs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background invalidation processor started with {MaxConcurrency} concurrent jobs", 
            _options.MaxConcurrentJobs);

        var processingTasks = new List<Task>();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 배치로 작업들을 가져옴
                    var jobs = await _queue.DequeueBatchAsync(
                        _options.MaxBatchSize, 
                        _options.BatchWaitTime, 
                        stoppingToken);

                    var jobList = jobs.ToList();
                    if (jobList.Count == 0) continue;

                    _logger.LogTrace("Processing batch of {JobCount} invalidation jobs", jobList.Count);

                    if (_options.EnableBatchOptimization)
                    {
                        // 배치 최적화 처리
                        await ProcessBatchOptimizedAsync(jobList, stoppingToken);
                    }
                    else
                    {
                        // 개별 작업 병렬 처리
                        var tasks = jobList.Select(job => ProcessJobAsync(job, stoppingToken));
                        processingTasks.AddRange(tasks);

                        // 완료된 태스크들 정리
                        processingTasks.RemoveAll(t => t.IsCompleted);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Background invalidation processor is stopping");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in background invalidation processor main loop");
                    
                    // 오류 발생 시 잠시 대기
                    await Task.Delay(_options.ErrorRetryDelay, stoppingToken);
                }
            }

            // 남은 작업들 완료 대기
            if (processingTasks.Count > 0)
            {
                _logger.LogInformation("Waiting for {TaskCount} remaining tasks to complete", processingTasks.Count);
                await Task.WhenAll(processingTasks);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Critical error in background invalidation processor");
        }

        _logger.LogInformation("Background invalidation processor stopped");
    }

    private async Task ProcessBatchOptimizedAsync(List<InvalidationJob> jobs, CancellationToken cancellationToken)
    {
        // 작업 타입별로 그룹화
        var groupedJobs = jobs.GroupBy(j => j.Type).ToList();
        
        foreach (var group in groupedJobs)
        {
            try
            {
                await ProcessJobGroupAsync(group.Key, group.ToList(), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process job group {JobType} with {JobCount} jobs", 
                    group.Key, group.Count());
            }
        }
    }

    private async Task ProcessJobGroupAsync(InvalidationJobType jobType, List<InvalidationJob> jobs, CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;
        
        try
        {
            switch (jobType)
            {
                case InvalidationJobType.Table:
                    await ProcessTableJobsAsync(jobs, cancellationToken);
                    break;
                    
                case InvalidationJobType.Pattern:
                    await ProcessPatternJobsAsync(jobs, cancellationToken);
                    break;
                    
                case InvalidationJobType.Key:
                    await ProcessKeyJobsAsync(jobs, cancellationToken);
                    break;
                    
                case InvalidationJobType.Batch:
                    await ProcessBatchJobsAsync(jobs, cancellationToken);
                    break;
                    
                default:
                    // 다른 타입들은 개별 처리
                    var tasks = jobs.Select(job => ProcessJobAsync(job, cancellationToken));
                    await Task.WhenAll(tasks);
                    break;
            }
            
            var duration = DateTimeOffset.UtcNow - startTime;
            UpdateStatistics(jobType.ToString(), jobs.Count, duration, true);
            
            _logger.LogDebug("Processed {JobCount} {JobType} jobs in {Duration}ms", 
                jobs.Count, jobType, duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            var duration = DateTimeOffset.UtcNow - startTime;
            UpdateStatistics(jobType.ToString(), jobs.Count, duration, false);
            
            _logger.LogError(ex, "Failed to process {JobCount} {JobType} jobs", jobs.Count, jobType);
            throw;
        }
    }

    private async Task ProcessTableJobsAsync(List<InvalidationJob> jobs, CancellationToken cancellationToken)
    {
        // 테이블 이름들을 수집하여 배치 처리
        var tableNames = jobs
            .Select(j => j.Parameters.GetValueOrDefault("tableName")?.ToString())
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct()
            .ToArray();

        if (tableNames.Length > 0)
        {
            await _invalidationEngine.InvalidateBatchAsync(tableNames!, cancellationToken);
        }
    }

    private async Task ProcessPatternJobsAsync(List<InvalidationJob> jobs, CancellationToken cancellationToken)
    {
        // 패턴은 개별적으로 처리해야 함
        foreach (var job in jobs)
        {
            var pattern = job.Parameters.GetValueOrDefault("pattern")?.ToString();
            if (!string.IsNullOrEmpty(pattern))
            {
                await _invalidationEngine.InvalidateByPatternAsync(pattern, cancellationToken);
            }
        }
    }

    private async Task ProcessKeyJobsAsync(List<InvalidationJob> jobs, CancellationToken cancellationToken)
    {
        // 키는 개별적으로 처리
        var tasks = jobs.Select(async job =>
        {
            var key = job.Parameters.GetValueOrDefault("key")?.ToString();
            if (!string.IsNullOrEmpty(key))
            {
                await _invalidationEngine.InvalidateByKeyAsync(key, cancellationToken);
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task ProcessBatchJobsAsync(List<InvalidationJob> jobs, CancellationToken cancellationToken)
    {
        // 모든 배치 작업의 테이블 이름들을 합쳐서 하나의 대용량 배치로 처리
        var allTableNames = new HashSet<string>();
        
        foreach (var job in jobs)
        {
            if (job.Parameters.TryGetValue("tableNames", out var tableNamesObj) && 
                tableNamesObj is string[] tableNames)
            {
                foreach (var tableName in tableNames)
                {
                    allTableNames.Add(tableName);
                }
            }
        }

        if (allTableNames.Count > 0)
        {
            await _invalidationEngine.InvalidateBatchAsync(allTableNames.ToArray(), cancellationToken);
        }
    }

    private async Task ProcessJobAsync(InvalidationJob job, CancellationToken cancellationToken)
    {
        await _processingLock.WaitAsync(cancellationToken);
        
        try
        {
            var startTime = DateTimeOffset.UtcNow;
            var success = false;

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.JobTimeout);

                await ExecuteJobAsync(job, timeoutCts.Token);
                success = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Job {JobId} was cancelled", job.Id);
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Job {JobId} timed out after {Timeout}", job.Id, _options.JobTimeout);
                throw new TimeoutException($"Job {job.Id} timed out");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process job {JobId} of type {JobType}", job.Id, job.Type);
                throw;
            }
            finally
            {
                var duration = DateTimeOffset.UtcNow - startTime;
                UpdateStatistics(job.Type.ToString(), 1, duration, success);
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private async Task ExecuteJobAsync(InvalidationJob job, CancellationToken cancellationToken)
    {
        switch (job.Type)
        {
            case InvalidationJobType.Table:
                var tableName = job.Parameters["tableName"].ToString()!;
                await _invalidationEngine.InvalidateByTableAsync(tableName, cancellationToken);
                break;
                
            case InvalidationJobType.Pattern:
                var pattern = job.Parameters["pattern"].ToString()!;
                await _invalidationEngine.InvalidateByPatternAsync(pattern, cancellationToken);
                break;
                
            case InvalidationJobType.Key:
                var key = job.Parameters["key"].ToString()!;
                await _invalidationEngine.InvalidateByKeyAsync(key, cancellationToken);
                break;
                
            case InvalidationJobType.Batch:
                var tableNames = (string[])job.Parameters["tableNames"];
                await _invalidationEngine.InvalidateBatchAsync(tableNames, cancellationToken);
                break;
                
            case InvalidationJobType.Hierarchy:
                var rootTable = job.Parameters["rootTable"].ToString()!;
                var relatedTables = (string[])job.Parameters["relatedTables"];
                var maxDepth = (int)job.Parameters.GetValueOrDefault("maxDepth", 3);
                await _invalidationEngine.InvalidateHierarchyAsync(rootTable, relatedTables, maxDepth, cancellationToken);
                break;
                
            default:
                _logger.LogWarning("Unknown job type: {JobType}", job.Type);
                break;
        }
    }

    private void UpdateStatistics(string jobType, int count, TimeSpan duration, bool success)
    {
        _statistics.AddOrUpdate(jobType,
            new ProcessingStatistics { TotalJobs = count, SuccessfulJobs = success ? count : 0, TotalDuration = duration },
            (key, existing) => new ProcessingStatistics
            {
                TotalJobs = existing.TotalJobs + count,
                SuccessfulJobs = existing.SuccessfulJobs + (success ? count : 0),
                TotalDuration = existing.TotalDuration + duration
            });
    }

    public ProcessingStatistics GetStatistics(string jobType)
    {
        return _statistics.GetValueOrDefault(jobType, new ProcessingStatistics());
    }

    public Dictionary<string, ProcessingStatistics> GetAllStatistics()
    {
        return new Dictionary<string, ProcessingStatistics>(_statistics);
    }

    public override void Dispose()
    {
        _processingLock?.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// 처리 통계 정보
/// </summary>
public class ProcessingStatistics
{
    public long TotalJobs { get; set; }
    public long SuccessfulJobs { get; set; }
    public long FailedJobs => TotalJobs - SuccessfulJobs;
    public double SuccessRate => TotalJobs > 0 ? (double)SuccessfulJobs / TotalJobs : 1.0;
    public TimeSpan TotalDuration { get; set; }
    public TimeSpan AverageDuration => TotalJobs > 0 ? 
        TimeSpan.FromMilliseconds(TotalDuration.TotalMilliseconds / TotalJobs) : TimeSpan.Zero;
}

/// <summary>
/// 백그라운드 프로세서 옵션
/// </summary>
public class BackgroundProcessorOptions
{
    /// <summary>최대 동시 작업 수</summary>
    public int MaxConcurrentJobs { get; set; } = Environment.ProcessorCount;
    
    /// <summary>배치 처리 최대 크기</summary>
    public int MaxBatchSize { get; set; } = 100;
    
    /// <summary>배치 대기 시간</summary>
    public TimeSpan BatchWaitTime { get; set; } = TimeSpan.FromSeconds(1);
    
    /// <summary>작업 타임아웃</summary>
    public TimeSpan JobTimeout { get; set; } = TimeSpan.FromMinutes(5);
    
    /// <summary>배치 최적화 활성화</summary>
    public bool EnableBatchOptimization { get; set; } = true;
    
    /// <summary>오류 발생 시 재시도 지연</summary>
    public TimeSpan ErrorRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}
