using Athena.Cache.Core.Middleware;
using Microsoft.AspNetCore.Builder;

namespace Athena.Cache.Core.Extensions;

/// <summary>
/// 애플리케이션에 Athena 캐시 미들웨어 추가
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Athena 캐시 미들웨어 등록
    /// </summary>
    public static IApplicationBuilder UseAthenaCache(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AthenaCacheMiddleware>();
    }
}