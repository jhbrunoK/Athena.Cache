using Athena.Invalidation.Monitoring.Abstractions;
using Athena.Invalidation.Monitoring.Core;
using Athena.Invalidation.Monitoring.HealthChecks;
using Athena.Invalidation.Monitoring.Prometheus;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Metrics;

namespace Athena.Invalidation.Monitoring.Extensions;

/// <summary>
/// 서비스 컬렉션 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 무효화 모니터링 서비스 추가
    /// </summary>
    public static IServiceCollection AddInvalidationMonitoring(
        this IServiceCollection services,
        Action<MonitoringOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // 핵심 모니터링 서비스
        services.TryAddSingleton<IInvalidationMetricsCollector, DefaultInvalidationMetricsCollector>();
        services.TryAddSingleton<IInvalidationHealthChecker, InvalidationEngineHealthCheck>();
        
        // 헬스체크 등록
        services.AddHealthChecks()
            .AddTypeActivatedCheck<InvalidationEngineHealthCheck>(
                "invalidation_engine",
                Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: new[] { "invalidation", "engine" });

        return services;
    }

    /// <summary>
    /// OpenTelemetry 메트릭 추가
    /// </summary>
    public static IServiceCollection AddInvalidationOpenTelemetry(
        this IServiceCollection services,
        Action<MeterProviderBuilder>? configureMeter = null)
    {
        services.AddOpenTelemetry()
            .WithMetrics(builder =>
            {
                builder
                    .AddMeter("Athena.Invalidation")
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation();
                    
                configureMeter?.Invoke(builder);
            });

        return services;
    }

    /// <summary>
    /// Prometheus 메트릭 익스포터 추가
    /// </summary>
    public static IServiceCollection AddInvalidationPrometheus(
        this IServiceCollection services,
        Action<PrometheusOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.TryAddSingleton<PrometheusMetricsExporter>();
        
        // Hosted Service로 등록하여 자동 시작/중지
        services.AddHostedService<PrometheusHostedService>();

        return services;
    }

    /// <summary>
    /// 전체 모니터링 스택 추가 (올인원)
    /// </summary>
    public static IServiceCollection AddInvalidationFullMonitoring(
        this IServiceCollection services,
        Action<MonitoringOptions>? configureMonitoring = null,
        Action<PrometheusOptions>? configurePrometheus = null,
        Action<MeterProviderBuilder>? configureOpenTelemetry = null)
    {
        return services
            .AddInvalidationMonitoring(configureMonitoring)
            .AddInvalidationOpenTelemetry(configureOpenTelemetry)
            .AddInvalidationPrometheus(configurePrometheus);
    }
}

/// <summary>
/// Prometheus 호스티드 서비스
/// </summary>
internal class PrometheusHostedService : BackgroundService
{
    private readonly PrometheusMetricsExporter _exporter;
    private readonly ILogger<PrometheusHostedService> _logger;

    public PrometheusHostedService(
        PrometheusMetricsExporter exporter,
        ILogger<PrometheusHostedService> logger)
    {
        _exporter = exporter;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Prometheus metrics exporter");
        await _exporter.StartAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Prometheus metrics exporter");
        await base.StopAsync(cancellationToken);
        await _exporter.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Prometheus 익스포터는 자체적으로 백그라운드에서 동작하므로
        // 여기서는 단순히 취소 토큰을 기다림
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}