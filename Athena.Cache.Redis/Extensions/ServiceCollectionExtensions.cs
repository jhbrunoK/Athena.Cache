using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
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
}