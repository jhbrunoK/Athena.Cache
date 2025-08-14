using Athena.Invalidation.Sample.Data;
using Athena.Invalidation.Sample.Services;
using Microsoft.EntityFrameworkCore;
using Athena.Invalidation.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database configuration
builder.Services.AddDbContext<SampleDbContext>(options =>
    options.UseInMemoryDatabase("InMemoryDb"));

// Business services
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IUserService, UserService>();

// Add Athena Invalidation with simple configuration
builder.Services.AddInvalidationEngine(options =>
{
    options.DefaultTimeout = TimeSpan.FromSeconds(30);
    options.DefaultMaxRetries = 3;
});

var app = builder.Build();

// Initialize database with seed data
using (var scope = app.Services.CreateScope())
{
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

app.MapGet("/", () => new
{
    message = "Welcome to Athena Invalidation Sample API",
    version = "1.0.0",
    swagger = "/swagger"
});

app.Run();

