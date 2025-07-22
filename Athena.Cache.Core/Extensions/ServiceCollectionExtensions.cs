using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Filters;
using Athena.Cache.Core.Implementations;
using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Cache.Core.Extensions;

/// <summary>
/// Athena Cache 서비스 등록 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAthenaCache(
        this IServiceCollection services,
        Action<AthenaCacheOptions>? configure = null)
    {
        var options = new AthenaCacheOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ICacheKeyGenerator, DefaultCacheKeyGenerator>();
        services.AddSingleton<ICacheInvalidator, DefaultCacheInvalidator>();

        return services;
    }

    public static IServiceCollection AddAthenaCacheMemory(
        this IServiceCollection services,
        Action<AthenaCacheOptions>? configure = null)
    {
        services.AddAthenaCache(configure);
        services.AddSingleton<IAthenaCache, MemoryCacheProvider>();
        return services;
    }

    /// <summary>
    /// Athena 캐시 액션 필터 등록
    /// </summary>
    public static IServiceCollection AddAthenaCacheActionFilter(this IServiceCollection services)
    {
        services.AddScoped<AthenaCacheActionFilter>();

        services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add<AthenaCacheActionFilter>();
        });

        return services;
    }

    /// <summary>
    /// 전체 Athena 캐시 시스템 등록 (미들웨어 + 필터 포함)
    /// </summary>
    public static IServiceCollection AddAthenaCacheComplete(
        this IServiceCollection services,
        Action<AthenaCacheOptions>? configure = null)
    {
        return services
            .AddAthenaCache(configure)
            .AddAthenaCacheActionFilter();
    }
}