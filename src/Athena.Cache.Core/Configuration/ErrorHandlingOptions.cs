namespace Athena.Cache.Core.Configuration;

/// <summary>
/// 에러 처리 설정
/// </summary>
public class ErrorHandlingOptions
{
    /// <summary>캐시 에러 시 조용히 넘어가기</summary>
    public bool SilentFallback { get; set; } = true;

    /// <summary>커스텀 에러 핸들러</summary>
    public Func<Exception, Task>? CustomErrorHandler { get; set; }
}