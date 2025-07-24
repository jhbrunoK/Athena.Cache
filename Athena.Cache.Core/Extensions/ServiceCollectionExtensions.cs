using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Filters;
using Athena.Cache.Core.Implementations;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Cache.Core.Extensions;

/// <summary>
/// Athena Cache 서비스 등록 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 기본 Athena Cache 서비스 등록 (IAthenaCache 제외)
    /// </summary>
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

    /// <summary>
    /// MemoryCache 기반 Athena 캐시 등록
    /// </summary>
    public static IServiceCollection AddAthenaCacheMemory(
        this IServiceCollection services,
        Action<AthenaCacheOptions>? configure = null)
    {
        // 기본 서비스 등록
        services.AddAthenaCache(configure);

        // MemoryCache 의존성 확인 및 등록
        services.AddMemoryCache();

        // MemoryCache 구현체 등록
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
    /// 전체 Athena 캐시 시스템 등록 (MemoryCache + 미들웨어 + 필터)
    /// </summary>
    public static IServiceCollection AddAthenaCacheComplete(
        this IServiceCollection services,
        Action<AthenaCacheOptions>? configure = null)
    {
        return services
            .AddAthenaCacheMemory(configure)
            .AddAthenaCacheActionFilter();
    }

    /// <summary>
    /// 커스텀 캐시 구현체 등록 (고급 사용자용)
    /// </summary>
    public static IServiceCollection AddAthenaCacheCustom<TCacheProvider>(
        this IServiceCollection services,
        Action<AthenaCacheOptions>? configure = null)
        where TCacheProvider : class, IAthenaCache
    {
        // 기본 서비스 등록
        services.AddAthenaCache(configure);

        // 커스텀 구현체 등록
        services.AddSingleton<IAthenaCache, TCacheProvider>();

        return services;
    }

    /// <summary>
    /// 서비스 등록 상태 검증 (디버깅/테스트용)
    /// </summary>
    public static void ValidateAthenaCacheServices(this IServiceProvider serviceProvider)
    {
        var requiredServices = new[]
        {
            typeof(AthenaCacheOptions),
            typeof(ICacheKeyGenerator),
            typeof(ICacheInvalidator),
            typeof(IAthenaCache)
        };

        var missingServices = new List<string>();

        foreach (var serviceType in requiredServices)
        {
            var service = serviceProvider.GetService(serviceType);
            if (service == null)
            {
                missingServices.Add(serviceType.Name);
            }
        }

        if (missingServices.Any())
        {
            throw new InvalidOperationException(
                $"다음 Athena Cache 서비스들이 등록되지 않았습니다: {string.Join(", ", missingServices)}");
        }
    }
}