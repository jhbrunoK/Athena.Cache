using Invalidus.Core.Abstractions;
using Invalidus.Redis.Configuration;
using Invalidus.Redis.Providers;
using StackExchange.Redis;

namespace Invalidus.Redis.Extensions;

/// <summary>
/// Invalidus Redis Provider DI 확장 메서드
/// Dependency injection extensions for Invalidus Redis Provider
/// </summary>
public static class ServiceCollectionExtensions
{
    #region Basic Redis Registration

    /// <summary>
    /// Universal Redis Provider를 서비스 컬렉션에 추가
    /// Add Universal Redis Provider to the service collection
    /// </summary>
    public static IServiceCollection AddInvalidusRedis(
        this IServiceCollection services,
        string connectionString,
        Action<RedisProviderOptions>? configureOptions = null)
    {
        if (string.IsNullOrEmpty(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));

        // Configure options
        services.Configure<RedisProviderOptions>(options =>
        {
            configureOptions?.Invoke(options);
        });

        // Register Redis connection
        services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
        {
            var configuration = ConfigurationOptions.Parse(connectionString);
            
            var options = serviceProvider.GetService<IOptions<RedisProviderOptions>>()?.Value ?? new RedisProviderOptions();
            
            // Apply timeouts from options
            configuration.ConnectTimeout = (int)options.ConnectTimeout.TotalMilliseconds;
            configuration.SyncTimeout = (int)options.CommandTimeout.TotalMilliseconds;
            configuration.AsyncTimeout = (int)options.CommandTimeout.TotalMilliseconds;
            
            return ConnectionMultiplexer.Connect(configuration);
        });

        // Register the provider
        services.AddSingleton<ICacheProvider, UniversalRedisProvider>();
        
        return services;
    }

    /// <summary>
    /// 기존 Redis ConnectionMultiplexer를 사용하여 Universal Redis Provider 추가
    /// Add Universal Redis Provider using existing ConnectionMultiplexer
    /// </summary>
    public static IServiceCollection AddInvalidusRedis(
        this IServiceCollection services,
        IConnectionMultiplexer connectionMultiplexer,
        Action<RedisProviderOptions>? configureOptions = null)
    {
        if (connectionMultiplexer == null)
            throw new ArgumentNullException(nameof(connectionMultiplexer));

        // Configure options
        services.Configure<RedisProviderOptions>(options =>
        {
            configureOptions?.Invoke(options);
        });

        // Register existing connection
        services.AddSingleton(connectionMultiplexer);

        // Register the provider
        services.AddSingleton<ICacheProvider, UniversalRedisProvider>();
        
        return services;
    }

    #endregion

    #region Environment-Specific Configurations

    /// <summary>
    /// 개발 환경용 Redis Provider 추가
    /// Add Redis Provider for development environment
    /// </summary>
    public static IServiceCollection AddInvalidusRedisDevelopment(
        this IServiceCollection services,
        string connectionString,
        Action<RedisProviderOptions>? configureOptions = null)
    {
        return services.AddInvalidusRedis(connectionString, options =>
        {
            // Apply development defaults
            var devOptions = RedisProviderDefaults.Development;
            CopyOptions(devOptions, options);
            
            // Apply user customizations
            configureOptions?.Invoke(options);
        });
    }

    /// <summary>
    /// 프로덕션 환경용 Redis Provider 추가
    /// Add Redis Provider for production environment
    /// </summary>
    public static IServiceCollection AddInvalidusRedisProduction(
        this IServiceCollection services,
        string connectionString,
        Action<RedisProviderOptions>? configureOptions = null)
    {
        return services.AddInvalidusRedis(connectionString, options =>
        {
            // Apply production defaults
            var prodOptions = RedisProviderDefaults.Production;
            CopyOptions(prodOptions, options);
            
            // Apply user customizations
            configureOptions?.Invoke(options);
        });
    }

    /// <summary>
    /// 고성능 환경용 Redis Provider 추가
    /// Add Redis Provider for high-performance scenarios
    /// </summary>
    public static IServiceCollection AddInvalidusRedisHighPerformance(
        this IServiceCollection services,
        string connectionString,
        Action<RedisProviderOptions>? configureOptions = null)
    {
        return services.AddInvalidusRedis(connectionString, options =>
        {
            // Apply high-performance defaults
            var perfOptions = RedisProviderDefaults.HighPerformance;
            CopyOptions(perfOptions, options);
            
            // Apply user customizations
            configureOptions?.Invoke(options);
        });
    }

    #endregion

    #region Multiple Redis Instances

    /// <summary>
    /// 명명된 Redis Provider 인스턴스 추가 (다중 Redis 인스턴스 지원)
    /// Add named Redis Provider instance for multiple Redis instances
    /// </summary>
    public static IServiceCollection AddInvalidusRedisNamed(
        this IServiceCollection services,
        string name,
        string connectionString,
        Action<RedisProviderOptions>? configureOptions = null)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));

        // Configure named options
        services.Configure<RedisProviderOptions>(name, options =>
        {
            options.ProviderName = name;
            configureOptions?.Invoke(options);
        });

        // Register named connection
        services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
        {
            var configuration = ConfigurationOptions.Parse(connectionString);
            configuration.ClientName = name;
            
            return ConnectionMultiplexer.Connect(configuration);
        });

        // Register as named service (requires manual resolution)
        services.AddSingleton<Func<string, ICacheProvider>>(serviceProvider => 
            providerName =>
            {
                if (providerName == name)
                {
                    var connection = serviceProvider.GetRequiredService<IConnectionMultiplexer>();
                    var logger = serviceProvider.GetRequiredService<ILogger<UniversalRedisProvider>>();
                    var options = serviceProvider.GetRequiredService<IOptionsSnapshot<RedisProviderOptions>>()
                        .Get(name);
                    
                    return new UniversalRedisProvider(connection, logger, 
                        Microsoft.Extensions.Options.Options.Create(options));
                }
                
                throw new ArgumentException($"Unknown provider name: {providerName}");
            });
        
        return services;
    }

    #endregion

    #region Advanced Configuration

    /// <summary>
    /// Redis Provider 설정을 구성 파일에서 읽어서 추가
    /// Add Redis Provider with configuration from settings
    /// </summary>
    public static IServiceCollection AddInvalidusRedisFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Invalidus:Redis")
    {
        // Bind configuration
        var connectionString = configuration.GetConnectionString("Redis") 
            ?? configuration[$"{sectionName}:ConnectionString"];
        
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException($"Redis connection string not found in configuration section '{sectionName}' or 'ConnectionStrings:Redis'");

        services.Configure<RedisProviderOptions>(
            configuration.GetSection(sectionName));

        // Register Redis connection with configuration
        services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
        {
            var config = ConfigurationOptions.Parse(connectionString);
            var options = serviceProvider.GetRequiredService<IOptions<RedisProviderOptions>>().Value;
            
            // Apply configuration
            config.ConnectTimeout = (int)options.ConnectTimeout.TotalMilliseconds;
            config.SyncTimeout = (int)options.CommandTimeout.TotalMilliseconds;
            config.AsyncTimeout = (int)options.CommandTimeout.TotalMilliseconds;
            
            return ConnectionMultiplexer.Connect(config);
        });

        // Register the provider
        services.AddSingleton<ICacheProvider, UniversalRedisProvider>();
        
        return services;
    }

    /// <summary>
    /// Redis Provider와 함께 헬스체크 추가
    /// Add Redis Provider with health checks
    /// </summary>
    public static IServiceCollection AddInvalidusRedisWithHealthChecks(
        this IServiceCollection services,
        string connectionString,
        Action<RedisProviderOptions>? configureOptions = null)
    {
        // Add Redis provider
        services.AddInvalidusRedis(connectionString, configureOptions);

        // Add health checks
        services.AddHealthChecks()
            .AddCheck<RedisHealthCheck>("invalidus_redis");
        
        return services;
    }

    #endregion

    #region Helper Methods

    private static void CopyOptions(RedisProviderOptions source, RedisProviderOptions target)
    {
        target.ProviderName = source.ProviderName;
        target.Database = source.Database;
        target.KeyPrefix = source.KeyPrefix;
        target.DefaultExpiration = source.DefaultExpiration;
        target.ScanPageSize = source.ScanPageSize;
        target.MaxScanResults = source.MaxScanResults;
        target.UseTransaction = source.UseTransaction;
        target.BatchSize = source.BatchSize;
        target.HealthCheckKeyPrefix = source.HealthCheckKeyPrefix;
        target.HealthCheckTimeout = source.HealthCheckTimeout;
        target.ConnectTimeout = source.ConnectTimeout;
        target.CommandTimeout = source.CommandTimeout;
        target.BatchDelay = source.BatchDelay;
        target.UsePipeline = source.UsePipeline;
        
        // Copy logging options
        target.Logging.LogCacheOperations = source.Logging.LogCacheOperations;
        target.Logging.LogInvalidation = source.Logging.LogInvalidation;
        target.Logging.LogPerformanceMetrics = source.Logging.LogPerformanceMetrics;
        target.Logging.LogConnectionEvents = source.Logging.LogConnectionEvents;
        target.Logging.LogErrors = source.Logging.LogErrors;
        target.Logging.LogDebugInfo = source.Logging.LogDebugInfo;
        
        // Copy failure handling options
        target.FailureHandling.ThrowOnRedisError = source.FailureHandling.ThrowOnRedisError;
        target.FailureHandling.ThrowOnJsonError = source.FailureHandling.ThrowOnJsonError;
        target.FailureHandling.MaxRetryAttempts = source.FailureHandling.MaxRetryAttempts;
        target.FailureHandling.RetryDelay = source.FailureHandling.RetryDelay;
        target.FailureHandling.EnableCircuitBreaker = source.FailureHandling.EnableCircuitBreaker;
        target.FailureHandling.CircuitBreakerFailureThreshold = source.FailureHandling.CircuitBreakerFailureThreshold;
        target.FailureHandling.CircuitBreakerRecoveryTime = source.FailureHandling.CircuitBreakerRecoveryTime;
    }

    #endregion
}

/// <summary>
/// Redis 헬스체크 구현
/// Redis health check implementation
/// </summary>
public class RedisHealthCheck : IHealthCheck
{
    private readonly ICacheProvider _cacheProvider;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(ICacheProvider cacheProvider, ILogger<RedisHealthCheck> logger)
    {
        _cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _cacheProvider.GetHealthCheckAsync(cancellationToken);
            
            return result.IsHealthy
                ? HealthCheckResult.Healthy("Redis is healthy", result.Data)
                : HealthCheckResult.Unhealthy(result.Description, result.Exception, result.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check failed");
            return HealthCheckResult.Unhealthy("Redis health check failed", ex);
        }
    }
}