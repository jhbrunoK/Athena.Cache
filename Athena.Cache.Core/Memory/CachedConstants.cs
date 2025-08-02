namespace Athena.Cache.Core.Memory;

/// <summary>
/// 자주 사용되는 상수들을 미리 캐싱하여 메모리 할당 최소화
/// 문자열 인터닝과 유사한 효과를 제공
/// </summary>
public static class CachedConstants
{
    // HTTP 메서드들
    public static readonly string HttpGet = LazyCache.InternString("GET");
    public static readonly string HttpPost = LazyCache.InternString("POST");
    public static readonly string HttpPut = LazyCache.InternString("PUT");
    public static readonly string HttpDelete = LazyCache.InternString("DELETE");
    public static readonly string HttpPatch = LazyCache.InternString("PATCH");
    public static readonly string HttpHead = LazyCache.InternString("HEAD");
    public static readonly string HttpOptions = LazyCache.InternString("OPTIONS");
    
    // Content Types
    public static readonly string ContentTypeJson = LazyCache.InternString("application/json");
    public static readonly string ContentTypeText = LazyCache.InternString("text/plain");
    public static readonly string ContentTypeHtml = LazyCache.InternString("text/html");
    public static readonly string ContentTypeXml = LazyCache.InternString("application/xml");
    
    // 캐시 관련 문자열들
    public static readonly string CacheHit = LazyCache.InternString("HIT");
    public static readonly string CacheMiss = LazyCache.InternString("MISS");
    public static readonly string CacheExpired = LazyCache.InternString("EXPIRED");
    public static readonly string CacheDisabled = LazyCache.InternString("DISABLED");
    
    // 우선순위 레벨들
    public static readonly string PriorityHigh = LazyCache.InternString("high");
    public static readonly string PriorityMedium = LazyCache.InternString("medium");
    public static readonly string PriorityLow = LazyCache.InternString("low");
    public static readonly string PriorityCritical = LazyCache.InternString("critical");
    
    // 상태값들
    public static readonly string StatusPending = LazyCache.InternString("pending");
    public static readonly string StatusCompleted = LazyCache.InternString("completed");
    public static readonly string StatusInProgress = LazyCache.InternString("in_progress");
    public static readonly string StatusFailed = LazyCache.InternString("failed");
    public static readonly string StatusSuccess = LazyCache.InternString("success");
    
    // 메트릭 관련 문자열들
    public static readonly string MetricGet = LazyCache.InternString("get");
    public static readonly string MetricSet = LazyCache.InternString("set");
    public static readonly string MetricHit = LazyCache.InternString("hit");
    public static readonly string MetricMiss = LazyCache.InternString("miss");
    public static readonly string MetricMiddleware = LazyCache.InternString("middleware");
    public static readonly string MetricController = LazyCache.InternString("controller");
    
    // 일반적인 문자열들
    public static readonly string Unknown = LazyCache.InternString("unknown");
    public static readonly string Default = LazyCache.InternString("default");
    public static readonly string Empty = LazyCache.InternString("");
    public static readonly string Separator = LazyCache.InternString(":");
    public static readonly string KeySeparator = LazyCache.InternString("_");
    
    // 컨트롤러 접미사
    public static readonly string ControllerSuffix = LazyCache.InternString("Controller");
    
    // 헤더 이름들
    public static readonly string HeaderCacheControl = LazyCache.InternString("Cache-Control");
    public static readonly string HeaderExpires = LazyCache.InternString("Expires");
    public static readonly string HeaderETag = LazyCache.InternString("ETag");
    public static readonly string HeaderLastModified = LazyCache.InternString("Last-Modified");
    public static readonly string HeaderAuthorization = LazyCache.InternString("Authorization");
    public static readonly string HeaderContentType = LazyCache.InternString("Content-Type");
    
    // 자주 사용되는 숫자들 (문자열로)
    public static readonly string Zero = LazyCache.IntToString(0);
    public static readonly string One = LazyCache.IntToString(1);
    public static readonly string Ten = LazyCache.IntToString(10);
    public static readonly string Hundred = LazyCache.IntToString(100);
    public static readonly string Thousand = LazyCache.IntToString(1000);
    
    // 자주 사용되는 백분율들
    public static readonly string Percent0 = LazyCache.FormatPercentage(0.0);
    public static readonly string Percent10 = LazyCache.FormatPercentage(0.1);
    public static readonly string Percent50 = LazyCache.FormatPercentage(0.5);
    public static readonly string Percent70 = LazyCache.FormatPercentage(0.7);
    public static readonly string Percent80 = LazyCache.FormatPercentage(0.8);
    public static readonly string Percent90 = LazyCache.FormatPercentage(0.9);
    public static readonly string Percent100 = LazyCache.FormatPercentage(1.0);
    
    // 자주 사용되는 바이트 크기들
    public static readonly string Size0B = LazyCache.FormatByteSize(0);
    public static readonly string Size1KB = LazyCache.FormatByteSize(1024);
    public static readonly string Size1MB = LazyCache.FormatByteSize(1024 * 1024);
    public static readonly string Size1GB = LazyCache.FormatByteSize(1024L * 1024 * 1024);
    
    /// <summary>
    /// 자주 사용되는 백분율 값을 캐시에서 가져오기
    /// </summary>
    public static string GetCachedPercentage(double ratio)
    {
        return ratio switch
        {
            0.0 => Percent0,
            0.1 => Percent10,
            0.5 => Percent50,
            0.7 => Percent70,
            0.8 => Percent80,
            0.9 => Percent90,
            1.0 => Percent100,
            _ => LazyCache.FormatPercentage(ratio)
        };
    }
    
    /// <summary>
    /// 자주 사용되는 정수 값을 캐시에서 가져오기
    /// </summary>
    public static string GetCachedInt(int value)
    {
        return value switch
        {
            0 => Zero,
            1 => One,
            10 => Ten,
            100 => Hundred,
            1000 => Thousand,
            _ => LazyCache.IntToString(value)
        };
    }
}