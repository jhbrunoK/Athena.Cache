using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Security;

namespace Athena.Cache.Core.Extensions;

/// <summary>
/// 보안 기능을 위한 서비스 컬렉션 확장 메서드
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Athena Cache 보안 기능을 추가합니다
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">보안 옵션 설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddAthenaCacheSecurity(
        this IServiceCollection services,
        Action<SecureCacheOptions>? configureOptions = null)
    {
        // 기본 보안 옵션 등록
        var securityOptions = new SecureCacheOptions();
        configureOptions?.Invoke(securityOptions);
        services.TryAddSingleton(securityOptions);

        // 보안 키 생성기 옵션 등록
        services.TryAddSingleton<SecureCacheKeyOptions>();

        // 보안 키 생성기 등록
        services.TryAddSingleton<SecureCacheKeyGenerator>();

        // ICacheKeyGenerator로도 등록 (기존 인터페이스 대체)
        services.TryAddSingleton<ICacheKeyGenerator>(provider => 
            provider.GetRequiredService<SecureCacheKeyGenerator>());

        return services;
    }

    /// <summary>
    /// 보안이 강화된 캐시 키 생성기를 추가합니다
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">키 생성기 옵션 설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddSecureCacheKeyGenerator(
        this IServiceCollection services,
        Action<SecureCacheKeyOptions>? configureOptions = null)
    {
        // 키 생성기 옵션 설정
        var keyOptions = new SecureCacheKeyOptions();
        configureOptions?.Invoke(keyOptions);
        services.TryAddSingleton(keyOptions);

        // 보안 키 생성기 등록
        services.TryAddSingleton<SecureCacheKeyGenerator>();
        services.TryAddSingleton<ICacheKeyGenerator>(provider => 
            provider.GetRequiredService<SecureCacheKeyGenerator>());

        return services;
    }

    /// <summary>
    /// 보안이 강화된 캐시 매니저를 추가합니다
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">보안 옵션 설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddSecureCacheManager(
        this IServiceCollection services,
        Action<SecureCacheOptions>? configureOptions = null)
    {
        // 보안 기능 기본 설정 (아직 등록되지 않은 경우)
        services.AddAthenaCacheSecurity(configureOptions);

        // 보안 캐시 매니저를 데코레이터로 등록
        services.TryAddSingleton<SecureCacheManager>();

        return services;
    }

    /// <summary>
    /// 민감한 데이터 감지기 설정을 커스터마이즈합니다
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureDetector">감지기 설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection ConfigureSensitiveDataDetection(
        this IServiceCollection services,
        Action<SensitiveDataDetectorConfiguration> configureDetector)
    {
        var config = new SensitiveDataDetectorConfiguration();
        configureDetector(config);

        // 설정된 패턴들을 감지기에 추가
        foreach (var pattern in config.CustomPatterns)
        {
            SensitiveDataDetector.AddSensitivePattern(pattern.Key, pattern.Value);
        }

        foreach (var fieldName in config.CustomSensitiveFields)
        {
            SensitiveDataDetector.AddSensitiveFieldName(fieldName);
        }

        return services;
    }

    /// <summary>
    /// 전체 캐시 시스템에 보안 래퍼를 적용합니다
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">보안 옵션 설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection SecureAllCacheManagers(
        this IServiceCollection services,
        Action<SecureCacheOptions>? configureOptions = null)
    {
        // 보안 기능 기본 설정
        services.AddAthenaCacheSecurity(configureOptions);

        // 기존 IAthenaCache 등록을 보안 버전으로 래핑
        services.Decorate<IAthenaCache, SecureCacheManager>();

        return services;
    }

    /// <summary>
    /// 개발 환경용 관대한 보안 설정을 적용합니다
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddDevelopmentCacheSecurity(this IServiceCollection services)
    {
        return services.AddAthenaCacheSecurity(options =>
        {
            options.StrictMode = false;
            options.ThrowOnSensitiveData = false;
            options.MaskSensitiveData = true;
            options.ValidateOnGet = false;
            options.BlockSensitiveRetrieval = false;
            options.EnableSecurityLogging = true;
            options.FailSafeMode = true;
        }).AddSecureCacheKeyGenerator(keyOptions =>
        {
            keyOptions.KeyGenerationStrategy = KeyGenerationStrategy.PlainText;
            keyOptions.StrictMode = false;
            keyOptions.AllowedCharacters = null; // 모든 문자 허용
        });
    }

    /// <summary>
    /// 프로덕션 환경용 엄격한 보안 설정을 적용합니다
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddProductionCacheSecurity(this IServiceCollection services)
    {
        return services.AddAthenaCacheSecurity(options =>
        {
            options.StrictMode = true;
            options.ThrowOnSensitiveData = false; // 로그만 남기고 차단
            options.MaskSensitiveData = false;    // 민감한 데이터는 아예 저장하지 않음
            options.ValidateOnGet = true;
            options.BlockSensitiveRetrieval = true;
            options.EnableSecurityLogging = true;
            options.FailSafeMode = true;
        }).AddSecureCacheKeyGenerator(keyOptions =>
        {
            keyOptions.KeyGenerationStrategy = KeyGenerationStrategy.Hybrid;
            keyOptions.HashAlgorithm = HashAlgorithmType.SHA256;
            keyOptions.StrictMode = true;
            keyOptions.UseSalt = true;
        });
    }
}

/// <summary>
/// 민감한 데이터 감지기 설정
/// </summary>
public class SensitiveDataDetectorConfiguration
{
    /// <summary>
    /// 커스텀 민감한 데이터 패턴들
    /// </summary>
    public Dictionary<string, string> CustomPatterns { get; set; } = new();

    /// <summary>
    /// 커스텀 민감한 필드명들
    /// </summary>
    public List<string> CustomSensitiveFields { get; set; } = new();

    /// <summary>
    /// 커스텀 패턴을 추가합니다
    /// </summary>
    /// <param name="name">패턴 이름</param>
    /// <param name="regex">정규표현식</param>
    /// <returns>설정 객체</returns>
    public SensitiveDataDetectorConfiguration AddPattern(string name, string regex)
    {
        CustomPatterns[name] = regex;
        return this;
    }

    /// <summary>
    /// 커스텀 민감한 필드명을 추가합니다
    /// </summary>
    /// <param name="fieldName">필드명</param>
    /// <returns>설정 객체</returns>
    public SensitiveDataDetectorConfiguration AddSensitiveField(string fieldName)
    {
        CustomSensitiveFields.Add(fieldName);
        return this;
    }

    /// <summary>
    /// 여러 민감한 필드명을 한번에 추가합니다
    /// </summary>
    /// <param name="fieldNames">필드명들</param>
    /// <returns>설정 객체</returns>
    public SensitiveDataDetectorConfiguration AddSensitiveFields(params string[] fieldNames)
    {
        CustomSensitiveFields.AddRange(fieldNames);
        return this;
    }
}

/// <summary>
/// 서비스 등록 확장 (Decorate 메서드 구현)
/// </summary>
internal static class ServiceCollectionDecorateExtensions
{
    public static IServiceCollection Decorate<TInterface, TDecorator>(
        this IServiceCollection services)
        where TDecorator : class, TInterface
        where TInterface : class
    {
        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(TInterface));
        if (descriptor == null)
        {
            throw new InvalidOperationException($"Service of type {typeof(TInterface).Name} is not registered.");
        }

        var decoratedDescriptor = ServiceDescriptor.Describe(
            typeof(TInterface),
            provider => ActivatorUtilities.CreateInstance<TDecorator>(provider, 
                provider.GetRequiredService<TInterface>()),
            descriptor.Lifetime);

        services.Remove(descriptor);
        services.Add(decoratedDescriptor);

        return services;
    }
}