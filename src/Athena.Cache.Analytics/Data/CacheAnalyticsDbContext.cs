using Microsoft.EntityFrameworkCore;

namespace Athena.Cache.Analytics.Data;

/// <summary>
/// 캐시 분석 데이터베이스 컨텍스트
/// </summary>
public class CacheAnalyticsDbContext(DbContextOptions<CacheAnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<CacheEventEntity> CacheEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CacheEventEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.CacheKey);
            entity.HasIndex(e => e.EndpointPath);
            entity.HasIndex(e => new { e.Timestamp, e.EventType });

            entity.Property(e => e.CacheKey).HasMaxLength(500);
            entity.Property(e => e.EndpointPath).HasMaxLength(500);
            entity.Property(e => e.HttpMethod).HasMaxLength(10);
            entity.Property(e => e.UserId).HasMaxLength(100);
            entity.Property(e => e.SessionId).HasMaxLength(100);
        });
    }
}