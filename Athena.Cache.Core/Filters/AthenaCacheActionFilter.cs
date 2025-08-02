using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Attributes;
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Models;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Athena.Cache.Core.Filters;

/// <summary>
/// Athena 캐시 Action Filter
/// 미들웨어로 전달할 캐시 메타데이터를 수집하고 설정
/// </summary>
public class AthenaCacheActionFilter(ILogger<AthenaCacheActionFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            // NoCacheAttribute가 있으면 캐시 비활성화
            if (HasNoCacheAttribute(context))
            {
                context.HttpContext.Items["AthenaCache.Disabled"] = true;
                await next();
                return;
            }

            // 캐시 설정 수집
            var cacheConfig = BuildCacheConfiguration(context);
            if (cacheConfig != null)
            {
                // HttpContext.Items에 캐시 설정 저장 (미들웨어에서 사용)
                context.HttpContext.Items["AthenaCache.Config"] = cacheConfig;

                if (context.HttpContext.RequestServices.GetService<AthenaCacheOptions>() is AthenaCacheOptions athenaOptions &&
                    athenaOptions.Logging.LogKeyGeneration)
                {
                    logger.LogDebug("Cache configuration set for {Controller}.{Action}",
                        cacheConfig.Controller, cacheConfig.Action);
                }
            }

            // 액션 실행
            var executedContext = await next();

            // 액션 실행 후 HTTP 메서드별 처리
            if (executedContext.HttpContext.Items.TryGetValue("AthenaCache.Config", out var configObj) &&
                configObj is CacheConfiguration config &&
                config.InvalidationRules.Any())
            {
                var invalidator = executedContext.HttpContext.RequestServices.GetService<ICacheInvalidator>();
                if (invalidator != null)
                {
                    var httpMethod = executedContext.HttpContext.Request.Method;
                    
                    if (IsGetRequest(httpMethod))
                    {
                        // GET 요청: 캐시 키를 테이블들과 연결하여 추적
                        var cacheKey = executedContext.HttpContext.Items["AthenaCache.GeneratedKey"] as string;
                        if (!string.IsNullOrEmpty(cacheKey))
                        {
                            var tablesToTrack = config.InvalidationRules
                                .Select(rule => rule.TableName)
                                .Distinct()
                                .ToArray();

                            await invalidator.TrackCacheKeyAsync(tablesToTrack, cacheKey);
                            
                            if (context.HttpContext.RequestServices.GetService<AthenaCacheOptions>() is AthenaCacheOptions options &&
                                options.Logging.LogInvalidation)
                            {
                                logger.LogDebug("Tracked cache key for tables [{Tables}] in {Controller}.{Action}",
                                    string.Join(", ", tablesToTrack), config.Controller, config.Action);
                            }
                        }
                    }
                    else if (IsModifyingRequest(httpMethod))
                    {
                        // POST/PUT/DELETE 요청: 관련 테이블 캐시 무효화
                        var tablesToInvalidate = config.InvalidationRules
                            .Select(rule => rule.TableName)
                            .Distinct()
                            .ToArray();

                        foreach (var tableName in tablesToInvalidate)
                        {
                            await invalidator.InvalidateAsync(tableName);
                        }
                        
                        if (context.HttpContext.RequestServices.GetService<AthenaCacheOptions>() is AthenaCacheOptions options &&
                            options.Logging.LogInvalidation)
                        {
                            logger.LogInformation("Invalidated cache for tables [{Tables}] after {Method} {Controller}.{Action}",
                                string.Join(", ", tablesToInvalidate), httpMethod, config.Controller, config.Action);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in AthenaCacheActionFilter");
            // 예외가 발생해도 액션은 계속 실행
            await next();
        }
    }

    private static bool HasNoCacheAttribute(ActionExecutingContext context)
    {
        return context.ActionDescriptor.FilterDescriptors
            .Any(fd => fd.Filter is NoCacheAttribute);
    }

    /// <summary>
    /// GET 요청인지 확인
    /// </summary>
    private static bool IsGetRequest(string httpMethod)
    {
        return string.Equals(httpMethod, "GET", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 데이터 수정 요청인지 확인 (POST, PUT, DELETE, PATCH)
    /// </summary>
    private static bool IsModifyingRequest(string httpMethod)
    {
        return string.Equals(httpMethod, "POST", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(httpMethod, "PUT", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(httpMethod, "DELETE", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(httpMethod, "PATCH", StringComparison.OrdinalIgnoreCase);
    }

    private static CacheConfiguration? BuildCacheConfiguration(ActionExecutingContext context)
    {
        var controllerName = context.Controller.GetType().Name;
        var actionName = context.ActionDescriptor.RouteValues["action"] ?? "Unknown";

        // AthenaCacheAttribute 수집
        var cacheAttribute = GetAttribute<AthenaCacheAttribute>(context);
        if (cacheAttribute?.Enabled == false)
        {
            return null;
        }

        // CacheInvalidateOnAttribute들 수집 (명시적 선언)
        var invalidationAttributes = GetAttributes<CacheInvalidateOnAttribute>(context);

        // Convention 기반 테이블명 추론 및 병합
        var allInvalidationRules = MergeInvalidationRules(context, invalidationAttributes, controllerName);

        var config = new CacheConfiguration
        {
            Controller = controllerName,
            Action = actionName,
            Enabled = cacheAttribute?.Enabled ?? true,
            ExpirationMinutes = cacheAttribute?.ExpirationMinutes ?? -1,
            MaxRelatedDepth = cacheAttribute?.MaxRelatedDepth ?? -1,
            AdditionalKeyParameters = cacheAttribute?.AdditionalKeyParameters ?? [],
            ExcludeParameters = cacheAttribute?.ExcludeParameters ?? [],
            CustomKeyPrefix = cacheAttribute?.CustomKeyPrefix,
            InvalidationRules = allInvalidationRules
        };

        return config;
    }

    private static T? GetAttribute<T>(ActionExecutingContext context) where T : Attribute
    {
        // 액션 레벨에서 먼저 찾기
        var actionAttributes = context.ActionDescriptor.FilterDescriptors
            .Where(fd => fd.Filter is T)
            .Select(fd => fd.Filter as T)
            .FirstOrDefault();

        if (actionAttributes != null) return actionAttributes;

        // 컨트롤러 레벨에서 찾기
        var controllerAttributes = context.Controller.GetType()
            .GetCustomAttributes(typeof(T), true)
            .Cast<T>()
            .FirstOrDefault();

        return controllerAttributes;
    }

    private static IEnumerable<T> GetAttributes<T>(ActionExecutingContext context) where T : Attribute
    {
        var attributes = new List<T>();

        // 액션 레벨 속성들
        var actionAttributes = context.ActionDescriptor.FilterDescriptors
            .Where(fd => fd.Filter is T)
            .Select(fd => fd.Filter as T)
            .Where(attr => attr != null)
            .Cast<T>();

        attributes.AddRange(actionAttributes);

        // 컨트롤러 레벨 속성들
        var controllerAttributes = context.Controller.GetType()
            .GetCustomAttributes(typeof(T), true)
            .Cast<T>();

        attributes.AddRange(controllerAttributes);

        return attributes;
    }

    /// <summary>
    /// Convention 기반 테이블명 추론과 명시적 선언을 병합
    /// </summary>
    private static List<TableInvalidationRule> MergeInvalidationRules(
        ActionExecutingContext context, 
        IEnumerable<CacheInvalidateOnAttribute> explicitAttributes, 
        string controllerName)
    {
        var rules = new List<TableInvalidationRule>();

        // 1. 명시적 선언 추가 (최우선)
        rules.AddRange(explicitAttributes.Select(attr => new TableInvalidationRule
        {
            TableName = attr.TableName,
            InvalidationType = attr.InvalidationType,
            Pattern = attr.Pattern,
            RelatedTables = attr.RelatedTables,
            MaxDepth = attr.MaxDepth
        }));

        // 2. Convention 기반 추론 추가
        var conventionOptions = context.HttpContext.RequestServices.GetService<AthenaCacheOptions>()?.Convention;
        if (conventionOptions?.Enabled == true && !IsConventionDisabled(context, controllerName, conventionOptions))
        {
            var inferredTableNames = InferTableNamesFromController(controllerName, conventionOptions);
            
            foreach (var tableName in inferredTableNames)
            {
                // 이미 명시적으로 선언된 테이블은 중복 추가하지 않음
                if (!rules.Any(r => r.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase)))
                {
                    rules.Add(new TableInvalidationRule
                    {
                        TableName = tableName,
                        InvalidationType = InvalidationType.All,
                        Pattern = null,
                        RelatedTables = [],
                        MaxDepth = -1
                    });
                }
            }
        }

        return rules;
    }

    /// <summary>
    /// 컨트롤러명에서 테이블명 추론
    /// </summary>
    private static string[] InferTableNamesFromController(string controllerName, ConventionOptions conventionOptions)
    {
        // 1. 설정 기반 매핑 확인 (최우선)
        if (conventionOptions.ControllerTableMappings.TryGetValue(controllerName, out var mappedTables))
        {
            return mappedTables;
        }

        // 2. 커스텀 다중 테이블 추론 함수 사용
        if (conventionOptions.CustomMultiTableInferrer != null)
        {
            return conventionOptions.CustomMultiTableInferrer(controllerName);
        }

        // 3. 기본 단일 테이블 추론
        var baseTableName = ExtractBaseTableName(controllerName, conventionOptions);
        return [baseTableName];
    }

    /// <summary>
    /// 컨트롤러명에서 기본 테이블명 추출
    /// </summary>
    private static string ExtractBaseTableName(string controllerName, ConventionOptions conventionOptions)
    {
        // "Controller" 접미사 제거
        var baseName = controllerName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
            ? controllerName[..^10]  // "Controller" 길이 = 10
            : controllerName;

        // 커스텀 단일 변환 함수 사용
        if (conventionOptions.CustomPluralizer != null)
        {
            return conventionOptions.CustomPluralizer(baseName);
        }

        // 기본 동작: 복수형 변환 여부에 따라 처리
        if (conventionOptions.UsePluralizer)
        {
            // 간단한 복수형 변환 (향후 Humanizer 라이브러리 연동 가능)
            return ConvertToPlural(baseName);
        }

        return baseName;
    }

    /// <summary>
    /// 간단한 복수형 변환 (기본 구현)
    /// </summary>
    private static string ConvertToPlural(string singular)
    {
        // 이미 복수형인지 확인 (간단한 휴리스틱)
        if (singular.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
            singular.EndsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            return singular;
        }

        // 기본 복수형 규칙
        if (singular.EndsWith("y", StringComparison.OrdinalIgnoreCase))
        {
            return singular[..^1] + "ies";
        }
        
        if (singular.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
            singular.EndsWith("sh", StringComparison.OrdinalIgnoreCase) ||
            singular.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
            singular.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
            singular.EndsWith("z", StringComparison.OrdinalIgnoreCase))
        {
            return singular + "es";
        }

        return singular + "s";
    }

    /// <summary>
    /// Convention 기반 추론이 비활성화되어 있는지 확인
    /// </summary>
    private static bool IsConventionDisabled(ActionExecutingContext context, string controllerName, ConventionOptions conventionOptions)
    {
        // 1. NoConventionInvalidationAttribute 체크 (액션 레벨 우선)
        var noConventionAttribute = GetAttribute<NoConventionInvalidationAttribute>(context);
        if (noConventionAttribute != null)
        {
            return true;
        }

        // 2. 전역 설정에서 제외된 컨트롤러 체크
        if (conventionOptions.ExcludedControllers.Contains(controllerName))
        {
            return true;
        }

        return false;
    }
}