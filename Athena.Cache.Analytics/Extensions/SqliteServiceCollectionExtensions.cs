using Athena.Cache.Analytics.Abstractions;
using Athena.Cache.Analytics.Data;
using Athena.Cache.Analytics.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Athena.Cache.Analytics.Extensions;

/// <summary>
/// SQLite 기반 캐시 분석 확장 메서드
/// </summary>
public static class SqliteServiceCollectionExtensions
{
    /// <summary>
    /// SQLite 기반 캐시 분석 시스템 등록
    /// </summary>
    public static IServiceCollection AddAthenaCacheAnalyticsSqlite(
        this IServiceCollection services,
        string connectionString,
        Action<CacheAnalyticsOptions>? configure = null)
    {
        var options = new CacheAnalyticsOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        // SQLite DbContext 등록
        services.AddDbContext<CacheAnalyticsDbContext>(opt =>
            opt.UseSqlite(connectionString));

        services.AddScoped<SqliteCacheEventCollector>();
        services.AddScoped<ICacheEventCollector>(provider =>
            provider.GetRequiredService<SqliteCacheEventCollector>());
        services.AddScoped<ICacheAnalyticsService, SqliteCacheAnalyticsService>();

        return services;
    }
}