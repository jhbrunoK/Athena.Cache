using System.Diagnostics;
using Athena.Invalidation.Core.Abstractions;

namespace Athena.Invalidation.Monitoring.Telemetry;

/// <summary>
/// 무효화 작업의 분산 추적을 위한 Activity Source
/// </summary>
public static class InvalidationActivitySource
{
    private static readonly ActivitySource ActivitySource = new("Athena.Invalidation", "1.0.0");
    
    public const string ActivityNamePrefix = "Athena.Invalidation";
    
    // Activity 이름 상수
    public const string InvalidateByTableActivity = $"{ActivityNamePrefix}.InvalidateByTable";
    public const string InvalidateByPatternActivity = $"{ActivityNamePrefix}.InvalidateByPattern"; 
    public const string InvalidateByKeyActivity = $"{ActivityNamePrefix}.InvalidateByKey";
    public const string InvalidateBatchActivity = $"{ActivityNamePrefix}.InvalidateBatch";
    public const string InvalidateHierarchyActivity = $"{ActivityNamePrefix}.InvalidateHierarchy";
    public const string InvalidateCommandActivity = $"{ActivityNamePrefix}.InvalidateCommand";
    public const string InvalidateEventActivity = $"{ActivityNamePrefix}.InvalidateEvent";
    public const string DistributedEventActivity = $"{ActivityNamePrefix}.DistributedEvent";
    
    // 태그 이름 상수  
    public const string InvalidationTypeTag = "invalidation.type";
    public const string InvalidationTargetTag = "invalidation.target";
    public const string InvalidationCountTag = "invalidation.count";
    public const string InvalidationSuccessTag = "invalidation.success";
    public const string EventSourceNodeTag = "event.source_node";
    public const string EventTypeTag = "event.type";
    public const string CacheProviderTag = "cache.provider";
    public const string CacheKeyTag = "cache.key";

    /// <summary>
    /// 테이블 무효화 Activity 시작
    /// </summary>
    public static Activity? StartInvalidateByTable(string tableName, string? cacheProvider = null)
    {
        var activity = ActivitySource.StartActivity(InvalidateByTableActivity);
        activity?.SetTag(InvalidationTypeTag, InvalidationType.Table.ToString())
                  .SetTag(InvalidationTargetTag, tableName);
                  
        if (!string.IsNullOrEmpty(cacheProvider))
        {
            activity?.SetTag(CacheProviderTag, cacheProvider);
        }
        
        return activity;
    }

    /// <summary>
    /// 패턴 무효화 Activity 시작
    /// </summary>
    public static Activity? StartInvalidateByPattern(string pattern, string? cacheProvider = null)
    {
        var activity = ActivitySource.StartActivity(InvalidateByPatternActivity);
        activity?.SetTag(InvalidationTypeTag, InvalidationType.Pattern.ToString())
                  .SetTag(InvalidationTargetTag, pattern);
                  
        if (!string.IsNullOrEmpty(cacheProvider))
        {
            activity?.SetTag(CacheProviderTag, cacheProvider);
        }
        
        return activity;
    }

    /// <summary>
    /// 키 무효화 Activity 시작
    /// </summary>
    public static Activity? StartInvalidateByKey(string key, string? cacheProvider = null)
    {
        var activity = ActivitySource.StartActivity(InvalidateByKeyActivity);
        activity?.SetTag(InvalidationTypeTag, InvalidationType.Key.ToString())
                  .SetTag(InvalidationTargetTag, key)
                  .SetTag(CacheKeyTag, key);
                  
        if (!string.IsNullOrEmpty(cacheProvider))
        {
            activity?.SetTag(CacheProviderTag, cacheProvider);
        }
        
        return activity;
    }

    /// <summary>
    /// 배치 무효화 Activity 시작
    /// </summary>
    public static Activity? StartInvalidateBatch(int count, string? cacheProvider = null)
    {
        var activity = ActivitySource.StartActivity(InvalidateBatchActivity);
        activity?.SetTag(InvalidationTypeTag, InvalidationType.Batch.ToString())
                  .SetTag(InvalidationCountTag, count);
                  
        if (!string.IsNullOrEmpty(cacheProvider))
        {
            activity?.SetTag(CacheProviderTag, cacheProvider);
        }
        
        return activity;
    }

    /// <summary>
    /// 계층적 무효화 Activity 시작
    /// </summary>
    public static Activity? StartInvalidateHierarchy(string rootTable, int maxDepth, string? cacheProvider = null)
    {
        var activity = ActivitySource.StartActivity(InvalidateHierarchyActivity);
        activity?.SetTag(InvalidationTypeTag, InvalidationType.Hierarchy.ToString())
                  .SetTag(InvalidationTargetTag, rootTable)
                  .SetTag("hierarchy.max_depth", maxDepth);
                  
        if (!string.IsNullOrEmpty(cacheProvider))
        {
            activity?.SetTag(CacheProviderTag, cacheProvider);
        }
        
        return activity;
    }

    /// <summary>
    /// 명령 무효화 Activity 시작
    /// </summary>
    public static Activity? StartInvalidateCommand(string commandType, string? cacheProvider = null)
    {
        var activity = ActivitySource.StartActivity(InvalidateCommandActivity);
        activity?.SetTag(InvalidationTypeTag, InvalidationType.Custom.ToString())
                  .SetTag(InvalidationTargetTag, commandType)
                  .SetTag("command.type", commandType);
                  
        if (!string.IsNullOrEmpty(cacheProvider))
        {
            activity?.SetTag(CacheProviderTag, cacheProvider);
        }
        
        return activity;
    }

    /// <summary>
    /// 이벤트 무효화 Activity 시작
    /// </summary>
    public static Activity? StartInvalidateEvent(string eventType, string? aggregateId = null, string? cacheProvider = null)
    {
        var activity = ActivitySource.StartActivity(InvalidateEventActivity);
        activity?.SetTag(InvalidationTypeTag, InvalidationType.Custom.ToString())
                  .SetTag(InvalidationTargetTag, eventType)
                  .SetTag("domain_event.type", eventType);
                  
        if (!string.IsNullOrEmpty(aggregateId))
        {
            activity?.SetTag("domain_event.aggregate_id", aggregateId);
        }
        
        if (!string.IsNullOrEmpty(cacheProvider))
        {
            activity?.SetTag(CacheProviderTag, cacheProvider);
        }
        
        return activity;
    }

    /// <summary>
    /// 분산 이벤트 처리 Activity 시작
    /// </summary>
    public static Activity? StartDistributedEvent(string eventType, string sourceNodeId)
    {
        var activity = ActivitySource.StartActivity(DistributedEventActivity);
        activity?.SetTag(EventTypeTag, eventType)
                  .SetTag(EventSourceNodeTag, sourceNodeId);
        
        return activity;
    }

    /// <summary>
    /// Activity에 성공/실패 상태 설정
    /// </summary>
    public static void SetSuccess(this Activity? activity, bool success, Exception? exception = null)
    {
        if (activity == null) return;
        
        activity.SetTag(InvalidationSuccessTag, success);
        
        if (!success)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception?.Message ?? "Operation failed");
            
            if (exception != null)
            {
                activity.SetTag("exception.type", exception.GetType().FullName);
                activity.SetTag("exception.message", exception.Message);
                activity.SetTag("exception.stacktrace", exception.StackTrace);
            }
        }
        else
        {
            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    /// <summary>
    /// Activity에 추가 컨텍스트 정보 설정
    /// </summary>
    public static Activity? SetContext(this Activity? activity, IInvalidationContext? context)
    {
        if (activity == null || context == null) return activity;
        
        activity.SetTag("context.trigger", context.Trigger.ToString());
        activity.SetTag("context.timestamp", context.Timestamp.ToString("O"));
        
        if (context.Metadata != null)
        {
            foreach (var kvp in context.Metadata.Take(5)) // 상위 5개만 추가
            {
                activity.SetTag($"context.metadata.{kvp.Key}", kvp.Value?.ToString());
            }
        }
        
        return activity;
    }

    /// <summary>
    /// Activity에 캐시 관련 정보 설정
    /// </summary>
    public static Activity? SetCacheInfo(this Activity? activity, string? cacheProvider, int? keyCount = null)
    {
        if (activity == null) return activity;
        
        if (!string.IsNullOrEmpty(cacheProvider))
        {
            activity.SetTag(CacheProviderTag, cacheProvider);
        }
        
        if (keyCount.HasValue)
        {
            activity.SetTag("cache.key_count", keyCount.Value);
        }
        
        return activity;
    }

    /// <summary>
    /// ActivitySource 정리
    /// </summary>
    public static void Dispose()
    {
        ActivitySource.Dispose();
    }
}