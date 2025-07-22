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

            // 액션 실행 후 테이블 추적 설정
            if (executedContext.HttpContext.Items.TryGetValue("AthenaCache.Config", out var configObj) &&
                configObj is CacheConfiguration config &&
                config.InvalidationRules.Any())
            {
                var invalidator = executedContext.HttpContext.RequestServices.GetService<ICacheInvalidator>();
                var cacheKey = executedContext.HttpContext.Items["AthenaCache.GeneratedKey"] as string;

                if (invalidator != null && !string.IsNullOrEmpty(cacheKey))
                {
                    // 캐시 키를 테이블들과 연결하여 추적
                    var tablesToTrack = config.InvalidationRules
                        .Select(rule => rule.TableName)
                        .Distinct()
                        .ToArray();

                    await invalidator.TrackCacheKeyAsync(tablesToTrack, cacheKey);
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

        // CacheInvalidateOnAttribute들 수집
        var invalidationAttributes = GetAttributes<CacheInvalidateOnAttribute>(context);

        var config = new CacheConfiguration
        {
            Controller = controllerName,
            Action = actionName,
            Enabled = cacheAttribute?.Enabled ?? true,
            ExpirationMinutes = cacheAttribute?.ExpirationMinutes ?? -1,
            MaxRelatedDepth = cacheAttribute?.MaxRelatedDepth ?? -1,
            AdditionalKeyParameters = cacheAttribute?.AdditionalKeyParameters ?? Array.Empty<string>(),
            ExcludeParameters = cacheAttribute?.ExcludeParameters ?? Array.Empty<string>(),
            CustomKeyPrefix = cacheAttribute?.CustomKeyPrefix,
            InvalidationRules = invalidationAttributes.Select(attr => new TableInvalidationRule
            {
                TableName = attr.TableName,
                InvalidationType = attr.InvalidationType,
                Pattern = attr.Pattern,
                RelatedTables = attr.RelatedTables,
                MaxDepth = attr.MaxDepth
            }).ToList()
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
}