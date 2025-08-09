using Athena.Invalidation.Redis.Abstractions;
using Athena.Invalidation.Redis.Engine;
using Athena.Invalidation.Redis.Providers;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Athena.Invalidation.Redis.Extensions;

/// <summary>
/// Redis 무효화 서비스 등록 확장
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Redis 캐시 무효화 서비스를 등록합니다
    /// </summary>
    public static IServiceCollection AddRedisInvalidation(
        this IServiceCollection services,
        string connectionString,
        Action<RedisInvalidationOptions>? configureProvider = null,
        Action<RedisInvalidationEngineOptions>? configureEngine = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Redis connection string is required", nameof(connectionString));
        }

        // Redis 연결 등록
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var configuration = ConfigurationOptions.Parse(connectionString);
            return ConnectionMultiplexer.Connect(configuration);
        });

        // 옵션 등록
        if (configureProvider != null)
        {
            services.Configure(configureProvider);
        }
        else
        {
            services.Configure<RedisInvalidationOptions>(_ => { });
        }

        if (configureEngine != null)
        {
            services.Configure(configureEngine);
        }
        else
        {
            services.Configure<RedisInvalidationEngineOptions>(_ => { });
        }

        // Redis 무효화 서비스 등록
        services.AddSingleton<IRedisInvalidationProvider, RedisInvalidationProvider>();
        services.AddSingleton<RedisInvalidationEngine>();
        services.AddSingleton<IInvalidationEngine>(sp => sp.GetRequiredService<RedisInvalidationEngine>());

        return services;
    }

    /// <summary>
    /// Redis 캐시 무효화 서비스를 등록합니다 (기존 ConnectionMultiplexer 사용)
    /// </summary>
    public static IServiceCollection AddRedisInvalidation(
        this IServiceCollection services,
        Action<RedisInvalidationOptions>? configureProvider = null,
        Action<RedisInvalidationEngineOptions>? configureEngine = null)
    {
        // 옵션 등록
        if (configureProvider != null)
        {
            services.Configure(configureProvider);
        }
        else
        {
            services.Configure<RedisInvalidationOptions>(_ => { });
        }

        if (configureEngine != null)
        {
            services.Configure(configureEngine);
        }
        else
        {
            services.Configure<RedisInvalidationEngineOptions>(_ => { });
        }

        // Redis 무효화 서비스 등록 (ConnectionMultiplexer는 이미 등록되어 있다고 가정)
        services.AddSingleton<IRedisInvalidationProvider, RedisInvalidationProvider>();
        services.AddSingleton<RedisInvalidationEngine>();
        services.AddSingleton<IInvalidationEngine>(sp => sp.GetRequiredService<RedisInvalidationEngine>());

        return services;
    }

    /// <summary>
    /// Redis 연결 설정 생성
    /// </summary>
    public static ConfigurationOptions CreateRedisConfiguration(
        string connectionString,
        int connectTimeout = 30000,
        int syncTimeout = 10000,
        int asyncTimeout = 10000,
        bool abortOnConnectFail = false)
    {
        var config = ConfigurationOptions.Parse(connectionString);
        config.ConnectTimeout = connectTimeout;
        config.SyncTimeout = syncTimeout;
        config.AsyncTimeout = asyncTimeout;
        config.AbortOnConnectFail = abortOnConnectFail;
        config.ConnectRetry = 3;
        config.ReconnectRetryPolicy = new ExponentialRetry(1000, 10000);

        return config;
    }

    /// <summary>
    /// Redis 무효화 헬스체크 추가
    /// </summary>
    public static IServiceCollection AddRedisInvalidationHealthChecks(
        this IServiceCollection services,
        string? healthCheckName = null,
        TimeSpan? timeout = null)
    {
        services.AddHealthChecks()
            .AddCheck<RedisInvalidationHealthCheck>(
                healthCheckName ?? "redis_invalidation",
                timeout: timeout ?? TimeSpan.FromSeconds(10));

        return services;
    }
}

/// <summary>
/// Redis 무효화 헬스체크
/// </summary>
public class RedisInvalidationHealthCheck : IHealthCheck
{
    private readonly IRedisInvalidationProvider _redisProvider;
    private readonly ILogger<RedisInvalidationHealthCheck> _logger;

    public RedisInvalidationHealthCheck(
        IRedisInvalidationProvider redisProvider,
        ILogger<RedisInvalidationHealthCheck> logger)
    {
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await _redisProvider.IsHealthyAsync(cancellationToken);
            
            if (isHealthy)
            {
                var stats = await _redisProvider.GetStatisticsAsync(cancellationToken);
                var data = new Dictionary<string, object>
                {
                    ["provider_name"] = _redisProvider.Name,
                    ["is_connected"] = _redisProvider.IsConnected,
                    ["database"] = _redisProvider.Database,
                    ["total_keys"] = stats.TotalKeys,
                    ["uptime"] = stats.Uptime.TotalSeconds
                };

                return HealthCheckResult.Healthy("Redis invalidation provider is healthy", data);
            }
            else
            {
                return HealthCheckResult.Unhealthy("Redis invalidation provider is not healthy");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis invalidation health check failed");
            return HealthCheckResult.Unhealthy("Redis invalidation health check failed", ex);
        }
    }
}