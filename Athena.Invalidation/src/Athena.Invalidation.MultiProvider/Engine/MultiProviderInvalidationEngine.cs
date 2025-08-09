using Athena.Invalidation.MultiProvider.Abstractions;

namespace Athena.Invalidation.MultiProvider.Engine;

/// <summary>
/// 다중 캐시 제공자 무효화 엔진 구현
/// </summary>
public class MultiProviderInvalidationEngine : IMultiProviderInvalidationEngine, IAsyncDisposable
{
    private readonly IEnumerable<IInvalidationEngine> _engines;
    private readonly ILogger<MultiProviderInvalidationEngine> _logger;
    private readonly MultiProviderInvalidationOptions _options;
    
    // 제공자 관리
    private readonly ConcurrentDictionary<string, IInvalidationEngine> _enginesByName = new();
    private readonly ConcurrentDictionary<string, int> _providerPriorities = new();
    private readonly ConcurrentDictionary<string, bool> _providerEnabled = new();
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    
    // 실행 설정
    private volatile FailureHandlingPolicy _failurePolicy = FailureHandlingPolicy.AtLeastOneSucceeds;
    private volatile bool _parallelExecution = true;
    private volatile int _maxConcurrency = Environment.ProcessorCount;
    
    // 통계
    private long _totalOperations = 0;
    private long _successfulOperations = 0;
    private long _failedOperations = 0;
    
    private volatile bool _disposed = false;

    public int ProvidersCount => _enginesByName.Count;
    
    public IEnumerable<string> ActiveProviders => 
        _providerEnabled.Where(kvp => kvp.Value).Select(kvp => kvp.Key);

    public MultiProviderInvalidationEngine(
        IEnumerable<IInvalidationEngine> engines,
        ILogger<MultiProviderInvalidationEngine> logger,
        IOptions<MultiProviderInvalidationOptions> options)
    {
        _engines = engines ?? throw new ArgumentNullException(nameof(engines));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new MultiProviderInvalidationOptions();
        
        InitializeProviders();
        InitializeSettings();
    }

    public async Task InvalidateByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        Interlocked.Increment(ref _totalOperations);
        
        try
        {
            await ExecuteOnAllProvidersAsync(
                async (engine, name) => await engine.InvalidateByTableAsync(tableName, cancellationToken),
                $"InvalidateByTable: {tableName}",
                cancellationToken);
                
            Interlocked.Increment(ref _successfulOperations);
            _logger.LogInformation("Successfully invalidated table {TableName} across {ProviderCount} providers", 
                tableName, ActiveProviders.Count());
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedOperations);
            _logger.LogError(ex, "Failed to invalidate table {TableName} on some providers", tableName);
            throw;
        }
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        Interlocked.Increment(ref _totalOperations);
        
        try
        {
            await ExecuteOnAllProvidersAsync(
                async (engine, name) => await engine.InvalidateByPatternAsync(pattern, cancellationToken),
                $"InvalidateByPattern: {pattern}",
                cancellationToken);
                
            Interlocked.Increment(ref _successfulOperations);
            _logger.LogInformation("Successfully invalidated pattern {Pattern} across {ProviderCount} providers", 
                pattern, ActiveProviders.Count());
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedOperations);
            _logger.LogError(ex, "Failed to invalidate pattern {Pattern} on some providers", pattern);
            throw;
        }
    }

    public async Task InvalidateByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        Interlocked.Increment(ref _totalOperations);
        
        try
        {
            await ExecuteOnAllProvidersAsync(
                async (engine, name) => await engine.InvalidateByKeyAsync(key, cancellationToken),
                $"InvalidateByKey: {key}",
                cancellationToken);
                
            Interlocked.Increment(ref _successfulOperations);
            _logger.LogDebug("Successfully invalidated key {Key} across {ProviderCount} providers", 
                key, ActiveProviders.Count());
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedOperations);
            _logger.LogError(ex, "Failed to invalidate key {Key} on some providers", key);
            throw;
        }
    }

    public async Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        var tableList = tableNames.ToList();
        if (!tableList.Any()) return;
        
        Interlocked.Increment(ref _totalOperations);
        
        try
        {
            await ExecuteOnAllProvidersAsync(
                async (engine, name) => await engine.InvalidateBatchAsync(tableList, cancellationToken),
                $"InvalidateBatch: {tableList.Count} tables",
                cancellationToken);
                
            Interlocked.Increment(ref _successfulOperations);
            _logger.LogInformation("Successfully invalidated {TableCount} tables in batch across {ProviderCount} providers", 
                tableList.Count, ActiveProviders.Count());
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedOperations);
            _logger.LogError(ex, "Failed to invalidate batch on some providers");
            throw;
        }
    }

    public async Task InvalidateHierarchyAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        Interlocked.Increment(ref _totalOperations);
        
        try
        {
            await ExecuteOnAllProvidersAsync(
                async (engine, name) => await engine.InvalidateHierarchyAsync(tableName, relatedTables, maxDepth, cancellationToken),
                $"InvalidateHierarchy: {tableName} with {relatedTables.Length} related tables",
                cancellationToken);
                
            Interlocked.Increment(ref _successfulOperations);
            _logger.LogInformation("Successfully invalidated hierarchy for {TableName} across {ProviderCount} providers", 
                tableName, ActiveProviders.Count());
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedOperations);
            _logger.LogError(ex, "Failed to invalidate hierarchy for {TableName} on some providers", tableName);
            throw;
        }
    }

    public async Task InvalidateOnCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        await ExecuteOnAllProvidersAsync(
            async (engine, name) => await engine.InvalidateOnCommandAsync(command, cancellationToken),
            $"InvalidateOnCommand: {typeof(TCommand).Name}",
            cancellationToken);
    }

    public async Task InvalidateOnEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        await ExecuteOnAllProvidersAsync(
            async (engine, name) => await engine.InvalidateOnEventAsync(domainEvent, cancellationToken),
            $"InvalidateOnEvent: {typeof(TEvent).Name}",
            cancellationToken);
    }

    public Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default) where TReadModel : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        return ExecuteOnAllProvidersAsync(
            async (engine, name) => await engine.InvalidateReadModelAsync<TReadModel>(modelId, cancellationToken),
            $"InvalidateReadModel: {typeof(TReadModel).Name}",
            cancellationToken);
    }

    public Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default) where TProjection : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        return ExecuteOnAllProvidersAsync(
            async (engine, name) => await engine.InvalidateProjectionAsync<TProjection>(projectionId, cancellationToken),
            $"InvalidateProjection: {typeof(TProjection).Name}",
            cancellationToken);
    }

    public async Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        await ExecuteOnAllProvidersAsync(
            async (engine, name) => await engine.TrackCacheKeyAsync(tableName, cacheKey, cancellationToken),
            $"TrackCacheKey: {cacheKey} for {tableName}",
            cancellationToken);
    }

    public async Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        await ExecuteOnAllProvidersAsync(
            async (engine, name) => await engine.TrackCacheKeyAsync(tableNames, cacheKey, cancellationToken),
            $"TrackCacheKey: {cacheKey} for {tableNames.Length} tables",
            cancellationToken);
    }

    public async Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        var allKeys = new HashSet<string>();
        
        await ExecuteOnAllProvidersAsync(
            async (engine, name) =>
            {
                try
                {
                    var keys = await engine.GetTrackedKeysAsync(tableName, cancellationToken);
                    lock (allKeys)
                    {
                        foreach (var key in keys)
                        {
                            allKeys.Add(key);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get tracked keys from provider {ProviderName}", name);
                }
            },
            $"GetTrackedKeys: {tableName}",
            cancellationToken);
            
        return allKeys;
    }

    public Task RegisterInvalidationRuleAsync(IInvalidationRule rule, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        return ExecuteOnAllProvidersAsync(
            async (engine, name) => await engine.RegisterInvalidationRuleAsync(rule, cancellationToken),
            $"RegisterRule: {rule.Id}",
            cancellationToken);
    }

    public Task UnregisterInvalidationRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        return ExecuteOnAllProvidersAsync(
            async (engine, name) => await engine.UnregisterInvalidationRuleAsync(ruleId, cancellationToken),
            $"UnregisterRule: {ruleId}",
            cancellationToken);
    }

    public async Task<IEnumerable<IInvalidationRule>> GetInvalidationRulesAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        var allRules = new Dictionary<string, IInvalidationRule>();
        
        await ExecuteOnAllProvidersAsync(
            async (engine, name) =>
            {
                try
                {
                    var rules = await engine.GetInvalidationRulesAsync(cancellationToken);
                    lock (allRules)
                    {
                        foreach (var rule in rules)
                        {
                            allRules[rule.Id] = rule;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get invalidation rules from provider {ProviderName}", name);
                }
            },
            "GetInvalidationRules",
            cancellationToken);
            
        return allRules.Values;
    }

    public IInvalidationContext CreateContext(InvalidationTrigger trigger, object? metadata = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        return new MultiProviderInvalidationContext
        {
            Trigger = trigger,
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = metadata as Dictionary<string, object> ?? new Dictionary<string, object>
            {
                ["multi_provider_engine"] = true,
                ["active_providers"] = ActiveProviders.ToArray(),
                ["providers_count"] = ProvidersCount,
                ["parallel_execution"] = _parallelExecution,
                ["failure_policy"] = _failurePolicy.ToString()
            }
        };
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        if (!_options.AllowClearAll)
        {
            _logger.LogWarning("Clear all operation is disabled for safety in multi-provider mode");
            throw new InvalidOperationException("Clear all operation is disabled for safety in multi-provider mode");
        }
        
        _logger.LogWarning("Performing clear all operation across {ProviderCount} providers - this is a dangerous operation", 
            ActiveProviders.Count());
        
        await ExecuteOnAllProvidersAsync(
            async (engine, name) => await engine.ClearAllAsync(cancellationToken),
            "ClearAll",
            cancellationToken);
    }

    public async Task<InvalidationEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        var providerStatuses = await GetProvidersStatusAsync(cancellationToken);
        var allHealthy = providerStatuses.Values.All(s => s.IsHealthy);
        
        return new InvalidationEngineStatus
        {
            IsHealthy = allHealthy,
            Uptime = DateTimeOffset.UtcNow - _startTime,
            TrackedKeysCount = providerStatuses.Values.Sum(s => s.TrackedKeysCount),
            RegisteredRulesCount = providerStatuses.Values.Max(s => s.RegisteredRulesCount),
            LastActivity = DateTimeOffset.UtcNow,
            Metrics = new Dictionary<string, object>
            {
                ["engine_type"] = "MultiProvider",
                ["providers_count"] = ProvidersCount,
                ["active_providers_count"] = ActiveProviders.Count(),
                ["parallel_execution"] = _parallelExecution,
                ["max_concurrency"] = _maxConcurrency,
                ["failure_policy"] = _failurePolicy.ToString(),
                ["total_operations"] = _totalOperations,
                ["successful_operations"] = _successfulOperations,
                ["failed_operations"] = _failedOperations,
                ["success_rate"] = _totalOperations > 0 ? (double)_successfulOperations / _totalOperations : 1.0
            }
        };
    }

    // Multi-Provider specific methods
    
    public async Task<Dictionary<string, InvalidationEngineStatus>> GetProvidersStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        var results = new ConcurrentDictionary<string, InvalidationEngineStatus>();
        
        await ExecuteOnAllProvidersAsync(
            async (engine, name) =>
            {
                try
                {
                    var status = await engine.GetStatusAsync(cancellationToken);
                    results[name] = status;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get status from provider {ProviderName}", name);
                    results[name] = new InvalidationEngineStatus
                    {
                        IsHealthy = false,
                        Uptime = TimeSpan.Zero,
                        TrackedKeysCount = -1,
                        RegisteredRulesCount = -1,
                        LastActivity = DateTimeOffset.UtcNow,
                        Metrics = new Dictionary<string, object> { ["error"] = ex.Message }
                    };
                }
            },
            "GetProvidersStatus",
            cancellationToken);
            
        return results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public Task InvalidateByTableOnProviderAsync(string tableName, string providerName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        if (!_enginesByName.TryGetValue(providerName, out var engine))
        {
            throw new ArgumentException($"Provider '{providerName}' not found", nameof(providerName));
        }
        
        return engine.InvalidateByTableAsync(tableName, cancellationToken);
    }

    public async Task InvalidateByTableOnProvidersAsync(string tableName, IEnumerable<string> providerNames, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        var selectedEngines = new List<(IInvalidationEngine engine, string name)>();
        
        foreach (var providerName in providerNames)
        {
            if (_enginesByName.TryGetValue(providerName, out var engine))
            {
                selectedEngines.Add((engine, providerName));
            }
            else
            {
                _logger.LogWarning("Provider '{ProviderName}' not found, skipping", providerName);
            }
        }
        
        if (selectedEngines.Any())
        {
            await ExecuteOnProvidersAsync(
                selectedEngines,
                async (engine, name) => await engine.InvalidateByTableAsync(tableName, cancellationToken),
                $"InvalidateByTableOnProviders: {tableName}",
                cancellationToken);
        }
    }

    public Task SetParallelExecutionAsync(bool enabled, int? maxConcurrency = null, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        _parallelExecution = enabled;
        if (maxConcurrency.HasValue && maxConcurrency.Value > 0)
        {
            _maxConcurrency = maxConcurrency.Value;
        }
        
        _logger.LogInformation("Parallel execution set to {Enabled}, max concurrency: {MaxConcurrency}", 
            enabled, _maxConcurrency);
            
        return Task.CompletedTask;
    }

    public Task SetProviderPriorityAsync(string providerName, int priority, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        if (!_enginesByName.ContainsKey(providerName))
        {
            throw new ArgumentException($"Provider '{providerName}' not found", nameof(providerName));
        }
        
        _providerPriorities[providerName] = priority;
        _logger.LogDebug("Set priority {Priority} for provider {ProviderName}", priority, providerName);
        
        return Task.CompletedTask;
    }

    public Task SetProviderEnabledAsync(string providerName, bool enabled, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        if (!_enginesByName.ContainsKey(providerName))
        {
            throw new ArgumentException($"Provider '{providerName}' not found", nameof(providerName));
        }
        
        _providerEnabled[providerName] = enabled;
        _logger.LogInformation("Provider {ProviderName} {Status}", 
            providerName, enabled ? "enabled" : "disabled");
        
        return Task.CompletedTask;
    }

    public Task SetFailureHandlingPolicyAsync(FailureHandlingPolicy policy, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiProviderInvalidationEngine));
        
        _failurePolicy = policy;
        _logger.LogInformation("Failure handling policy set to {Policy}", policy);
        
        return Task.CompletedTask;
    }

    private void InitializeProviders()
    {
        var providerIndex = 0;
        foreach (var engine in _engines)
        {
            var providerName = _options.ProviderNames?.ElementAtOrDefault(providerIndex) 
                ?? $"Provider_{providerIndex}";
            
            _enginesByName[providerName] = engine;
            _providerPriorities[providerName] = providerIndex;
            _providerEnabled[providerName] = true;
            
            providerIndex++;
        }
        
        _logger.LogInformation("Initialized {ProviderCount} cache providers: {ProviderNames}", 
            ProvidersCount, string.Join(", ", _enginesByName.Keys));
    }

    private void InitializeSettings()
    {
        _failurePolicy = _options.DefaultFailurePolicy;
        _parallelExecution = _options.EnableParallelExecution;
        _maxConcurrency = _options.MaxConcurrency;
    }

    private async Task ExecuteOnAllProvidersAsync(
        Func<IInvalidationEngine, string, Task> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        var activeProviders = _enginesByName
            .Where(kvp => _providerEnabled.GetValueOrDefault(kvp.Key, true))
            .Select(kvp => (kvp.Value, kvp.Key))
            .ToList();
            
        await ExecuteOnProvidersAsync(activeProviders, operation, operationName, cancellationToken);
    }

    private async Task ExecuteOnProvidersAsync(
        IEnumerable<(IInvalidationEngine engine, string name)> providers,
        Func<IInvalidationEngine, string, Task> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        var providerList = providers.ToList();
        if (!providerList.Any()) return;
        
        var exceptions = new ConcurrentBag<Exception>();
        var successCount = 0;
        
        if (_parallelExecution && providerList.Count > 1)
        {
            // 병렬 실행
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Min(_maxConcurrency, providerList.Count)
            };
            
            await Parallel.ForEachAsync(providerList, parallelOptions, async (provider, ct) =>
            {
                try
                {
                    await operation(provider.engine, provider.name);
                    Interlocked.Increment(ref successCount);
                }
                catch (Exception ex)
                {
                    exceptions.Add(new InvalidOperationException($"Provider '{provider.name}' failed: {ex.Message}", ex));
                    _logger.LogWarning(ex, "Operation {OperationName} failed on provider {ProviderName}", 
                        operationName, provider.name);
                }
            });
        }
        else
        {
            // 순차 실행 (우선순위 고려)
            var sortedProviders = providerList
                .OrderBy(p => _providerPriorities.GetValueOrDefault(p.name, int.MaxValue))
                .ToList();
            
            foreach (var provider in sortedProviders)
            {
                try
                {
                    await operation(provider.engine, provider.name);
                    successCount++;
                }
                catch (Exception ex)
                {
                    exceptions.Add(new InvalidOperationException($"Provider '{provider.name}' failed: {ex.Message}", ex));
                    _logger.LogWarning(ex, "Operation {OperationName} failed on provider {ProviderName}", 
                        operationName, provider.name);
                }
            }
        }
        
        // 실패 처리 정책 적용
        HandleFailures(exceptions, successCount, providerList.Count, operationName);
    }

    private void HandleFailures(ConcurrentBag<Exception> exceptions, int successCount, int totalCount, string operationName)
    {
        if (!exceptions.Any())
        {
            return; // 모든 작업이 성공
        }
        
        var shouldThrow = _failurePolicy switch
        {
            FailureHandlingPolicy.AllMustSucceed => exceptions.Any(),
            FailureHandlingPolicy.AtLeastOneSucceeds => successCount == 0,
            FailureHandlingPolicy.MajoritySucceeds => successCount <= totalCount / 2,
            FailureHandlingPolicy.IgnoreErrors => false,
            _ => exceptions.Any()
        };
        
        if (shouldThrow)
        {
            var aggregateException = new AggregateException($"Operation '{operationName}' failed according to policy {_failurePolicy}", exceptions);
            _logger.LogError(aggregateException, "Operation {OperationName} failed with {SuccessCount}/{TotalCount} successes using policy {Policy}", 
                operationName, successCount, totalCount, _failurePolicy);
            throw aggregateException;
        }
        
        _logger.LogWarning("Operation {OperationName} completed with {SuccessCount}/{TotalCount} successes, {FailureCount} failures (policy: {Policy})", 
            operationName, successCount, totalCount, exceptions.Count, _failurePolicy);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        try
        {
            foreach (var engine in _enginesByName.Values)
            {
                if (engine is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else if (engine is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            
            _logger.LogDebug("MultiProviderInvalidationEngine disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during MultiProviderInvalidationEngine disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// 다중 제공자 무효화 엔진 옵션
/// </summary>
public class MultiProviderInvalidationOptions
{
    /// <summary>제공자 이름 목록</summary>
    public string[]? ProviderNames { get; set; }
    
    /// <summary>기본 실패 처리 정책</summary>
    public FailureHandlingPolicy DefaultFailurePolicy { get; set; } = FailureHandlingPolicy.AtLeastOneSucceeds;
    
    /// <summary>병렬 실행 활성화</summary>
    public bool EnableParallelExecution { get; set; } = true;
    
    /// <summary>최대 동시 실행 수</summary>
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
    
    /// <summary>전체 클리어 허용 여부</summary>
    public bool AllowClearAll { get; set; } = false;
    
    /// <summary>제공자별 타임아웃</summary>
    public TimeSpan ProviderTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>통계 수집 활성화</summary>
    public bool EnableStatistics { get; set; } = true;
}

/// <summary>
/// 다중 제공자 무효화 컨텍스트
/// </summary>
public class MultiProviderInvalidationContext : IInvalidationContext
{
    public InvalidationTrigger Trigger { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    
    // TODO: IInvalidationContext 인터페이스의 나머지 멤버들을 구현해야 함
    // 현재는 빌드 오류를 피하기 위해 기본 구현만 제공
}