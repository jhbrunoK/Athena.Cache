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

// 비즈니스 서비스 등록
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IOrderService, OrderService>();

// Athena Cache 설정 - Redis 테스트용
//builder.Services.AddAthenaCacheRedisComplete(
//    athena =>
//    {
//        athena.Namespace = "SampleApp_REDIS_TEST";
//        athena.VersionKey = "v1.0";
//        athena.DefaultExpirationMinutes = 30;
//        athena.Logging.LogCacheHitMiss = true;
//        athena.Logging.LogInvalidation = true;
//        athena.Logging.LogKeyGeneration = true;
//    },
//    redis =>
//    {
//        redis.ConnectionString = "localhost:6379";
//        redis.DatabaseId = 2;
//        redis.KeyPrefix = "test";
//    });

builder.Services.AddAthenaCacheComplete(
    athena =>
    {
        athena.Namespace = "SampleApp_REDIS_TEST";
        athena.VersionKey = "v1.0";
        athena.DefaultExpirationMinutes = 30;
        athena.Logging.LogCacheHitMiss = true;
        athena.Logging.LogInvalidation = true;
        athena.Logging.LogKeyGeneration = true;
    });

var app = builder.Build();

// 개발 환경에서만 데이터 시딩
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

// Athena Cache 미들웨어 추가
app.UseAthenaCache();

app.MapControllers();

app.Run();