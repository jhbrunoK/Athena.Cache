using Invalidus.Core.Abstractions;
using Invalidus.Monitoring.Abstractions;
using Invalidus.Monitoring.Implementations;

namespace Invalidus.Monitoring.Extensions;

/// <summary>
/// Invalidus 모니터링 시스템 DI 확장 메서드
/// Dependency injection extensions for Invalidus monitoring system
/// </summary>
public static class ServiceCollectionExtensions
{
    #region Basic Monitoring Registration

    /// <summary>
    /// Invalidus 모니터링 시스템을 서비스 컬렉션에 추가
    /// Add Invalidus monitoring system to the service collection
    /// </summary>
    public static IServiceCollection AddInvalidusMonitoring(
        this IServiceCollection services,
        Action<InvalidationMonitorOptions>? configureOptions = null)
    {
        // Configure options
        services.Configure<InvalidationMonitorOptions>(options =>
        {
            configureOptions?.Invoke(options);
        });

        // Register monitoring services
        services.AddSingleton<IInvalidationMonitor, InvalidationMonitor>();
        
        return services;
    }

    /// <summary>
    /// 구성 파일에서 모니터링 설정을 읽어서 추가
    /// Add monitoring with configuration from settings
    /// </summary>
    public static IServiceCollection AddInvalidusMonitoringFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Invalidus:Monitoring")
    {
        services.Configure<InvalidationMonitorOptions>(
            configuration.GetSection(sectionName));

        services.AddSingleton<IInvalidationMonitor, InvalidationMonitor>();
        
        return services;
    }

    #endregion

    #region Environment-Specific Configurations

    /// <summary>
    /// 개발 환경용 모니터링 설정
    /// Development environment monitoring configuration
    /// </summary>
    public static IServiceCollection AddInvalidusMonitoringDevelopment(
        this IServiceCollection services)
    {
        return services.AddInvalidusMonitoring(options =>
        {
            options.AlertEvaluationInterval = TimeSpan.FromSeconds(30);
            options.MetricsCollectionInterval = TimeSpan.FromSeconds(30);
            options.MaxEventHistory = 1000;
            options.LogEvents = true;
            options.LogPerformanceEvents = true;
        });
    }

    /// <summary>
    /// 프로덕션 환경용 모니터링 설정
    /// Production environment monitoring configuration
    /// </summary>
    public static IServiceCollection AddInvalidusMonitoringProduction(
        this IServiceCollection services)
    {
        return services.AddInvalidusMonitoring(options =>
        {
            options.AlertEvaluationInterval = TimeSpan.FromMinutes(1);
            options.MetricsCollectionInterval = TimeSpan.FromMinutes(1);
            options.MaxEventHistory = 10000;
            options.MaxTrackedKeysWarning = 1000000;
            options.LogEvents = false;
            options.LogPerformanceEvents = false;
        });
    }

    /// <summary>
    /// 고성능 환경용 모니터링 설정 (최소한의 오버헤드)
    /// High-performance environment monitoring (minimal overhead)
    /// </summary>
    public static IServiceCollection AddInvalidusMonitoringHighPerformance(
        this IServiceCollection services)
    {
        return services.AddInvalidusMonitoring(options =>
        {
            options.AlertEvaluationInterval = TimeSpan.FromMinutes(5);
            options.MetricsCollectionInterval = TimeSpan.FromMinutes(2);
            options.MaxEventHistory = 1000;
            options.LogEvents = false;
            options.LogPerformanceEvents = false;
        });
    }

    #endregion

    #region Health Checks Integration

    /// <summary>
    /// 모니터링 시스템과 함께 헬스체크 추가
    /// Add monitoring system with health checks
    /// </summary>
    public static IServiceCollection AddInvalidusMonitoringWithHealthChecks(
        this IServiceCollection services,
        Action<InvalidationMonitorOptions>? configureOptions = null)
    {
        // Add monitoring
        services.AddInvalidusMonitoring(configureOptions);

        // Add health checks
        services.AddHealthChecks()
            .AddCheck<InvalidationSystemHealthCheck>("invalidus_system")
            .AddCheck<InvalidationEngineHealthCheck>("invalidus_engine");

        return services;
    }

    #endregion

    #region Advanced Configuration

    /// <summary>
    /// 사전 정의된 알림 설정과 함께 모니터링 시스템 추가
    /// Add monitoring system with predefined alert configuration
    /// </summary>
    public static IServiceCollection AddInvalidusMonitoringWithAlerts(
        this IServiceCollection services,
        Action<InvalidationMonitorOptions>? configureOptions = null,
        Action<AlertConfiguration>? configureAlerts = null)
    {
        services.AddInvalidusMonitoring(configureOptions);

        // Configure alerts
        if (configureAlerts != null)
        {
            services.AddSingleton<AlertConfiguration>(serviceProvider =>
            {
                var alertConfig = new AlertConfiguration();
                configureAlerts(alertConfig);
                return alertConfig;
            });

            // Auto-configure alerts when monitor is created
            services.Configure<InvalidationMonitorOptions>(options =>
            {
                // The monitor will pick up the AlertConfiguration from DI
            });
        }

        return services;
    }

    /// <summary>
    /// 기본 알림 임계값과 함께 모니터링 시스템 추가
    /// Add monitoring system with default alert thresholds
    /// </summary>
    public static IServiceCollection AddInvalidusMonitoringWithDefaultAlerts(
        this IServiceCollection services,
        Action<InvalidationMonitorOptions>? configureOptions = null)
    {
        services.AddInvalidusMonitoring(configureOptions);

        // Configure alerts with proper initialization
        services.AddSingleton<AlertConfiguration>(serviceProvider =>
        {
            var thresholds = new Dictionary<string, AlertThreshold>
            {
                ["CacheHitRatio"] = new AlertThreshold
                {
                    MetricName = "CacheHitRatio",
                    WarningThreshold = 0.80, // 80%
                    CriticalThreshold = 0.70, // 70%
                    ComparisonType = AlertComparisonType.LessThan,
                    EvaluationWindow = TimeSpan.FromMinutes(5)
                },
                ["InvalidationSuccessRate"] = new AlertThreshold
                {
                    MetricName = "InvalidationSuccessRate",
                    WarningThreshold = 0.95, // 95%
                    CriticalThreshold = 0.90, // 90%
                    ComparisonType = AlertComparisonType.LessThan,
                    EvaluationWindow = TimeSpan.FromMinutes(5)
                },
                ["TotalMemoryUsage"] = new AlertThreshold
                {
                    MetricName = "TotalMemoryUsage",
                    WarningThreshold = 1024L * 1024 * 1024, // 1GB
                    CriticalThreshold = 2048L * 1024 * 1024, // 2GB
                    ComparisonType = AlertComparisonType.GreaterThan,
                    EvaluationWindow = TimeSpan.FromMinutes(10)
                },
                ["FailedInvalidations"] = new AlertThreshold
                {
                    MetricName = "FailedInvalidations",
                    WarningThreshold = 10,
                    CriticalThreshold = 50,
                    ComparisonType = AlertComparisonType.GreaterThan,
                    EvaluationWindow = TimeSpan.FromMinutes(5)
                }
            };

            return new AlertConfiguration
            {
                Thresholds = thresholds,
                EvaluationInterval = TimeSpan.FromMinutes(1)
            };
        });
        
        return services;
    }

    #endregion

    #region Event Handling

    /// <summary>
    /// 모니터링 이벤트 핸들러 등록
    /// Register monitoring event handlers
    /// </summary>
    public static IServiceCollection AddInvalidusMonitoringEventHandlers(
        this IServiceCollection services,
        Action<InvalidationMonitorOptions>? configureOptions = null)
    {
        services.AddInvalidusMonitoring(configureOptions);

        // Add event handlers as hosted services
        services.AddHostedService<MonitoringEventHandler>();

        return services;
    }

    #endregion

    #region Metrics Export

    /// <summary>
    /// Prometheus 메트릭 익스포트와 함께 모니터링 추가
    /// Add monitoring with Prometheus metrics export
    /// </summary>
    public static IServiceCollection AddInvalidusMonitoringWithPrometheus(
        this IServiceCollection services,
        Action<InvalidationMonitorOptions>? configureOptions = null)
    {
        services.AddInvalidusMonitoring(configureOptions);

        // Add Prometheus support (would need additional package)
        // services.AddSingleton<IMetricsExporter, PrometheusMetricsExporter>();

        return services;
    }

    /// <summary>
    /// Application Insights와 함께 모니터링 추가
    /// Add monitoring with Application Insights integration
    /// </summary>
    public static IServiceCollection AddInvalidusMonitoringWithApplicationInsights(
        this IServiceCollection services,
        Action<InvalidationMonitorOptions>? configureOptions = null)
    {
        services.AddInvalidusMonitoring(configureOptions);

        // Add Application Insights integration
        // services.AddSingleton<IMetricsExporter, ApplicationInsightsMetricsExporter>();

        return services;
    }

    #endregion
}

/// <summary>
/// 시스템 전체 헬스체크
/// Overall system health check
/// </summary>
public class InvalidationSystemHealthCheck : IHealthCheck
{
    private readonly IInvalidationMonitor _monitor;
    private readonly ILogger<InvalidationSystemHealthCheck> _logger;

    public InvalidationSystemHealthCheck(
        IInvalidationMonitor monitor,
        ILogger<InvalidationSystemHealthCheck> logger)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var systemHealth = await _monitor.CheckSystemHealthAsync(cancellationToken);

            if (systemHealth.IsHealthy)
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                    $"System is healthy ({systemHealth.HealthyProviders}/{systemHealth.TotalProviders} providers healthy)",
                    new Dictionary<string, object>
                    {
                        ["total_providers"] = systemHealth.TotalProviders,
                        ["healthy_providers"] = systemHealth.HealthyProviders,
                        ["response_time_ms"] = systemHealth.ResponseTime.TotalMilliseconds
                    });
            }
            else
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded(
                    $"System partially healthy ({systemHealth.HealthyProviders}/{systemHealth.TotalProviders} providers healthy)",
                    null,
                    new Dictionary<string, object>
                    {
                        ["total_providers"] = systemHealth.TotalProviders,
                        ["healthy_providers"] = systemHealth.HealthyProviders,
                        ["unhealthy_providers"] = systemHealth.UnhealthyProviders,
                        ["issues"] = systemHealth.Issues,
                        ["response_time_ms"] = systemHealth.ResponseTime.TotalMilliseconds
                    });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System health check failed");
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("System health check failed", ex);
        }
    }
}

/// <summary>
/// 무효화 엔진 헬스체크
/// Invalidation engine health check
/// </summary>
public class InvalidationEngineHealthCheck : IHealthCheck
{
    private readonly IInvalidationMonitor _monitor;
    private readonly ILogger<InvalidationEngineHealthCheck> _logger;

    public InvalidationEngineHealthCheck(
        IInvalidationMonitor monitor,
        ILogger<InvalidationEngineHealthCheck> logger)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var engineHealth = await _monitor.CheckEngineHealthAsync(cancellationToken);

            if (engineHealth.IsHealthy)
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                    "Invalidation engine is healthy",
                    new Dictionary<string, object>
                    {
                        ["uptime_hours"] = engineHealth.Uptime.TotalHours,
                        ["active_strategies"] = engineHealth.ActiveStrategies,
                        ["active_rules"] = engineHealth.ActiveRules,
                        ["tracked_keys"] = engineHealth.TrackedKeys,
                        ["success_rate"] = engineHealth.SuccessRate
                    });
            }
            else
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded(
                    "Invalidation engine has issues",
                    null,
                    new Dictionary<string, object>
                    {
                        ["issues"] = engineHealth.Issues,
                        ["uptime_hours"] = engineHealth.Uptime.TotalHours,
                        ["success_rate"] = engineHealth.SuccessRate
                    });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Engine health check failed");
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Engine health check failed", ex);
        }
    }
}

/// <summary>
/// 모니터링 이벤트 처리 백그라운드 서비스
/// Background service for handling monitoring events
/// </summary>
public class MonitoringEventHandler : BackgroundService
{
    private readonly IInvalidationMonitor _monitor;
    private readonly ILogger<MonitoringEventHandler> _logger;

    public MonitoringEventHandler(
        IInvalidationMonitor monitor,
        ILogger<MonitoringEventHandler> logger)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribe to monitoring events
        _monitor.AlertTriggered += OnAlertTriggered;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Perform periodic monitoring tasks
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                
                // Could add periodic maintenance tasks here
                await PerformMaintenanceTasks(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in monitoring event handler");
        }
        finally
        {
            _monitor.AlertTriggered -= OnAlertTriggered;
        }
    }

    private void OnAlertTriggered(object? sender, AlertTriggeredEventArgs e)
    {
        var alert = e.Alert;
        
        var logLevel = alert.Severity switch
        {
            AlertSeverity.Critical => LogLevel.Critical,
            AlertSeverity.Warning => LogLevel.Warning,
            _ => LogLevel.Information
        };

        _logger.Log(logLevel, "Alert triggered: {MetricName} = {CurrentValue} (threshold: {ThresholdValue}). {Message}",
            alert.MetricName, alert.CurrentValue, alert.ThresholdValue, alert.Message);

        // Could add notification logic here (email, Slack, etc.)
    }

    private async Task PerformMaintenanceTasks(CancellationToken cancellationToken)
    {
        try
        {
            // Example maintenance tasks
            var activeAlerts = await _monitor.GetActiveAlertsAsync(cancellationToken);
            
            if (activeAlerts.Any())
            {
                _logger.LogInformation("Active alerts: {AlertCount}", activeAlerts.Count());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during maintenance tasks");
        }
    }
}