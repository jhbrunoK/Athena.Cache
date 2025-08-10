namespace Athena.Invalidation.Strategies.Basic;

/// <summary>
/// 기본 무효화 전략 - Table, Pattern, Key 기반 무효화 지원
/// 가장 범용적이고 안정적인 무효화 처리
/// </summary>
public class BasicInvalidationStrategy(ILogger<BasicInvalidationStrategy> logger) : IInvalidationStrategy
{
    private readonly ILogger<BasicInvalidationStrategy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private IServiceProvider _serviceProvider = null!;

    public string StrategyName => "Basic";
    public int Priority => 100; // 기본 전략이므로 중간 우선순위

    public bool CanHandle(IInvalidationContext context)
    {
        // 기본 전략은 모든 타입을 처리할 수 있음
        return context.Type is InvalidationType.Table 
                         or InvalidationType.Pattern 
                         or InvalidationType.Key 
                         or InvalidationType.Batch;
    }

    public async Task<InvalidationResult> ExecuteAsync(IInvalidationContext context, CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var totalInvalidated = 0;

        try
        {
            switch (context.Type)
            {
                case InvalidationType.Table:
                    totalInvalidated = await InvalidateByTableAsync(context, cancellationToken);
                    break;
                    
                case InvalidationType.Pattern:
                    totalInvalidated = await InvalidateByPatternAsync(context, cancellationToken);
                    break;
                    
                case InvalidationType.Key:
                    totalInvalidated = await InvalidateByKeyAsync(context, cancellationToken);
                    break;
                    
                case InvalidationType.Batch:
                    totalInvalidated = await InvalidateBatchAsync(context, cancellationToken);
                    break;
                    
                default:
                    return InvalidationResult.Failed(
                        $"Unsupported invalidation type: {context.Type}",
                        metadata: new Dictionary<string, object> { ["Context"] = context.ContextId });
            }

            var executionTime = DateTimeOffset.UtcNow - startTime;
            
            return InvalidationResult.Successful(
                totalInvalidated, 
                executionTime, 
                new Dictionary<string, object>
                {
                    ["Strategy"] = StrategyName,
                    ["Context"] = context.ContextId,
                    ["Type"] = context.Type.ToString()
                });
        }
        catch (Exception ex)
        {
            var executionTime = DateTimeOffset.UtcNow - startTime;
            _logger.LogError(ex, "BasicInvalidationStrategy failed for context {ContextId}", context.ContextId);
            
            return InvalidationResult.Failed(
                $"Strategy execution failed: {ex.Message}",
                ex,
                new Dictionary<string, object>
                {
                    ["Strategy"] = StrategyName,
                    ["Context"] = context.ContextId,
                    ["ExecutionTime"] = executionTime.TotalMilliseconds
                });
        }
    }

    private async Task<int> InvalidateByTableAsync(IInvalidationContext context, CancellationToken cancellationToken)
    {
        var tableName = context.Target;
        var totalInvalidated = 0;

        // 모든 캐시 프로바이더에서 추적된 키들을 찾아서 제거
        foreach (var provider in context.CacheProviders)
        {
            try
            {
                var trackingKey = GenerateTrackingKey(tableName);
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

    private async Task<int> InvalidateByPatternAsync(IInvalidationContext context, CancellationToken cancellationToken)
    {
        var pattern = context.Target;
        var totalInvalidated = 0;

        // 모든 캐시 프로바이더에서 패턴 매칭 키들 제거
        var tasks = context.CacheProviders.Select(async provider =>
        {
            try
            {
                var removedCount = await provider.RemoveByPatternAsync(pattern, cancellationToken);
                Interlocked.Add(ref totalInvalidated, removedCount);
                
                _logger.LogDebug(
                    "Invalidated {Count} keys for pattern '{Pattern}' in provider '{Provider}'",
                    removedCount, pattern, provider.ProviderName);
                    
                return removedCount;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "Failed to invalidate pattern '{Pattern}' in provider '{Provider}'",
                    pattern, provider.ProviderName);
                return 0;
            }
        });

        await Task.WhenAll(tasks);
        return totalInvalidated;
    }

    private async Task<int> InvalidateByKeyAsync(IInvalidationContext context, CancellationToken cancellationToken)
    {
        var key = context.Target;
        var totalInvalidated = 0;

        // 모든 캐시 프로바이더에서 특정 키 제거
        var tasks = context.CacheProviders.Select(async provider =>
        {
            try
            {
                var removed = await provider.RemoveAsync(key, cancellationToken);
                if (removed)
                {
                    Interlocked.Increment(ref totalInvalidated);
                    _logger.LogDebug(
                        "Invalidated key '{Key}' in provider '{Provider}'",
                        key, provider.ProviderName);
                }
                return removed ? 1 : 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "Failed to invalidate key '{Key}' in provider '{Provider}'",
                    key, provider.ProviderName);
                return 0;
            }
        });

        await Task.WhenAll(tasks);
        return totalInvalidated;
    }

    private async Task<int> InvalidateBatchAsync(IInvalidationContext context, CancellationToken cancellationToken)
    {
        var tableNames = context.GetMetadata<List<string>>("TableNames", new List<string>()) ?? new List<string>();
        var totalInvalidated = 0;

        // 각 테이블을 순차적으로 무효화
        foreach (var tableName in tableNames)
        {
            var tableContext = context.Clone();
            tableContext.Target = tableName;
            tableContext.Type = InvalidationType.Table;
            
            var invalidated = await InvalidateByTableAsync(tableContext, cancellationToken);
            totalInvalidated += invalidated;
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
        _logger.LogInformation("BasicInvalidationStrategy initialized");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _logger.LogInformation("BasicInvalidationStrategy disposed");
        return Task.CompletedTask;
    }
}
