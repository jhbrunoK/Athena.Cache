using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Extensions;
using Athena.Cache.Core.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Athena.Cache.Redis.Extensions;

/// <summary>
/// Redis 캐시 서비스 등록 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Redis 기반 Athena 캐시 등록
    /// </summary>
    public static IServiceCollection AddAthenaCacheRedis(
        this IServiceCollection services,
        Action<AthenaCacheOptions>? configureAthena = null,
        Action<RedisCacheOptions>? configureRedis = null)
    {
        // 기본 Athena Cache 서비스 등록 (ICacheConfigurationRegistry 포함)
        services.AddAthenaCache(configureAthena);

        // Redis 캐시 옵션 설정
        var redisOptions = new RedisCacheOptions();
        configureRedis?.Invoke(redisOptions);
        services.AddSingleton(redisOptions);

        // Redis 연결 등록
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var options = provider.GetRequiredService<RedisCacheOptions>();
            var configuration = ConfigurationOptions.Parse(options.ConnectionString);
            configuration.ConnectTimeout = options.ConnectTimeoutSeconds * 1000;
            configuration.ConnectRetry = options.RetryCount;
            configuration.AbortOnConnectFail = false; // 연결 실패 시에도 계속 시도

            return ConnectionMultiplexer.Connect(configuration);
        });

        // Redis 구현체로 재등록
        services.AddSingleton<IAthenaCache, RedisCacheProvider>();

        return services;
    }

    /// <summary>
    /// Redis 캐시 전체 시스템 등록 (미들웨어 + 필터 포함)
    /// </summary>
    public static IServiceCollection AddAthenaCacheRedisComplete(
        this IServiceCollection services,
        Action<AthenaCacheOptions>? configureAthena = null,
        Action<RedisCacheOptions>? configureRedis = null)
    {
        return services
            .AddAthenaCacheRedis(configureAthena, configureRedis)
            .AddAthenaCacheActionFilter();
    }

    /// <summary>
    /// Redis 기반 분산 캐시 무효화 시스템 등록
    /// </summary>
    public static IServiceCollection AddAthenaCacheRedisDistributed(
        this IServiceCollection services,
        Action<AthenaCacheOptions>? configureAthena = null,
        Action<RedisCacheOptions>? configureRedis = null)
    {
        // 기본 Redis 캐시 등록
        services.AddAthenaCacheRedis(configureAthena, configureRedis);

        // 지능형 캐시 관리자 등록
        services.AddSingleton<IIntelligentCacheManager, IntelligentCacheManager>();

        // 기존 로컬 무효화기를 분산 무효화기로 교체
        services.AddSingleton<ICacheInvalidator>(provider =>
        {
            var cache = provider.GetRequiredService<IAthenaCache>();
            var keyGenerator = provider.GetRequiredService<ICacheKeyGenerator>();
            var options = provider.GetRequiredService<AthenaCacheOptions>();
            var logger = provider.GetRequiredService<ILogger<DefaultCacheInvalidator>>();
            
            // 로컬 무효화기 생성
            var localInvalidator = new DefaultCacheInvalidator(cache, keyGenerator, options, logger);
            
            // Redis 연결 및 분산 무효화기 생성
            var redis = provider.GetRequiredService<IConnectionMultiplexer>();
            var distributedLogger = provider.GetRequiredService<ILogger<DistributedCacheInvalidator>>();

            return new DistributedCacheInvalidator(redis, localInvalidator, options, distributedLogger);
        });

        // 분산 무효화기도 별도로 등록 (명시적 사용을 위해)
        services.AddSingleton<IDistributedCacheInvalidator>(provider =>
            (IDistributedCacheInvalidator)provider.GetRequiredService<ICacheInvalidator>());

        return services;
    }

    /// <summary>
    /// Redis 기반 엔터프라이즈 캐시 시스템 등록 (분산 무효화 + 지능형 관리)
    /// </summary>
    public static IServiceCollection AddAthenaCacheRedisEnterprise(
        this IServiceCollection services,
        Action<AthenaCacheOptions>? configureAthena = null,
        Action<RedisCacheOptions>? configureRedis = null)
    {
        // 분산 캐시 시스템 등록
        services.AddAthenaCacheRedisDistributed(configureAthena, configureRedis);

        // 액션 필터도 추가
        services.AddAthenaCacheActionFilter();

        return services;
    }
}