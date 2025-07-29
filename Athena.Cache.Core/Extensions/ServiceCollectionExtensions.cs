using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Diagnostics;
using Athena.Cache.Core.Filters;
using Athena.Cache.Core.Implementations;
using Athena.Cache.Core.Interfaces;
using Athena.Cache.Core.Models;
using Athena.Cache.Core.ObjectPools;
using Athena.Cache.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.ObjectPool;
using System.Reflection;

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
        
        // Object Pool 서비스 등록 (성능 최적화)
        services.AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
        services.AddSingleton<CachedResponsePool>();
        
        // 성능 모니터링 서비스 등록
        services.AddSingleton<CachePerformanceMonitor>();
        
        // Registry 등록 - Source Generator가 있으면 그것을 사용, 없으면 Reflection 백업
        RegisterCacheConfigurationRegistry(services);

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
    /// 전체 Athena 캐시 시스템 등록 (MemoryCache + 미들웨어)
    /// </summary>
    public static IServiceCollection AddAthenaCacheComplete(
        this IServiceCollection services,
        Action<AthenaCacheOptions>? configure = null)
    {
        return services
            .AddAthenaCacheMemory(configure);
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
    /// 캐시 설정 레지스트리 등록
    /// Source Generator가 생성한 구현체가 있으면 그것을 사용하고, 없으면 Reflection 백업 사용
    /// </summary>
    private static void RegisterCacheConfigurationRegistry(IServiceCollection services)
    {
        services.AddSingleton<ICacheConfigurationRegistry>(serviceProvider =>
        {
            // Source Generator가 생성한 구현체 탐지 시도
            try
            {
                // 현재 어셈블리에서 생성된 타입 찾기
                var currentAssembly = typeof(ServiceCollectionExtensions).Assembly;
                var generatedType = currentAssembly.GetType("Athena.Cache.Core.Generated.CacheConfigurationRegistry");
                if (generatedType != null)
                {
                    var instance = Activator.CreateInstance(generatedType);
                    if (instance != null)
                    {
                        // 생성된 클래스를 래핑하여 인터페이스 구현
                        return new GeneratedRegistryWrapper(instance);
                    }
                }
                
                // 다른 방법으로 시도 - 어셈블리 내 모든 타입 검색
                var types = currentAssembly.GetTypes();
                var generatedRegistryType = types.FirstOrDefault(t => 
                    t.Name == "CacheConfigurationRegistry" && 
                    t.Namespace == "Athena.Cache.Core.Generated");
                
                if (generatedRegistryType != null)
                {
                    var instance = Activator.CreateInstance(generatedRegistryType);
                    if (instance != null)
                    {
                        // 생성된 클래스를 래핑하여 인터페이스 구현
                        return new GeneratedRegistryWrapper(instance);
                    }
                }
            }
            catch (Exception ex)
            {
                // 디버깅을 위해 예외 로그 (프로덕션에서는 제거)
                System.Diagnostics.Debug.WriteLine($"Source Generator registry detection failed: {ex.Message}");
            }

            // 백업으로 Reflection 기반 구현체 사용
            return new ReflectionCacheConfigurationRegistry();
        });
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

/// <summary>
/// Source Generator로 생성된 클래스를 ICacheConfigurationRegistry 인터페이스로 래핑
/// </summary>
internal class GeneratedRegistryWrapper : ICacheConfigurationRegistry
{
    private readonly object _instance;
    private readonly MethodInfo _getConfigurationMethod;
    private readonly MethodInfo _getAllConfigurationsMethod;

    public GeneratedRegistryWrapper(object instance)
    {
        _instance = instance;
        var type = instance.GetType();
        
        _getConfigurationMethod = type.GetMethod("GetConfiguration",
                                      [typeof(string), typeof(string)]) 
            ?? throw new InvalidOperationException("GetConfiguration method not found");
            
        _getAllConfigurationsMethod = type.GetMethod("GetAllConfigurations") 
            ?? throw new InvalidOperationException("GetAllConfigurations method not found");
    }

    public CacheConfiguration? GetConfiguration(string controllerName, string actionName)
    {
        return (CacheConfiguration?)_getConfigurationMethod.Invoke(_instance, [controllerName, actionName]);
    }

    public IReadOnlyDictionary<string, CacheConfiguration> GetAllConfigurations()
    {
        return (IReadOnlyDictionary<string, CacheConfiguration>)_getAllConfigurationsMethod.Invoke(_instance, null)!;
    }
}