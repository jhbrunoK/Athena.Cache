using Athena.Cache.Core.Extensions;
using Athena.Cache.Redis.Extensions;
using Athena.Cache.Sample.Data;
using Athena.Cache.Sample.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Entity Framework 설정
builder.Services.AddDbContext<SampleDbContext>(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        options.UseInMemoryDatabase("SampleDb");
    }
    else
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    }
});

// 샘플 서비스 등록
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IOrderService, OrderService>();

// Athena Cache 설정
if (builder.Environment.IsDevelopment())
{
    // 개발 환경: MemoryCache 사용
    builder.Services.AddAthenaCacheComplete(options =>
    {
        options.Namespace = "SampleApp_DEV";
        options.VersionKey = "v1.0";
        options.DefaultExpirationMinutes = 15;
        options.Logging.LogCacheHitMiss = true;
        options.Logging.LogInvalidation = true;
    });
}
else
{
    // 운영 환경: Redis 사용
    builder.Services.AddAthenaCacheRedisComplete(
        athena =>
        {
            athena.Namespace = "SampleApp_PROD";
            athena.VersionKey = "v1.0";
            athena.DefaultExpirationMinutes = 30;
            athena.Logging.LogCacheHitMiss = true;
            athena.Logging.LogInvalidation = true;
        },
        redis =>
        {
            redis.ConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
            redis.DatabaseId = 1;
            redis.KeyPrefix = "sample";
        });
}

var app = builder.Build();

// 개발 환경에서 샘플 데이터 시드
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    await SeedData.InitializeAsync(context);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
