using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Athena.Invalidation.Engine.Core;
using Athena.Invalidation.Strategies.Basic;
using Athena.Invalidation.Strategies.Advanced;
using Athena.Invalidation.AspNetCore.Providers;
using StackExchange.Redis;

namespace Athena.Invalidation.AspNetCore.Extensions;

/// <summary>
/// DI 컨테이너를 위한 서비스 등록 확장 메서드들
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 기본 무효화 엔진을 등록 (MemoryCache 사용)
    /// </summary>
    public static IServiceCollection AddInvalidationEngine(
        this IServiceCollection services,
        Action<InvalidationOptions>? configureOptions = null)
    {
        // 기본 설정
        if (configureOptions != null)
        {
            services.Configure<InvalidationOptions>(configureOptions);
        }
        else
        {
            services.Configure<InvalidationOptions>(options => { });
        }

        // 핵심 서비스 등록
        services.TryAddSingleton<IInvalidationEngine, InvalidationEngine>();
        
        // 기본 전략들 등록
        services.TryAddTransient<IInvalidationStrategy, BasicInvalidationStrategy>();
        services.TryAddTransient<IInvalidationStrategy, HierarchicalInvalidationStrategy>();

        // 기본 MemoryCache 프로바이더 등록
        services.AddMemoryCache();
        services.TryAddTransient<ICacheProvider, MemoryCacheProvider>();

        return services;
    }

    /// <summary>
    /// 무효화 엔진에 MemoryCache 프로바이더 추가
    /// </summary>
    public static IServiceCollection WithMemoryCacheProvider(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.TryAddTransient<ICacheProvider, MemoryCacheProvider>();
        return services;
    }

    /// <summary>
    /// 무효화 엔진에 Redis 프로바이더 추가
    /// </summary>
    public static IServiceCollection WithRedisCacheProvider(
        this IServiceCollection services,
        string connectionString,
        Action<ConfigurationOptions>? configureRedis = null)
    {
        // Redis 연결 설정
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var options = ConfigurationOptions.Parse(connectionString);
            configureRedis?.Invoke(options);
            return ConnectionMultiplexer.Connect(options);
        });

        // Redis Distributed Cache 등록
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
        });

        // Redis 캐시 프로바이더 등록
        services.TryAddTransient<ICacheProvider, RedisCacheProvider>();

        return services;
    }

    /// <summary>
    /// 무효화 엔진에 커스텀 캐시 프로바이더 추가
    /// </summary>
    public static IServiceCollection WithCacheProvider<T>(this IServiceCollection services)
        where T : class, ICacheProvider
    {
        services.TryAddTransient<ICacheProvider, T>();
        return services;
    }

    /// <summary>
    /// 무효화 엔진에 커스텀 전략 추가
    /// </summary>
    public static IServiceCollection WithInvalidationStrategy<T>(this IServiceCollection services)
        where T : class, IInvalidationStrategy
    {
        services.TryAddTransient<IInvalidationStrategy, T>();
        return services;
    }

    /// <summary>
    /// 무효화 엔진 완전 설정 - MemoryCache + 기본 전략들
    /// </summary>
    public static IServiceCollection AddInvalidationEngineComplete(
        this IServiceCollection services,
        Action<InvalidationOptions>? configureOptions = null)
    {
        return services
            .AddInvalidationEngine(configureOptions)
            .WithMemoryCacheProvider();
    }

    /// <summary>
    /// 무효화 엔진 완전 설정 - Redis + 기본 전략들
    /// </summary>
    public static IServiceCollection AddInvalidationEngineWithRedis(
        this IServiceCollection services,
        string redisConnectionString,
        Action<InvalidationOptions>? configureOptions = null,
        Action<ConfigurationOptions>? configureRedis = null)
    {
        return services
            .AddInvalidationEngine(configureOptions)
            .WithRedisCacheProvider(redisConnectionString, configureRedis);
    }

    /// <summary>
    /// 무효화 엔진 완전 설정 - MemoryCache + Redis + 모든 기본 전략들
    /// </summary>
    public static IServiceCollection AddInvalidationEngineHybrid(
        this IServiceCollection services,
        string redisConnectionString,
        Action<InvalidationOptions>? configureOptions = null,
        Action<ConfigurationOptions>? configureRedis = null)
    {
        return services
            .AddInvalidationEngine(configureOptions)
            .WithMemoryCacheProvider()
            .WithRedisCacheProvider(redisConnectionString, configureRedis);
    }
}