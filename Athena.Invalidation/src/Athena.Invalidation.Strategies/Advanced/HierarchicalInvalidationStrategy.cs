namespace Athena.Invalidation.Strategies.Advanced;

/// <summary>
/// 계층적 무효화 전략 - 연관된 테이블들과 함께 계층적으로 무효화
/// 의존성 그래프를 따라 연쇄적으로 무효화 처리
/// </summary>
public class HierarchicalInvalidationStrategy(ILogger<HierarchicalInvalidationStrategy> logger) : IInvalidationStrategy
{
    private readonly ILogger<HierarchicalInvalidationStrategy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private IServiceProvider _serviceProvider = null!;

    public string StrategyName => "Hierarchical";
    public int Priority => 200; // 고급 전략이므로 높은 우선순위

    public bool CanHandle(IInvalidationContext context)
    {
        return context.Type == InvalidationType.Hierarchy;
    }

    public async Task<InvalidationResult> ExecuteAsync(IInvalidationContext context, CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var totalInvalidated = 0;

        try
        {
            var tableName = context.Target;
            var relatedTables = context.GetMetadata<string[]>("RelatedTables", []) ?? [];
            var maxDepth = context.GetMetadata<int>("MaxDepth", 3);

            var processedTables = new HashSet<string>();
            var invalidationTasks = new List<Task<int>>();

            // 메인 테이블 무효화
            totalInvalidated += await InvalidateTableHierarchyAsync(
                tableName, relatedTables, maxDepth, 0, processedTables, context, cancellationToken);

            var executionTime = DateTimeOffset.UtcNow - startTime;
            
            _logger.LogInformation(
                "Hierarchical invalidation completed for '{Table}' with {Count} related tables, invalidated {Total} keys in {Time}ms",
                tableName, processedTables.Count, totalInvalidated, executionTime.TotalMilliseconds);

            return InvalidationResult.Successful(
                totalInvalidated,
                executionTime,
                new Dictionary<string, object>
                {
                    ["Strategy"] = StrategyName,
                    ["RootTable"] = tableName,
                    ["ProcessedTables"] = processedTables.Count,
                    ["MaxDepthReached"] = processedTables.Count >= maxDepth
                });
        }
        catch (Exception ex)
        {
            var executionTime = DateTimeOffset.UtcNow - startTime;
            _logger.LogError(ex, "HierarchicalInvalidationStrategy failed for context {ContextId}", context.ContextId);
            
            return InvalidationResult.Failed(
                $"Hierarchical invalidation failed: {ex.Message}",
                ex,
                new Dictionary<string, object>
                {
                    ["Strategy"] = StrategyName,
                    ["Context"] = context.ContextId,
                    ["ExecutionTime"] = executionTime.TotalMilliseconds
                });
        }
    }

    private async Task<int> InvalidateTableHierarchyAsync(
        string tableName,
        string[] relatedTables,
        int maxDepth,
        int currentDepth,
        HashSet<string> processedTables,
        IInvalidationContext baseContext,
        CancellationToken cancellationToken)
    {
        // 순환 참조 방지 및 최대 깊이 체크
        if (processedTables.Contains(tableName) || currentDepth >= maxDepth)
        {
            return 0;
        }

        processedTables.Add(tableName);
        var totalInvalidated = 0;

        try
        {
            // 현재 테이블 무효화
            totalInvalidated += await InvalidateTableDirectAsync(tableName, baseContext, cancellationToken);

            _logger.LogDebug(
                "Invalidated table '{Table}' at depth {Depth} (total: {Total})",
                tableName, currentDepth, totalInvalidated);

            // 연관 테이블들 재귀적 무효화 (병렬 처리)
            if (relatedTables != null && relatedTables.Length > 0 && currentDepth < maxDepth - 1)
            {
                var tasks = relatedTables
                    .Where(relatedTable => !processedTables.Contains(relatedTable))
                    .Select(async relatedTable =>
                    {
                        try
                        {
                            // 각 관련 테이블을 재귀적으로 처리 (더 이상 연관 테이블은 없음)
                            return await InvalidateTableHierarchyAsync(
                                relatedTable, [], maxDepth, currentDepth + 1, 
                                processedTables, baseContext, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, 
                                "Failed to invalidate related table '{RelatedTable}' from '{Table}'",
                                relatedTable, tableName);
                            return 0;
                        }
                    });

                var results = await Task.WhenAll(tasks);
                totalInvalidated += results.Sum();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to invalidate table '{Table}' at depth {Depth}",
                tableName, currentDepth);
        }

        return totalInvalidated;
    }

    private async Task<int> InvalidateTableDirectAsync(
        string tableName, 
        IInvalidationContext baseContext, 
        CancellationToken cancellationToken)
    {
        var totalInvalidated = 0;
        var trackingKey = GenerateTrackingKey(tableName);

        // 모든 캐시 프로바이더에서 해당 테이블의 추적된 키들을 제거
        foreach (var provider in baseContext.CacheProviders)
        {
            try
            {
                var trackedKeys = await provider.GetAsync<HashSet<string>>(trackingKey, cancellationToken);
                
                if (trackedKeys != null && trackedKeys.Count > 0)
                {
                    var removedCount = await provider.RemoveManyAsync(trackedKeys, cancellationToken);
                    totalInvalidated += removedCount;
                    
                    // 추적 키도 제거
                    await provider.RemoveAsync(trackingKey, cancellationToken);
                    
                    _logger.LogDebug(
                        "Invalidated {Count} keys for table '{Table}' in provider '{Provider}'",
                        removedCount, tableName, provider.ProviderName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "Failed to invalidate table '{Table}' in provider '{Provider}'",
                    tableName, provider.ProviderName);
            }
        }

        return totalInvalidated;
    }

    private string GenerateTrackingKey(string tableName)
    {
        return $"invalidation:tracking:{tableName}";
    }

    public Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger.LogInformation("HierarchicalInvalidationStrategy initialized");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _logger.LogInformation("HierarchicalInvalidationStrategy disposed");
        return Task.CompletedTask;
    }
}
