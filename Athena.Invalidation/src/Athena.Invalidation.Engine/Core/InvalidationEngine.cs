using Microsoft.Extensions.DependencyInjection;
using Athena.Invalidation.CQRS.Abstractions;

namespace Athena.Invalidation.Engine.Core;

/// <summary>
/// 범용 캐시 무효화 엔진의 핵심 구현
/// 다양한 캐시 프로바이더와 무효화 전략을 조합하여 사용
/// </summary>
public class InvalidationEngine : IInvalidationEngine, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<ICacheProvider> _cacheProviders;
    private readonly IEnumerable<IInvalidationStrategy> _strategies;
    private readonly IOptionsMonitor<InvalidationOptions> _optionsMonitor;
    private readonly ILogger<InvalidationEngine> _logger;
    
    private readonly Dictionary<string, IInvalidationRule> _rules = new();
    private readonly SemaphoreSlim _semaphore;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    
    private volatile bool _disposed = false;

    public InvalidationEngine(
        IServiceProvider serviceProvider,
        IEnumerable<ICacheProvider> cacheProviders,
        IEnumerable<IInvalidationStrategy> strategies,
        IOptionsMonitor<InvalidationOptions> optionsMonitor,
        ILogger<InvalidationEngine> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _cacheProviders = cacheProviders ?? throw new ArgumentNullException(nameof(cacheProviders));
        _strategies = strategies.OrderByDescending(s => s.Priority) ?? throw new ArgumentNullException(nameof(strategies));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var options = _optionsMonitor.CurrentValue;
        _semaphore = new SemaphoreSlim(options.MaxConcurrentInvalidations, options.MaxConcurrentInvalidations);

        _logger.LogInformation("InvalidationEngine initialized with {ProviderCount} cache providers and {StrategyCount} strategies", 
            _cacheProviders.Count(), _strategies.Count());
    }

    public async Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var context = CreateContext(InvalidationTrigger.System("InvalidateByTable"), tableName);
        context.Type = InvalidationType.Table;
        
        await ExecuteInvalidationAsync(context, cancellationToken);
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var context = CreateContext(InvalidationTrigger.System("InvalidateByPattern"), pattern);
        context.Type = InvalidationType.Pattern;
        
        await ExecuteInvalidationAsync(context, cancellationToken);
    }

    public async Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var context = CreateContext(InvalidationTrigger.System("InvalidateByKey"), key);
        context.Type = InvalidationType.Key;
        
        await ExecuteInvalidationAsync(context, cancellationToken);
    }

    public async Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        var tables = tableNames.ToList();
        if (tables.Count == 0) return;

        var context = CreateContext(InvalidationTrigger.System("InvalidateBatch"), string.Join(",", tables));
        context.Type = InvalidationType.Batch;
        context.AddMetadata("TableNames", tables);
        
        await ExecuteInvalidationAsync(context, cancellationToken);
    }

    public async Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        var context = CreateContext(InvalidationTrigger.System("InvalidateHierarchy"), tableName);
        context.Type = InvalidationType.Hierarchy;
        context.AddMetadata("RelatedTables", relatedTables);
        context.AddMetadata("MaxDepth", maxDepth);
        
        await ExecuteInvalidationAsync(context, cancellationToken);
    }

    public async Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default)
    {
        await TrackCacheKeyAsync([tableName], cacheKey, cancellationToken);
    }

    public async Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (tableNames == null || tableNames.Length == 0) return;

        try
        {
            var options = _optionsMonitor.CurrentValue;
            var tasks = new List<Task>();

            foreach (var tableName in tableNames)
            {
                var trackingKey = GenerateTrackingKey(tableName);
                
                foreach (var provider in _cacheProviders)
                {
                    tasks.Add(AddToTrackingSetAsync(provider, trackingKey, cacheKey, options.TrackingKeyExpiration, cancellationToken));
                }
            }

            await Task.WhenAll(tasks);

            if (options.Logging.LogDebugInfo)
            {
                _logger.LogDebug("Tracked cache key '{CacheKey}' for tables [{Tables}]", 
                    cacheKey, string.Join(", ", tableNames));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track cache key '{CacheKey}' for tables [{Tables}]",
                cacheKey, string.Join(", ", tableNames));
        }
    }

    public async Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default)
    {
        try
        {
            var trackingKey = GenerateTrackingKey(tableName);
            var allKeys = new HashSet<string>();

            foreach (var provider in _cacheProviders)
            {
                var keys = await GetFromTrackingSetAsync(provider, trackingKey, cancellationToken);
                if (keys != null)
                {
                    foreach (var key in keys)
                    {
                        allKeys.Add(key);
                    }
                }
            }

            return allKeys;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get tracked keys for table '{TableName}'", tableName);
            return [];
        }
    }

    public Task RegisterInvalidationRuleAsync(IInvalidationRule rule, CancellationToken cancellationToken = default)
    {
        if (rule == null) throw new ArgumentNullException(nameof(rule));

        _rules[rule.Id] = rule;
        _logger.LogInformation("Registered invalidation rule '{RuleId}': {RuleName}", rule.Id, rule.Name);
        
        return Task.CompletedTask;
    }

    public Task UnregisterInvalidationRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        if (_rules.Remove(ruleId))
        {
            _logger.LogInformation("Unregistered invalidation rule '{RuleId}'", ruleId);
        }
        
        return Task.CompletedTask;
    }

    public Task<IEnumerable<IInvalidationRule>> GetInvalidationRulesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_rules.Values.AsEnumerable());
    }

    public IInvalidationContext CreateContext(InvalidationTrigger trigger, object? metadata = null)
    {
        var context = new InvalidationContext(trigger);
        context.Target = metadata?.ToString() ?? string.Empty;
        context.Type = InvalidationType.Custom;
        context.CacheProviders = _cacheProviders;
        context.Priority = 0;
        context.Timeout = _optionsMonitor.CurrentValue.DefaultTimeout;
        context.MaxRetries = _optionsMonitor.CurrentValue.DefaultMaxRetries;
        
        return context;
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("ClearAll operation requested - this will remove ALL cached data!");
        
        var tasks = _cacheProviders.Select(async provider =>
        {
            try
            {
                await provider.RemoveByPatternAsync("*", cancellationToken);
                _logger.LogInformation("Cleared all cache entries from provider '{ProviderName}'", provider.ProviderName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear all cache entries from provider '{ProviderName}'", provider.ProviderName);
            }
        });

        await Task.WhenAll(tasks);
    }

    public async Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = new InvalidationEngineStatus
        {
            IsHealthy = true,
            ActiveRules = _rules.Count,
            TrackedKeys = 0,
            Uptime = DateTimeOffset.UtcNow - _startTime
        };

        // 캐시 프로바이더 상태 체크
        var healthTasks = _cacheProviders.Select(async provider =>
        {
            try
            {
                var isHealthy = await provider.IsHealthyAsync(cancellationToken);
                var stats = await provider.GetStatisticsAsync(cancellationToken);
                
                status.Metrics[$"{provider.ProviderName}_Healthy"] = isHealthy;
                status.Metrics[$"{provider.ProviderName}_TotalKeys"] = stats.TotalKeys;
                status.Metrics[$"{provider.ProviderName}_HitRatio"] = stats.HitRatio;
                
                status.TrackedKeys += (int)stats.TotalKeys;
                
                if (!isHealthy) status.IsHealthy = false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Health check failed for cache provider '{ProviderName}'", provider.ProviderName);
                status.IsHealthy = false;
                status.Metrics[$"{provider.ProviderName}_Error"] = ex.Message;
            }
        });

        await Task.WhenAll(healthTasks);

        return status;
    }

    // CQRS 지원 메서드 구현
    
    public async Task InvalidateOnCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class
    {
        try
        {
            // CQRS 명령 핸들러 호출 (dynamic typing 사용)
            var commandHandler = _serviceProvider.GetService<ICommandInvalidationHandler>();
            if (commandHandler != null)
            {
                try
                {
                    var canHandleMethod = typeof(ICommandInvalidationHandler)
                        .GetMethod("CanHandle")!
                        .MakeGenericMethod(typeof(TCommand));
                    var canHandleResult = canHandleMethod.Invoke(commandHandler, [command!]);
                    var canHandle = canHandleResult != null ? (bool)canHandleResult : false;
                    
                    if (canHandle)
                    {
                        var handleMethod = typeof(ICommandInvalidationHandler)
                            .GetMethod("HandleCommandAsync")!
                            .MakeGenericMethod(typeof(TCommand));
                        var handleResult = handleMethod.Invoke(commandHandler, [command!, cancellationToken]);
                        if (handleResult is Task handleTask)
                        {
                            await handleTask;
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to invoke command handler for {CommandType}", typeof(TCommand).Name);
                }
            }

            // 기본 명령 기반 무효화 로직
            var commandType = typeof(TCommand);
            var tableName = InferTableFromCommandType(commandType.Name);
            
            if (!string.IsNullOrEmpty(tableName))
            {
                await InvalidateByTableAsync(tableName, cancellationToken);
                _logger.LogDebug("Command-based invalidation completed for {CommandType} -> {TableName}", 
                    commandType.Name, tableName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate on command {CommandType}", typeof(TCommand).Name);
            throw;
        }
    }

    public async Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        try
        {
            // CQRS 이벤트 핸들러 호출 (dynamic typing 사용)
            var eventHandler = _serviceProvider.GetService<IEventDrivenInvalidation>();
            if (eventHandler != null)
            {
                try
                {
                    var canHandleMethod = typeof(IEventDrivenInvalidation)
                        .GetMethod("CanHandle")!
                        .MakeGenericMethod(typeof(TEvent));
                    var canHandleResult = canHandleMethod.Invoke(eventHandler, [domainEvent!]);
                    var canHandle = canHandleResult != null ? (bool)canHandleResult : false;
                    
                    if (canHandle)
                    {
                        var handleMethod = typeof(IEventDrivenInvalidation)
                            .GetMethod("HandleEventAsync")!
                            .MakeGenericMethod(typeof(TEvent));
                        var handleResult = handleMethod.Invoke(eventHandler, [domainEvent!, cancellationToken]);
                        if (handleResult is Task handleTask)
                        {
                            await handleTask;
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to invoke event handler for {EventType}", typeof(TEvent).Name);
                }
            }

            // 기본 이벤트 기반 무효화 로직
            var eventType = typeof(TEvent);
            var tableName = InferTableFromEventType(eventType.Name);
            
            if (!string.IsNullOrEmpty(tableName))
            {
                await InvalidateByTableAsync(tableName, cancellationToken);
                _logger.LogDebug("Event-based invalidation completed for {EventType} -> {TableName}", 
                    eventType.Name, tableName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate on event {EventType}", typeof(TEvent).Name);
            throw;
        }
    }

    public async Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default)
        where TReadModel : class
    {
        try
        {
            // CQRS 읽기 모델 무효화 핸들러 호출 (dynamic typing 사용)
            var readModelInvalidator = _serviceProvider.GetService<IReadModelInvalidator>();
            if (readModelInvalidator != null)
            {
                try
                {
                    var invalidateMethod = typeof(IReadModelInvalidator)
                        .GetMethod("InvalidateReadModelAsync")!
                        .MakeGenericMethod(typeof(TReadModel));
                    var invalidateResult = invalidateMethod.Invoke(readModelInvalidator, [modelId, cancellationToken]);
                    if (invalidateResult is Task invalidateTask)
                    {
                        await invalidateTask;
                    }
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to invoke read model invalidator for {ReadModelType}", typeof(TReadModel).Name);
                }
            }

            // 기본 읽기 모델 무효화 로직
            var readModelType = typeof(TReadModel);
            var tableName = InferTableFromReadModelType(readModelType.Name);
            
            if (!string.IsNullOrEmpty(modelId))
            {
                var pattern = $"{tableName.ToLower()}:{modelId}:*";
                await InvalidateByPatternAsync(pattern, cancellationToken);
            }
            else
            {
                await InvalidateByTableAsync(tableName, cancellationToken);
            }
            
            _logger.LogDebug("Read model invalidation completed for {ReadModelType}", readModelType.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate read model {ReadModelType}", typeof(TReadModel).Name);
            throw;
        }
    }

    public async Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default)
        where TProjection : class
    {
        try
        {
            // CQRS 프로젝션 무효화 핸들러 호출 (dynamic typing 사용)
            var readModelInvalidator = _serviceProvider.GetService<IReadModelInvalidator>();
            if (readModelInvalidator != null)
            {
                try
                {
                    var invalidateProjectionMethod = typeof(IReadModelInvalidator)
                        .GetMethod("InvalidateProjectionAsync")!
                        .MakeGenericMethod(typeof(TProjection));
                    var invalidateProjectionResult = invalidateProjectionMethod.Invoke(readModelInvalidator, [projectionId, cancellationToken]);
                    if (invalidateProjectionResult is Task invalidateProjectionTask)
                    {
                        await invalidateProjectionTask;
                    }
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to invoke projection invalidator for {ProjectionType}", typeof(TProjection).Name);
                }
            }

            // 기본 프로젝션 무효화 로직
            var projectionType = typeof(TProjection);
            var tableName = InferTableFromProjectionType(projectionType.Name);
            
            if (!string.IsNullOrEmpty(projectionId))
            {
                var pattern = $"{tableName.ToLower()}:{projectionId}:*";
                await InvalidateByPatternAsync(pattern, cancellationToken);
            }
            else
            {
                await InvalidateByTableAsync(tableName, cancellationToken);
            }
            
            // 프로젝션 특화 캐시도 무효화
            await InvalidateByPatternAsync($"projection:{tableName.ToLower()}:*", cancellationToken);
            
            _logger.LogDebug("Projection invalidation completed for {ProjectionType}", projectionType.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate projection {ProjectionType}", typeof(TProjection).Name);
            throw;
        }
    }

    private async Task ExecuteInvalidationAsync(IInvalidationContext context, CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InvalidationEngine));

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var startTime = DateTimeOffset.UtcNow;
            var options = _optionsMonitor.CurrentValue;
            
            if (options.Logging.LogInvalidationEvents)
            {
                _logger.LogInformation("Starting invalidation: {Type} - {Target}", context.Type, context.Target);
            }

            // 적용 가능한 전략 찾기
            var applicableStrategy = _strategies.FirstOrDefault(s => s.CanHandle(context));
            if (applicableStrategy == null)
            {
                _logger.LogWarning("No applicable strategy found for invalidation context: {Type} - {Target}", 
                    context.Type, context.Target);
                return;
            }

            // 전략 실행
            var result = await applicableStrategy.ExecuteAsync(context, cancellationToken);
            
            var executionTime = DateTimeOffset.UtcNow - startTime;
            
            if (result.Success)
            {
                if (options.Logging.LogInvalidationEvents)
                {
                    _logger.LogInformation(
                        "Invalidation completed successfully: {Type} - {Target} | Strategy: {Strategy} | Count: {Count} | Time: {Time}ms",
                        context.Type, context.Target, applicableStrategy.StrategyName, result.InvalidatedCount, executionTime.TotalMilliseconds);
                }
            }
            else
            {
                _logger.LogError(
                    "Invalidation failed: {Type} - {Target} | Strategy: {Strategy} | Error: {Error}",
                    context.Type, context.Target, applicableStrategy.StrategyName, result.ErrorMessage);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private string GenerateTrackingKey(string tableName)
    {
        return $"invalidation:tracking:{tableName}";
    }

    private async Task AddToTrackingSetAsync(ICacheProvider provider, string trackingKey, string cacheKey, TimeSpan expiration, CancellationToken cancellationToken)
    {
        try
        {
            var existingSet = await provider.GetAsync<HashSet<string>>(trackingKey, cancellationToken) ?? [];
            existingSet.Add(cacheKey);
            await provider.SetAsync(trackingKey, existingSet, expiration, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to add key to tracking set in provider '{ProviderName}'", provider.ProviderName);
        }
    }

    private async Task<IEnumerable<string>?> GetFromTrackingSetAsync(ICacheProvider provider, string trackingKey, CancellationToken cancellationToken)
    {
        try
        {
            var set = await provider.GetAsync<HashSet<string>>(trackingKey, cancellationToken);
            return set?.AsEnumerable();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get tracking set from provider '{ProviderName}'", provider.ProviderName);
            return null;
        }
    }

    private string InferTableFromCommandType(string commandTypeName)
    {
        // CreateUserCommand -> Users
        // UpdateOrderCommand -> Orders
        if (commandTypeName.EndsWith("Command"))
        {
            var commandName = commandTypeName.Replace("Command", "");
            var prefixesToRemove = new[] { "Create", "Update", "Delete", "Modify" };
            
            foreach (var prefix in prefixesToRemove)
            {
                if (commandName.StartsWith(prefix))
                {
                    commandName = commandName.Substring(prefix.Length);
                    break;
                }
            }
            
            return string.IsNullOrEmpty(commandName) ? string.Empty : 
                   (commandName.EndsWith("s") ? commandName : commandName + "s");
        }
        
        return string.Empty;
    }

    private string InferTableFromEventType(string eventTypeName)
    {
        // UserCreatedEvent -> Users
        // OrderUpdatedEvent -> Orders
        if (eventTypeName.EndsWith("Event"))
        {
            var eventName = eventTypeName.Replace("Event", "");
            var suffixesToRemove = new[] { "Created", "Updated", "Deleted", "Changed", "Modified" };
            
            foreach (var suffix in suffixesToRemove)
            {
                if (eventName.EndsWith(suffix))
                {
                    eventName = eventName.Substring(0, eventName.Length - suffix.Length);
                    break;
                }
            }
            
            return string.IsNullOrEmpty(eventName) ? string.Empty : 
                   (eventName.EndsWith("s") ? eventName : eventName + "s");
        }
        
        return string.Empty;
    }

    private string InferTableFromReadModelType(string readModelTypeName)
    {
        // UserReadModel -> Users
        // OrderSummaryReadModel -> OrderSummarys
        var tableName = readModelTypeName;
        if (tableName.EndsWith("ReadModel"))
        {
            tableName = tableName.Replace("ReadModel", "");
        }
        
        return string.IsNullOrEmpty(tableName) ? string.Empty : 
               (tableName.EndsWith("s") ? tableName : tableName + "s");
    }

    private string InferTableFromProjectionType(string projectionTypeName)
    {
        // UserProjection -> Users
        // OrderSummaryProjection -> OrderSummarys
        var tableName = projectionTypeName;
        if (tableName.EndsWith("Projection"))
        {
            tableName = tableName.Replace("Projection", "");
        }
        
        return string.IsNullOrEmpty(tableName) ? string.Empty : 
               (tableName.EndsWith("s") ? tableName : tableName + "s");
    }

    public void Dispose()
    {
        if (_disposed) return;

        _semaphore?.Dispose();
        _disposed = true;
        
        _logger.LogInformation("InvalidationEngine disposed");
    }
}
