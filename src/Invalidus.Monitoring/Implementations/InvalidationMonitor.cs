using Invalidus.Core.Abstractions;
using Invalidus.Monitoring.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Invalidus.Monitoring.Implementations;

/// <summary>
/// 통합 무효화 모니터링 구현체
/// Unified invalidation monitoring implementation
/// </summary>
public class InvalidationMonitor : IInvalidationMonitor, IDisposable
{
    #region Fields

    private readonly IEnumerable<ICacheProvider> _cacheProviders;
    private readonly IInvalidationEngine? _invalidationEngine;
    private readonly ILogger<InvalidationMonitor> _logger;
    private readonly InvalidationMonitorOptions _options;
    
    // Event storage
    private readonly ConcurrentQueue<InvalidationEvent> _invalidationEvents = new();
    private readonly ConcurrentQueue<CacheOperationEvent> _cacheEvents = new();
    private readonly ConcurrentQueue<PerformanceEvent> _performanceEvents = new();
    
    // Alert management
    private AlertConfiguration _alertConfiguration = new();
    private readonly ConcurrentDictionary<string, ActiveAlert> _activeAlerts = new();
    private readonly Timer _alertEvaluationTimer;
    
    // Metrics aggregation
    private readonly Timer _metricsCollectionTimer;
    private InvalidationMetrics _lastMetrics = new();
    
    private volatile bool _disposed = false;

    #endregion

    #region Events

    public event EventHandler<AlertTriggeredEventArgs>? AlertTriggered;

    #endregion

    #region Constructor

    public InvalidationMonitor(
        IEnumerable<ICacheProvider> cacheProviders,
        ILogger<InvalidationMonitor> logger,
        IOptions<InvalidationMonitorOptions> options,
        IInvalidationEngine? invalidationEngine = null)
    {
        _cacheProviders = cacheProviders ?? throw new ArgumentNullException(nameof(cacheProviders));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? new InvalidationMonitorOptions();
        _invalidationEngine = invalidationEngine;
        
        // Setup timers
        _alertEvaluationTimer = new Timer(EvaluateAlerts, null, 
            _options.AlertEvaluationInterval, _options.AlertEvaluationInterval);
            
        _metricsCollectionTimer = new Timer(CollectMetricsBackground, null,
            _options.MetricsCollectionInterval, _options.MetricsCollectionInterval);
        
        _logger.LogInformation("InvalidationMonitor initialized with {ProviderCount} providers", 
            _cacheProviders.Count());
    }

    #endregion

    #region Health Monitoring

    public async Task<InvalidationSystemHealth> CheckSystemHealthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var providerStatuses = new Dictionary<string, ProviderHealthStatus>();
        var issues = new List<string>();
        
        try
        {
            // Check all providers in parallel
            var healthCheckTasks = _cacheProviders.Select(async provider =>
            {
                try
                {
                    var providerHealth = await CheckProviderHealthAsync(provider.ProviderName, cancellationToken);
                    providerStatuses[provider.ProviderName] = providerHealth;
                    
                    if (!providerHealth.IsHealthy && !string.IsNullOrEmpty(providerHealth.ErrorMessage))
                    {
                        issues.Add($"{provider.ProviderName}: {providerHealth.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    var errorStatus = new ProviderHealthStatus
                    {
                        ProviderName = provider.ProviderName,
                        ProviderType = provider.ProviderType,
                        IsHealthy = false,
                        ErrorMessage = ex.Message,
                        CheckedAt = DateTime.UtcNow
                    };
                    
                    providerStatuses[provider.ProviderName] = errorStatus;
                    issues.Add($"{provider.ProviderName}: Health check failed - {ex.Message}");
                }
            });

            await Task.WhenAll(healthCheckTasks);
            
            // Check invalidation engine health
            var engineHealth = await CheckEngineHealthAsync(cancellationToken);
            if (!engineHealth.IsHealthy)
            {
                issues.AddRange(engineHealth.Issues);
            }
            
            var healthyProviders = providerStatuses.Values.Count(p => p.IsHealthy);
            var totalProviders = providerStatuses.Count;
            
            return new InvalidationSystemHealth
            {
                IsHealthy = healthyProviders == totalProviders && engineHealth.IsHealthy,
                CheckedAt = DateTime.UtcNow,
                ResponseTime = stopwatch.Elapsed,
                TotalProviders = totalProviders,
                HealthyProviders = healthyProviders,
                UnhealthyProviders = totalProviders - healthyProviders,
                ProviderStatuses = providerStatuses,
                EngineHealth = engineHealth,
                Issues = issues
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during system health check");
            
            return new InvalidationSystemHealth
            {
                IsHealthy = false,
                CheckedAt = DateTime.UtcNow,
                ResponseTime = stopwatch.Elapsed,
                Issues = new List<string> { $"Health check failed: {ex.Message}" }
            };
        }
    }

    public async Task<ProviderHealthStatus> CheckProviderHealthAsync(string providerName, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var provider = _cacheProviders.FirstOrDefault(p => p.ProviderName == providerName);
            if (provider == null)
            {
                return new ProviderHealthStatus
                {
                    ProviderName = providerName,
                    IsHealthy = false,
                    ErrorMessage = "Provider not found",
                    CheckedAt = DateTime.UtcNow,
                    ResponseTime = stopwatch.Elapsed
                };
            }

            var healthResult = await provider.GetHealthCheckAsync(cancellationToken);
            
            return new ProviderHealthStatus
            {
                ProviderName = providerName,
                ProviderType = provider.ProviderType,
                IsHealthy = healthResult.IsHealthy,
                CheckedAt = DateTime.UtcNow,
                ResponseTime = stopwatch.Elapsed,
                Status = healthResult.Status,
                ErrorMessage = healthResult.Exception?.Message,
                Details = healthResult.Data
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking health for provider {ProviderName}", providerName);
            
            return new ProviderHealthStatus
            {
                ProviderName = providerName,
                IsHealthy = false,
                ErrorMessage = ex.Message,
                CheckedAt = DateTime.UtcNow,
                ResponseTime = stopwatch.Elapsed
            };
        }
    }

    public async Task<InvalidationEngineHealth> CheckEngineHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_invalidationEngine == null)
            {
                return new InvalidationEngineHealth
                {
                    IsHealthy = true, // No engine is fine
                    CheckedAt = DateTime.UtcNow,
                    Issues = new List<string> { "No invalidation engine configured" }
                };
            }

            var status = await _invalidationEngine.GetStatusAsync(cancellationToken);
            
            var issues = new List<string>();
            if (!status.IsHealthy)
            {
                issues.Add("Invalidation engine reports unhealthy status");
            }
            
            if (status.TrackedKeys > _options.MaxTrackedKeysWarning)
            {
                issues.Add($"High number of tracked keys: {status.TrackedKeys:N0}");
            }

            var successRate = status.Metrics.TryGetValue("SuccessRate", out var rate) && rate is double r ? r : 1.0;
            
            return new InvalidationEngineHealth
            {
                IsHealthy = status.IsHealthy && issues.Count == 0,
                CheckedAt = DateTime.UtcNow,
                Uptime = status.Uptime,
                ActiveStrategies = (int)status.Metrics.GetValueOrDefault("ActiveStrategies", 0),
                ActiveRules = status.ActiveRules,
                TrackedKeys = status.TrackedKeys,
                TotalInvalidations = (long)status.Metrics.GetValueOrDefault("TotalInvalidations", 0L),
                FailedInvalidations = (long)status.Metrics.GetValueOrDefault("FailedInvalidations", 0L),
                SuccessRate = successRate,
                Issues = issues
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking invalidation engine health");
            
            return new InvalidationEngineHealth
            {
                IsHealthy = false,
                CheckedAt = DateTime.UtcNow,
                Issues = new List<string> { $"Engine health check failed: {ex.Message}" }
            };
        }
    }

    #endregion

    #region Metrics Collection

    public async Task<InvalidationMetrics> CollectMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var providerMetricsTasks = _cacheProviders.Select(async provider =>
            {
                try
                {
                    var stats = await provider.GetStatisticsAsync(cancellationToken);
                    return new KeyValuePair<string, ProviderMetrics>(provider.ProviderName, new ProviderMetrics
                    {
                        ProviderName = provider.ProviderName,
                        ProviderType = provider.ProviderType,
                        Timestamp = DateTime.UtcNow,
                        Hits = (long)stats.AdditionalMetrics.GetValueOrDefault("hit_count", 0L),
                        Misses = (long)stats.AdditionalMetrics.GetValueOrDefault("miss_count", 0L),
                        HitRatio = stats.HitRatio,
                        TotalKeys = stats.TotalKeys,
                        MemoryUsage = stats.DatabaseSize,
                        IsConnected = provider.IsAvailable,
                        CustomMetrics = stats.AdditionalMetrics
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to collect metrics for provider {ProviderName}", provider.ProviderName);
                    return new KeyValuePair<string, ProviderMetrics>(provider.ProviderName, new ProviderMetrics
                    {
                        ProviderName = provider.ProviderName,
                        ProviderType = provider.ProviderType,
                        Timestamp = DateTime.UtcNow
                    });
                }
            });

            var providerMetricsResults = await Task.WhenAll(providerMetricsTasks);
            var providerMetrics = providerMetricsResults.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            // Aggregate metrics
            var totalHits = providerMetrics.Values.Sum(m => m.Hits);
            var totalMisses = providerMetrics.Values.Sum(m => m.Misses);
            var totalOperations = totalHits + totalMisses;
            var hitRatio = totalOperations > 0 ? (double)totalHits / totalOperations : 0.0;
            var totalKeys = providerMetrics.Values.Sum(m => m.TotalKeys);
            var totalMemory = providerMetrics.Values.Sum(m => m.MemoryUsage);

            // Get invalidation metrics from recent events
            var recentInvalidationEvents = GetRecentEvents(_invalidationEvents, TimeSpan.FromMinutes(5));
            var totalInvalidations = recentInvalidationEvents.Count;
            var successfulInvalidations = recentInvalidationEvents.Count(e => e.Success);
            var invalidationSuccessRate = totalInvalidations > 0 ? (double)successfulInvalidations / totalInvalidations : 1.0;

            var metrics = new InvalidationMetrics
            {
                Timestamp = DateTime.UtcNow,
                TotalCacheHits = totalHits,
                TotalCacheMisses = totalMisses,
                CacheHitRatio = hitRatio,
                TotalCacheOperations = totalOperations,
                TotalInvalidations = totalInvalidations,
                SuccessfulInvalidations = successfulInvalidations,
                FailedInvalidations = totalInvalidations - successfulInvalidations,
                InvalidationSuccessRate = invalidationSuccessRate,
                TotalKeys = totalKeys,
                TotalMemoryUsage = totalMemory,
                ProviderMetrics = providerMetrics
            };

            _lastMetrics = metrics;
            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting metrics");
            return _lastMetrics; // Return last known metrics
        }
    }

    public async Task<Dictionary<string, ProviderMetrics>> CollectProviderMetricsAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, ProviderMetrics>();
        
        var tasks = _cacheProviders.Select(async provider =>
        {
            try
            {
                var stats = await provider.GetStatisticsAsync(cancellationToken);
                var metrics = new ProviderMetrics
                {
                    ProviderName = provider.ProviderName,
                    ProviderType = provider.ProviderType,
                    Timestamp = DateTime.UtcNow,
                    Hits = (long)stats.AdditionalMetrics.GetValueOrDefault("hit_count", 0L),
                    Misses = (long)stats.AdditionalMetrics.GetValueOrDefault("miss_count", 0L),
                    HitRatio = stats.HitRatio,
                    TotalKeys = stats.TotalKeys,
                    MemoryUsage = stats.DatabaseSize,
                    IsConnected = provider.IsAvailable,
                    CustomMetrics = stats.AdditionalMetrics
                };

                lock (result)
                {
                    result[provider.ProviderName] = metrics;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to collect detailed metrics for provider {ProviderName}", provider.ProviderName);
            }
        });

        await Task.WhenAll(tasks);
        return result;
    }

    public async Task<IEnumerable<InvalidationMetrics>> GetMetricsHistoryAsync(DateTime startTime, DateTime endTime, TimeSpan interval, CancellationToken cancellationToken = default)
    {
        // This is a simplified implementation. In a real scenario, you'd store metrics in a time-series database
        var history = new List<InvalidationMetrics>();
        
        try
        {
            // For now, we'll simulate historical data based on current metrics
            var current = await CollectMetricsAsync(cancellationToken);
            var timePoints = new List<DateTime>();
            
            for (var time = startTime; time <= endTime; time += interval)
            {
                timePoints.Add(time);
            }

            // Generate simulated historical metrics
            foreach (var timePoint in timePoints)
            {
                var historicalMetrics = new InvalidationMetrics
                {
                    Timestamp = timePoint,
                    TotalCacheHits = (long)(current.TotalCacheHits * (0.8 + new Random().NextDouble() * 0.4)),
                    TotalCacheMisses = (long)(current.TotalCacheMisses * (0.8 + new Random().NextDouble() * 0.4)),
                    TotalInvalidations = (long)(current.TotalInvalidations * (0.8 + new Random().NextDouble() * 0.4)),
                    TotalKeys = current.TotalKeys,
                    TotalMemoryUsage = current.TotalMemoryUsage
                };
                
                historicalMetrics = historicalMetrics with 
                { 
                    CacheHitRatio = historicalMetrics.TotalCacheHits + historicalMetrics.TotalCacheMisses > 0 
                        ? (double)historicalMetrics.TotalCacheHits / (historicalMetrics.TotalCacheHits + historicalMetrics.TotalCacheMisses) 
                        : 0.0,
                    InvalidationSuccessRate = historicalMetrics.TotalInvalidations > 0
                        ? (double)historicalMetrics.SuccessfulInvalidations / historicalMetrics.TotalInvalidations
                        : 1.0
                };
                
                history.Add(historicalMetrics);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting metrics history from {StartTime} to {EndTime}", startTime, endTime);
        }

        return history;
    }

    #endregion

    #region Performance Monitoring

    public async Task<InvalidationPerformanceStats> GetPerformanceStatsAsync(TimeSpan period, CancellationToken cancellationToken = default)
    {
        try
        {
            var endTime = DateTime.UtcNow;
            var startTime = endTime - period;
            
            var recentInvalidationEvents = GetRecentEvents(_invalidationEvents, period);
            var recentCacheEvents = GetRecentEvents(_cacheEvents, period);
            
            var totalInvalidations = recentInvalidationEvents.Count;
            var invalidationsPerSecond = totalInvalidations > 0 ? totalInvalidations / period.TotalSeconds : 0.0;
            
            var latencies = recentInvalidationEvents.Select(e => e.Duration).OrderBy(d => d).ToList();
            var averageLatency = latencies.Any() ? TimeSpan.FromTicks((long)latencies.Average(d => d.Ticks)) : TimeSpan.Zero;
            var medianLatency = latencies.Any() ? latencies[latencies.Count / 2] : TimeSpan.Zero;
            var p95Index = (int)(latencies.Count * 0.95);
            var p95Latency = latencies.Any() && p95Index < latencies.Count ? latencies[p95Index] : TimeSpan.Zero;
            var p99Index = (int)(latencies.Count * 0.99);
            var p99Latency = latencies.Any() && p99Index < latencies.Count ? latencies[p99Index] : TimeSpan.Zero;
            var maxLatency = latencies.Any() ? latencies.Last() : TimeSpan.Zero;
            
            var errors = recentInvalidationEvents.Where(e => !e.Success).ToList();
            var errorRate = totalInvalidations > 0 ? (double)errors.Count / totalInvalidations : 0.0;
            
            return new InvalidationPerformanceStats
            {
                Period = period,
                StartTime = startTime,
                EndTime = endTime,
                TotalInvalidations = totalInvalidations,
                InvalidationsPerSecond = invalidationsPerSecond,
                PeakInvalidationsPerSecond = invalidationsPerSecond * 1.5, // Simplified
                AverageLatency = averageLatency,
                MedianLatency = medianLatency,
                P95Latency = p95Latency,
                P99Latency = p99Latency,
                MaxLatency = maxLatency,
                TotalErrors = errors.Count,
                ErrorRate = errorRate,
                ErrorsByType = errors.GroupBy(e => e.ErrorMessage ?? "Unknown")
                    .ToDictionary(g => g.Key, g => (long)g.Count()),
                InvalidationsByType = recentInvalidationEvents.GroupBy(e => e.Type.ToString())
                    .ToDictionary(g => g.Key, g => (long)g.Count()),
                InvalidationsByPattern = recentInvalidationEvents.GroupBy(e => e.Target)
                    .ToDictionary(g => g.Key, g => (long)g.Count()),
                AverageLatencyByProvider = recentInvalidationEvents.GroupBy(e => e.ProviderName)
                    .ToDictionary(g => g.Key, g => TimeSpan.FromTicks((long)g.Average(e => e.Duration.Ticks)))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating performance stats for period {Period}", period);
            return new InvalidationPerformanceStats { Period = period };
        }
    }

    public async Task<IEnumerable<HotKeyAnalysis>> GetHotKeysAnalysisAsync(TimeSpan period, int topCount = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var recentEvents = GetRecentEvents(_invalidationEvents, period);
            
            var keyGroups = recentEvents
                .GroupBy(e => e.Target)
                .Select(g => new HotKeyAnalysis
                {
                    Key = g.Key,
                    InvalidationCount = g.Count(),
                    InvalidationsPerHour = g.Count() / period.TotalHours,
                    FirstSeen = period - (DateTime.UtcNow - g.Min(e => e.Timestamp)),
                    LastSeen = period - (DateTime.UtcNow - g.Max(e => e.Timestamp)),
                    Pattern = ExtractPattern(g.Key),
                    RelatedKeys = FindRelatedKeys(g.Key, recentEvents.Select(e => e.Target).Distinct()),
                    Metadata = new Dictionary<string, object>
                    {
                        ["total_duration"] = TimeSpan.FromTicks(g.Sum(e => e.Duration.Ticks)),
                        ["average_duration"] = TimeSpan.FromTicks((long)g.Average(e => e.Duration.Ticks)),
                        ["success_rate"] = (double)g.Count(e => e.Success) / g.Count(),
                        ["providers"] = g.Select(e => e.ProviderName).Distinct().ToList()
                    }
                })
                .OrderByDescending(h => h.InvalidationCount)
                .Take(topCount);

            return keyGroups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing hot keys for period {Period}", period);
            return Enumerable.Empty<HotKeyAnalysis>();
        }
    }

    public async Task<InvalidationPatternAnalysis> AnalyzeInvalidationPatternsAsync(TimeSpan period, CancellationToken cancellationToken = default)
    {
        try
        {
            var recentEvents = GetRecentEvents(_invalidationEvents, period);
            
            var patternFrequency = recentEvents
                .GroupBy(e => ExtractPattern(e.Target))
                .ToDictionary(g => g.Key, g => (long)g.Count());
                
            var patternAverageLatency = recentEvents
                .GroupBy(e => ExtractPattern(e.Target))
                .ToDictionary(g => g.Key, g => TimeSpan.FromTicks((long)g.Average(e => e.Duration.Ticks)));
                
            var patternSuccessRate = recentEvents
                .GroupBy(e => ExtractPattern(e.Target))
                .ToDictionary(g => g.Key, g => (double)g.Count(e => e.Success) / g.Count());
            
            var invalidationsByHour = recentEvents
                .GroupBy(e => e.Timestamp.Hour)
                .ToDictionary(g => g.Key, g => (long)g.Count());
                
            var invalidationsByDayOfWeek = recentEvents
                .GroupBy(e => e.Timestamp.DayOfWeek)
                .ToDictionary(g => g.Key, g => (long)g.Count());

            var recommendations = GenerateRecommendations(patternFrequency, patternAverageLatency, patternSuccessRate);

            return new InvalidationPatternAnalysis
            {
                AnalysisPeriod = period,
                GeneratedAt = DateTime.UtcNow,
                PatternFrequency = patternFrequency,
                PatternAverageLatency = patternAverageLatency,
                PatternSuccessRate = patternSuccessRate,
                InvalidationsByHour = invalidationsByHour,
                InvalidationsByDayOfWeek = invalidationsByDayOfWeek,
                CascadePatterns = new Dictionary<string, List<string>>(), // Would need more complex analysis
                CascadeEfficiency = new Dictionary<string, double>(),
                Recommendations = recommendations
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing invalidation patterns for period {Period}", period);
            return new InvalidationPatternAnalysis { AnalysisPeriod = period };
        }
    }

    #endregion

    #region Event Recording

    public Task RecordInvalidationEventAsync(InvalidationEvent invalidationEvent, CancellationToken cancellationToken = default)
    {
        if (invalidationEvent == null) return Task.CompletedTask;
        
        try
        {
            _invalidationEvents.Enqueue(invalidationEvent);
            
            // Keep only recent events to prevent memory issues
            TrimEventQueue(_invalidationEvents, _options.MaxEventHistory);
            
            if (_options.LogEvents)
            {
                _logger.LogDebug("Recorded invalidation event: {EventId} - {Type} {Target} in {Duration}ms",
                    invalidationEvent.EventId, invalidationEvent.Type, invalidationEvent.Target, 
                    invalidationEvent.Duration.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error recording invalidation event {EventId}", invalidationEvent.EventId);
        }
        
        return Task.CompletedTask;
    }

    public Task RecordCacheEventAsync(CacheOperationEvent cacheEvent, CancellationToken cancellationToken = default)
    {
        if (cacheEvent == null) return Task.CompletedTask;
        
        try
        {
            _cacheEvents.Enqueue(cacheEvent);
            
            // Keep only recent events
            TrimEventQueue(_cacheEvents, _options.MaxEventHistory);
            
            if (_options.LogEvents)
            {
                _logger.LogDebug("Recorded cache event: {EventId} - {OperationType} {Key} in {Duration}ms",
                    cacheEvent.EventId, cacheEvent.OperationType, cacheEvent.Key, 
                    cacheEvent.Duration.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error recording cache event {EventId}", cacheEvent.EventId);
        }
        
        return Task.CompletedTask;
    }

    public Task RecordPerformanceEventAsync(PerformanceEvent performanceEvent, CancellationToken cancellationToken = default)
    {
        if (performanceEvent == null) return Task.CompletedTask;
        
        try
        {
            _performanceEvents.Enqueue(performanceEvent);
            
            // Keep only recent events
            TrimEventQueue(_performanceEvents, _options.MaxEventHistory);
            
            if (_options.LogPerformanceEvents)
            {
                _logger.LogDebug("Recorded performance event: {EventId} - {Category}.{MetricName} = {Value} {Unit}",
                    performanceEvent.EventId, performanceEvent.Category, performanceEvent.MetricName, 
                    performanceEvent.Value, performanceEvent.Unit);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error recording performance event {EventId}", performanceEvent.EventId);
        }
        
        return Task.CompletedTask;
    }

    #endregion

    #region Alerting

    public Task ConfigureAlertsAsync(AlertConfiguration alertConfig, CancellationToken cancellationToken = default)
    {
        if (alertConfig == null) throw new ArgumentNullException(nameof(alertConfig));
        
        _alertConfiguration = alertConfig;
        
        _logger.LogInformation("Alert configuration updated with {ThresholdCount} thresholds", 
            alertConfig.Thresholds.Count);
        
        return Task.CompletedTask;
    }

    public Task<IEnumerable<ActiveAlert>> GetActiveAlertsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_activeAlerts.Values.AsEnumerable());
    }

    #endregion

    #region Private Helper Methods

    private void CollectMetricsBackground(object? state)
    {
        if (_disposed) return;
        
        _ = Task.Run(async () =>
        {
            try
            {
                await CollectMetricsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during background metrics collection");
            }
        });
    }

    private void EvaluateAlerts(object? state)
    {
        if (_disposed) return;
        
        _ = Task.Run(async () =>
        {
            try
            {
                var metrics = await CollectMetricsAsync();
                await EvaluateAlertsAsync(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during alert evaluation");
            }
        });
    }

    private async Task EvaluateAlertsAsync(InvalidationMetrics metrics)
    {
        foreach (var threshold in _alertConfiguration.Thresholds.Values)
        {
            try
            {
                var currentValue = GetMetricValue(metrics, threshold.MetricName);
                var shouldAlert = EvaluateThreshold(currentValue, threshold);
                
                var alertKey = $"{threshold.MetricName}_{threshold.ComparisonType}_{threshold.WarningThreshold}";
                var existingAlert = _activeAlerts.GetValueOrDefault(alertKey);
                
                if (shouldAlert && existingAlert == null)
                {
                    // New alert
                    var severity = currentValue >= threshold.CriticalThreshold ? AlertSeverity.Critical : AlertSeverity.Warning;
                    var alert = new ActiveAlert
                    {
                        MetricName = threshold.MetricName,
                        Severity = severity,
                        TriggeredAt = DateTime.UtcNow,
                        Message = $"{threshold.MetricName} is {currentValue:F2}, threshold: {threshold.WarningThreshold:F2}",
                        CurrentValue = currentValue,
                        ThresholdValue = threshold.WarningThreshold,
                        Details = new Dictionary<string, object>
                        {
                            ["comparison_type"] = threshold.ComparisonType.ToString(),
                            ["evaluation_window"] = threshold.EvaluationWindow,
                            ["consecutive_failures"] = threshold.ConsecutiveFailures
                        }
                    };
                    
                    _activeAlerts[alertKey] = alert;
                    
                    AlertTriggered?.Invoke(this, new AlertTriggeredEventArgs { Alert = alert });
                    
                    _logger.LogWarning("Alert triggered: {AlertMessage}", alert.Message);
                }
                else if (!shouldAlert && existingAlert != null)
                {
                    // Clear existing alert
                    _activeAlerts.TryRemove(alertKey, out _);
                    _logger.LogInformation("Alert cleared: {MetricName}", threshold.MetricName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error evaluating alert for metric {MetricName}", threshold.MetricName);
            }
        }
    }

    private double GetMetricValue(InvalidationMetrics metrics, string metricName)
    {
        return metricName switch
        {
            "CacheHitRatio" => metrics.CacheHitRatio,
            "InvalidationSuccessRate" => metrics.InvalidationSuccessRate,
            "TotalKeys" => metrics.TotalKeys,
            "TotalMemoryUsage" => metrics.TotalMemoryUsage,
            "TotalInvalidations" => metrics.TotalInvalidations,
            "FailedInvalidations" => metrics.FailedInvalidations,
            _ => 0.0
        };
    }

    private bool EvaluateThreshold(double currentValue, AlertThreshold threshold)
    {
        return threshold.ComparisonType switch
        {
            AlertComparisonType.GreaterThan => currentValue > threshold.WarningThreshold,
            AlertComparisonType.LessThan => currentValue < threshold.WarningThreshold,
            AlertComparisonType.Equals => Math.Abs(currentValue - threshold.WarningThreshold) < 0.001,
            AlertComparisonType.NotEquals => Math.Abs(currentValue - threshold.WarningThreshold) >= 0.001,
            _ => false
        };
    }

    private List<T> GetRecentEvents<T>(ConcurrentQueue<T> queue, TimeSpan period) where T : class
    {
        var cutoff = DateTime.UtcNow - period;
        var recentEvents = new List<T>();
        
        // This is simplified - in real implementation, you'd use a more efficient time-based storage
        foreach (var item in queue)
        {
            if (GetEventTimestamp(item) >= cutoff)
            {
                recentEvents.Add(item);
            }
        }
        
        return recentEvents;
    }

    private DateTime GetEventTimestamp<T>(T eventItem) where T : class
    {
        return eventItem switch
        {
            InvalidationEvent ie => ie.Timestamp,
            CacheOperationEvent ce => ce.Timestamp,
            PerformanceEvent pe => pe.Timestamp,
            _ => DateTime.MinValue
        };
    }

    private void TrimEventQueue<T>(ConcurrentQueue<T> queue, int maxSize)
    {
        while (queue.Count > maxSize)
        {
            queue.TryDequeue(out _);
        }
    }

    private string ExtractPattern(string target)
    {
        // Simple pattern extraction - replace specific IDs with wildcards
        if (string.IsNullOrEmpty(target)) return target;
        
        // Replace GUIDs and numeric IDs with wildcards
        var pattern = System.Text.RegularExpressions.Regex.Replace(target, @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", "*");
        pattern = System.Text.RegularExpressions.Regex.Replace(pattern, @"\b\d+\b", "*");
        
        return pattern;
    }

    private List<string> FindRelatedKeys(string key, IEnumerable<string> allKeys)
    {
        var pattern = ExtractPattern(key);
        return allKeys.Where(k => k != key && ExtractPattern(k) == pattern).Take(5).ToList();
    }

    private List<string> GenerateRecommendations(
        Dictionary<string, long> patternFrequency,
        Dictionary<string, TimeSpan> patternAverageLatency,
        Dictionary<string, double> patternSuccessRate)
    {
        var recommendations = new List<string>();
        
        // High frequency patterns
        var highFrequencyPatterns = patternFrequency.Where(p => p.Value > 100).OrderByDescending(p => p.Value).Take(3);
        foreach (var pattern in highFrequencyPatterns)
        {
            recommendations.Add($"Consider optimizing high-frequency pattern '{pattern.Key}' ({pattern.Value:N0} invalidations)");
        }
        
        // High latency patterns
        var highLatencyPatterns = patternAverageLatency.Where(p => p.Value.TotalMilliseconds > 1000).OrderByDescending(p => p.Value).Take(3);
        foreach (var pattern in highLatencyPatterns)
        {
            recommendations.Add($"Investigate high-latency pattern '{pattern.Key}' (avg: {pattern.Value.TotalMilliseconds:F0}ms)");
        }
        
        // Low success rate patterns
        var lowSuccessPatterns = patternSuccessRate.Where(p => p.Value < 0.95).OrderBy(p => p.Value).Take(3);
        foreach (var pattern in lowSuccessPatterns)
        {
            recommendations.Add($"Improve reliability for pattern '{pattern.Key}' ({pattern.Value:P1} success rate)");
        }
        
        return recommendations;
    }

    #endregion

    #region IDisposable Implementation

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _alertEvaluationTimer?.Dispose();
            _metricsCollectionTimer?.Dispose();
            _disposed = true;
            
            _logger.LogInformation("InvalidationMonitor disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during InvalidationMonitor disposal");
        }
    }

    #endregion
}

/// <summary>
/// 무효화 모니터 설정 옵션
/// Invalidation monitor configuration options
/// </summary>
public class InvalidationMonitorOptions
{
    /// <summary>알림 평가 간격</summary>
    public TimeSpan AlertEvaluationInterval { get; set; } = TimeSpan.FromMinutes(1);
    
    /// <summary>메트릭 수집 간격</summary>
    public TimeSpan MetricsCollectionInterval { get; set; } = TimeSpan.FromMinutes(1);
    
    /// <summary>최대 이벤트 히스토리 보관 개수</summary>
    public int MaxEventHistory { get; set; } = 10000;
    
    /// <summary>추적 키 경고 임계값</summary>
    public long MaxTrackedKeysWarning { get; set; } = 100000;
    
    /// <summary>이벤트 로깅 활성화</summary>
    public bool LogEvents { get; set; } = false;
    
    /// <summary>성능 이벤트 로깅 활성화</summary>
    public bool LogPerformanceEvents { get; set; } = false;
}