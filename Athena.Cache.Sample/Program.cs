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

// Entity Framework ����
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

// ���� ���� ���
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IOrderService, OrderService>();

// Athena Cache ����
if (builder.Environment.IsDevelopment())
{
    // ���� ȯ��: MemoryCache ���
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
    // � ȯ��: Redis ���
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

// ���� ȯ�濡�� ���� ������ �õ�
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
