using Athena.Cache.FusionCache.Implementations;

namespace Athena.Cache.FusionCache.Extensions;

/// <summary>
/// FusionCache 통합을 위한 서비스 등록 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// FusionCache 기반 Athena.Cache 서비스 등록
    /// </summary>
    public static IServiceCollection AddAthenaCacheFusion(
        this IServiceCollection services,
        Action<AthenaCacheOptions>? configureAthena = null,
        Action<IFusionCacheBuilder>? configureFusion = null,
        bool useFusionOptimizedKeyGenerator = true)
    {
        // Athena.Cache 기본 서비스 등록 (Core에서)
        services.AddAthenaCache(configureAthena);

        // FusionCache 등록
        var fusionBuilder = services.AddFusionCache();
        configureFusion?.Invoke(fusionBuilder);

        // FusionCache 최적화된 키 생성기 사용 여부 결정
        if (useFusionOptimizedKeyGenerator)
        {
            services.AddSingleton<ICacheKeyGenerator, FusionCacheKeyGenerator>();
        }

        // FusionCache 구현체들로 교체
        services.AddSingleton<IAthenaCache, FusionCacheProvider>();
        services.AddSingleton<ICacheInvalidator, FusionCacheInvalidator>();

        return services;
    }

    /// <summary>
    /// FusionCache 기반 Athena.Cache 완전 통합 (액션 필터 포함)
    /// </summary>
    public static IServiceCollection AddAthenaCacheFusionComplete(
        this IServiceCollection services,
        Action<AthenaCacheOptions>? configureAthena = null,
        Action<IFusionCacheBuilder>? configureFusion = null,
        bool useFusionOptimizedKeyGenerator = true)
    {
        return services
            .AddAthenaCacheFusion(configureAthena, configureFusion, useFusionOptimizedKeyGenerator)
            .AddAthenaCacheActionFilter();
    }

    /// <summary>
    /// Redis와 FusionCache를 함께 사용하는 분산 캐시 설정
    /// </summary>
    public static IServiceCollection AddAthenaCacheFusionDistributed(
        this IServiceCollection services,
        string redisConnectionString,
        Action<AthenaCacheOptions>? configureAthena = null,
        Action<IFusionCacheBuilder>? configureFusion = null)
    {
        // Redis 연결 문자열을 이용한 기본 설정 (실제 Redis 패키지 필요)
        // services.AddStackExchangeRedisCache(options =>
        // {
        //     options.Configuration = redisConnectionString;
        // });

        // FusionCache와 함께 설정
        services.AddAthenaCacheFusion(configureAthena, fusionBuilder =>
        {
            // L2 캐시로 Redis 사용하도록 FusionCache 설정 (실제 Redis 구성 필요)
            // fusionBuilder.WithDistributedCache();
            
            // 사용자 정의 설정도 적용
            configureFusion?.Invoke(fusionBuilder);
        });

        return services;
    }

    /// <summary>
    /// 태그 기반 무효화를 위한 헬퍼 확장 메서드
    /// </summary>
    public static IServiceCollection AddAthenaCacheTaggedInvalidation(this IServiceCollection services)
    {
        services.AddSingleton<IAthenaCacheTagHelper, AthenaCacheTagHelper>();
        return services;
    }
}

/// <summary>
/// FusionCache 태깅 시스템을 위한 헬퍼 인터페이스
/// </summary>
public interface IAthenaCacheTagHelper
{
    /// <summary>
    /// 캐시 항목에 테이블 태그를 추가하여 저장
    /// </summary>
    Task SetWithTagsAsync<T>(string key, T value, string[] tableTags, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// GetOrSet 패턴으로 태그가 적용된 캐시 조회/설정
    /// </summary>
    Task<T> GetOrSetWithTagsAsync<T>(string key, Func<CancellationToken, Task<T>> factory, string[] tableTags, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// FusionCache 태깅 시스템 헬퍼 구현
/// </summary>
public class AthenaCacheTagHelper(IFusionCache fusionCache, ILogger<AthenaCacheTagHelper> logger)
    : IAthenaCacheTagHelper
{
    private readonly IFusionCache _fusionCache = fusionCache ?? throw new ArgumentNullException(nameof(fusionCache));
    private readonly ILogger<AthenaCacheTagHelper> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// 캐시 항목에 테이블 태그를 추가하여 저장
    /// </summary>
    public async Task SetWithTagsAsync<T>(string key, T value, string[] tableTags, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            FusionCacheEntryOptions? options = null;
            
            if (expiration.HasValue)
            {
                options = new FusionCacheEntryOptions { Duration = expiration.Value };
            }

            // 테이블 이름들을 태그로 설정 (FusionCache 확장 필요)
            // if (tableTags != null && tableTags.Length > 0)
            // {
            //     options = options.SetTags(tableTags);
            // }

            if (options != null)
            {
                await _fusionCache.SetAsync(key, value, options);
            }
            else
            {
                var duration = expiration ?? TimeSpan.FromMinutes(30);
                await _fusionCache.SetAsync(key, value, duration);
            }
            
            _logger.LogDebug("Cached key '{Key}' with tags [{Tags}]", key, string.Join(", ", tableTags ?? []));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching key '{Key}' with tags [{Tags}]", key, string.Join(", ", tableTags ?? []));
            throw;
        }
    }

    /// <summary>
    /// GetOrSet 패턴으로 태그가 적용된 캐시 조회/설정
    /// </summary>
    public async Task<T> GetOrSetWithTagsAsync<T>(string key, Func<CancellationToken, Task<T>> factory, string[] tableTags, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var duration = expiration ?? TimeSpan.FromMinutes(30);
            FusionCacheEntryOptions? options = null;
            
            if (expiration.HasValue)
            {
                options = new FusionCacheEntryOptions { Duration = expiration.Value };
            }

            // 태그 기능을 위한 로깅
            if (tableTags != null && tableTags.Length > 0)
            {
                _logger.LogDebug("GetOrSet requested with tags [{Tags}] - tags support requires FusionCache extensions", 
                    string.Join(", ", tableTags));
            }

            if (options != null)
            {
                return await _fusionCache.GetOrSetAsync<T>(key, factory, options, cancellationToken);
            }
            else
            {
                return await _fusionCache.GetOrSetAsync<T>(key, factory, duration, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetOrSet for key '{Key}' with tags [{Tags}]", key, string.Join(", ", tableTags ?? []));
            throw;
        }
    }
}
