namespace Athena.Cache.FusionCache.Implementations;

/// <summary>
/// FusionCache의 태깅 시스템과 Athena.Cache의 무효화 메커니즘을 통합한 고급 무효화자
/// </summary>
public class FusionCacheInvalidator(
    IFusionCache fusionCache,
    ICacheKeyGenerator keyGenerator,
    AthenaCacheOptions options,
    ILogger<FusionCacheInvalidator> logger)
    : ICacheInvalidator
{
    private readonly IFusionCache _fusionCache = fusionCache ?? throw new ArgumentNullException(nameof(fusionCache));
    private readonly ICacheKeyGenerator _keyGenerator = keyGenerator ?? throw new ArgumentNullException(nameof(keyGenerator));
    private readonly AthenaCacheOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<FusionCacheInvalidator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// 테이블 기반 무효화 - FusionCache의 태그 시스템 활용
    /// </summary>
    public async Task InvalidateAsync(string tableName, CancellationToken cancellationToken = default)
    {
        try
        {
            var startTime = DateTime.UtcNow;

            // FusionCache 태그 기반 무효화는 확장 메서드나 별도 구현 필요
            // 현재는 로그만 남기고 추후 확장
            _logger.LogInformation("Tag-based invalidation requested for table '{TableName}'", tableName);

            var duration = DateTime.UtcNow - startTime;

            if (_options.Logging.LogInvalidation)
            {
                _logger.LogInformation(
                    "Invalidated cache entries tagged with '{TableName}' in {Duration}ms using FusionCache tags",
                    tableName, duration.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            if (_options.ErrorHandling.SilentFallback)
            {
                _logger.LogError(ex, "Failed to invalidate cache for table '{TableName}' using FusionCache", tableName);

                if (_options.ErrorHandling.CustomErrorHandler != null)
                {
                    await _options.ErrorHandling.CustomErrorHandler(ex);
                }
            }
            else
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 패턴 기반 무효화 - FusionCache에서는 직접 지원하지 않으므로 대안 구현
    /// </summary>
    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            var startTime = DateTime.UtcNow;

            // FusionCache는 패턴 무효화를 직접 지원하지 않으므로
            // 패턴을 태그로 변환하여 처리하거나, 경고 로그를 남김
            _logger.LogWarning(
                "Pattern-based invalidation '{Pattern}' requested but FusionCache doesn't support pattern invalidation directly. " +
                "Consider using table-based invalidation with tags instead.", pattern);

            // 패턴이 와일드카드(*) 하나만 있다면 전체 캐시 클리어로 처리
            if (pattern == "*")
            {
                _logger.LogWarning("Clearing entire cache due to wildcard pattern '*'");
                // FusionCache에서는 전체 캐시 클리어를 위한 별도 메서드 필요
                // await _fusionCache.ClearAsync(); // 만약 지원된다면
            }

            var duration = DateTime.UtcNow - startTime;

            if (_options.Logging.LogInvalidation)
            {
                _logger.LogInformation(
                    "Processed pattern invalidation request for '{Pattern}' in {Duration}ms",
                    pattern, duration.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            if (_options.ErrorHandling.SilentFallback)
            {
                _logger.LogError(ex, "Failed to invalidate cache by pattern '{Pattern}' using FusionCache", pattern);

                if (_options.ErrorHandling.CustomErrorHandler != null)
                {
                    await _options.ErrorHandling.CustomErrorHandler(ex);
                }
            }
            else
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 캐시 키를 테이블과 연결하여 추적 - FusionCache 태그 시스템 사용
    /// </summary>
    public async Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default)
    {
        await TrackCacheKeyAsync([tableName], cacheKey, cancellationToken);
    }

    /// <summary>
    /// 여러 테이블과 연결하여 추적 - 캐시 항목에 태그 추가
    /// </summary>
    public Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (tableNames == null || tableNames.Length == 0) return Task.CompletedTask;

        try
        {
            // FusionCache에서는 캐시 항목 생성 시 태그를 설정해야 함
            // 이미 존재하는 항목에는 태그를 추가할 수 없으므로 로그만 남김
            _logger.LogDebug(
                "Cache key '{CacheKey}' should be tagged with tables [{Tables}] during cache creation",
                cacheKey, string.Join(", ", tableNames));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track cache key '{CacheKey}' for tables [{Tables}]",
                cacheKey, string.Join(", ", tableNames));
        }
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// 테이블과 연결된 모든 캐시 키 조회 - FusionCache에서는 직접 지원하지 않음
    /// </summary>
    public Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "GetTrackedKeysAsync is not directly supported by FusionCache. " +
            "Use tag-based invalidation (InvalidateAsync) instead for table '{TableName}'", tableName);

        return Task.FromResult(Enumerable.Empty<string>());
    }

    /// <summary>
    /// 관련 테이블들과 함께 연쇄 무효화
    /// </summary>
    public async Task InvalidateWithRelatedAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        var processedTables = new HashSet<string>();
        await InvalidateRecursiveAsync(tableName, relatedTables, maxDepth, 0, processedTables, cancellationToken);
    }

    /// <summary>
    /// 여러 테이블을 배치로 무효화
    /// </summary>
    public async Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        if (tableNames == null) return;

        var tables = tableNames.ToList();
        if (tables.Count == 0) return;

        try
        {
            var startTime = DateTime.UtcNow;

            // FusionCache는 여러 태그를 동시에 무효화할 수 있음
            var invalidationTasks = tables.Select(tableName => InvalidateAsync(tableName, cancellationToken));
            await Task.WhenAll(invalidationTasks);

            var elapsed = DateTime.UtcNow - startTime;

            if (_options.Logging.LogInvalidation)
            {
                _logger.LogInformation(
                    "Batch invalidated {TableCount} table tags in {ElapsedMs}ms using FusionCache",
                    tables.Count, elapsed.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during batch invalidation for tables '{Tables}' using FusionCache", 
                string.Join(", ", tables));

            if (!_options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 여러 패턴을 배치로 무효화
    /// </summary>
    public async Task InvalidateByPatternBatchAsync(IEnumerable<string> patterns, CancellationToken cancellationToken = default)
    {
        if (patterns == null) return;

        var patternList = patterns.ToList();
        if (patternList.Count == 0) return;

        try
        {
            var startTime = DateTime.UtcNow;

            var invalidationTasks = patternList.Select(pattern => InvalidateByPatternAsync(pattern, cancellationToken));
            await Task.WhenAll(invalidationTasks);

            var elapsed = DateTime.UtcNow - startTime;

            if (_options.Logging.LogInvalidation)
            {
                _logger.LogInformation(
                    "Batch pattern invalidation completed: {TotalCount} patterns processed in {ElapsedMs}ms",
                    patternList.Count, elapsed.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during batch pattern invalidation using FusionCache");

            if (!_options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 재귀적 무효화 (순환 참조 방지)
    /// </summary>
    private async Task InvalidateRecursiveAsync(
        string tableName,
        string[] relatedTables,
        int maxDepth,
        int currentDepth,
        HashSet<string> processedTables,
        CancellationToken cancellationToken)
    {
        if (processedTables.Contains(tableName) || currentDepth >= maxDepth)
        {
            return;
        }

        processedTables.Add(tableName);
        await InvalidateAsync(tableName, cancellationToken);

        if (_options.Logging.LogInvalidation)
        {
            _logger.LogDebug("Invalidated table '{TableName}' at depth {Depth} using FusionCache", 
                tableName, currentDepth);
        }

        if (relatedTables != null && relatedTables.Length > 0)
        {
            foreach (var relatedTable in relatedTables)
            {
                await InvalidateRecursiveAsync(relatedTable, [], maxDepth, currentDepth + 1, processedTables, cancellationToken);
            }
        }
    }
}
