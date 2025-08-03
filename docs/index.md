---
layout: default
title: Home
---

# 🏛️ Athena.Cache

**Smart caching library for ASP.NET Core with automatic query parameter key generation and table-based cache invalidation.**

Athena.Cache는 ASP.NET Core 애플리케이션을 위한 지능형 캐싱 라이브러리입니다. 어트리뷰트 기반의 선언적 캐싱과 자동 무효화를 통해 개발자가 캐시 관리에 신경 쓰지 않고도 높은 성능을 얻을 수 있습니다.

[![CI](https://github.com/jhbrunoK/Athena.Cache/actions/workflows/ci.yml/badge.svg)](https://github.com/jhbrunoK/Athena.Cache/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/jhbrunoK/Athena.Cache/graph/badge.svg)](https://codecov.io/gh/jhbrunoK/Athena.Cache)
[![NuGet Core](https://img.shields.io/nuget/v/Athena.Cache.Core.svg)](https://www.nuget.org/packages/Athena.Cache.Core/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## ⚡ Quick Start

### 1. Install Package

```bash
dotnet add package Athena.Cache.Core
```

### 2. Configure Services

```csharp
// Program.cs
builder.Services.AddAthenaCacheComplete(options => {
    options.Namespace = "MyApp";
    options.DefaultExpirationMinutes = 30;
});

var app = builder.Build();

app.UseRouting();
app.UseAthenaCache();  // Add after routing
app.MapControllers();
```

### 3. Use in Controllers

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    [CacheInvalidateOn("Users")]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
    {
        // This will be automatically cached
        // Cache will be invalidated when Users table changes
        return Ok(await _userService.GetUsersAsync());
    }
}
```

**That's it!** Your API responses are now cached automatically.

## ✨ Key Features

<div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 1.5rem; margin: 2rem 0;">

<div style="border: 1px solid #e1e8ed; border-radius: 8px; padding: 1.5rem; background: #f8f9fa;">
<h3 style="margin-top: 0; color: #2c3e50;"><i class="fas fa-magic" style="color: #3498db;"></i> Automatic Caching</h3>
<p>Simply add <code>[AthenaCache]</code> to your controller actions. Cache keys are automatically generated from method parameters.</p>
</div>

<div style="border: 1px solid #e1e8ed; border-radius: 8px; padding: 1.5rem; background: #f8f9fa;">
<h3 style="margin-top: 0; color: #2c3e50;"><i class="fas fa-sync-alt" style="color: #e74c3c;"></i> Smart Invalidation</h3>
<p>Use <code>[CacheInvalidateOn("TableName")]</code> to automatically invalidate cache when database tables change.</p>
</div>

<div style="border: 1px solid #e1e8ed; border-radius: 8px; padding: 1.5rem; background: #f8f9fa;">
<h3 style="margin-top: 0; color: #2c3e50;"><i class="fas fa-bolt" style="color: #f39c12;"></i> Zero Memory</h3>
<p>Advanced memory optimization techniques reduce allocations by up to 98% for high-performance applications.</p>
</div>

<div style="border: 1px solid #e1e8ed; border-radius: 8px; padding: 1.5rem; background: #f8f9fa;">
<h3 style="margin-top: 0; color: #2c3e50;"><i class="fas fa-network-wired" style="color: #9b59b6;"></i> Distributed Ready</h3>
<p>Seamless Redis integration for distributed caching across multiple application instances.</p>
</div>

<div style="border: 1px solid #e1e8ed; border-radius: 8px; padding: 1.5rem; background: #f8f9fa;">
<h3 style="margin-top: 0; color: #2c3e50;"><i class="fas fa-code" style="color: #27ae60;"></i> Source Generator</h3>
<p>Compile-time optimizations eliminate reflection overhead and enable AOT compilation support.</p>
</div>

<div style="border: 1px solid #e1e8ed; border-radius: 8px; padding: 1.5rem; background: #f8f9fa;">
<h3 style="margin-top: 0; color: #2c3e50;"><i class="fas fa-chart-line" style="color: #16a085;"></i> Built-in Monitoring</h3>
<p>Real-time dashboards, performance metrics, and analytics to optimize your cache performance.</p>
</div>

</div>

## 📦 Package Ecosystem

Athena.Cache는 모듈식 패키지 시스템으로 필요한 기능만 선택해서 사용할 수 있습니다.

| Package | Description | NuGet |
|---------|-------------|-------|
| **[Athena.Cache.Core](https://www.nuget.org/packages/Athena.Cache.Core/)** | 기본 캐싱 기능 (MemoryCache) | [![NuGet](https://img.shields.io/nuget/v/Athena.Cache.Core.svg)](https://www.nuget.org/packages/Athena.Cache.Core/) |
| **[Athena.Cache.Redis](https://www.nuget.org/packages/Athena.Cache.Redis/)** | Redis 분산 캐싱 지원 | [![NuGet](https://img.shields.io/nuget/v/Athena.Cache.Redis.svg)](https://www.nuget.org/packages/Athena.Cache.Redis/) |
| **[Athena.Cache.Monitoring](https://www.nuget.org/packages/Athena.Cache.Monitoring/)** | 실시간 모니터링 및 알림 | [![NuGet](https://img.shields.io/nuget/v/Athena.Cache.Monitoring.svg)](https://www.nuget.org/packages/Athena.Cache.Monitoring/) |
| **[Athena.Cache.Analytics](https://www.nuget.org/packages/Athena.Cache.Analytics/)** | 고급 분석 및 인사이트 | [![NuGet](https://img.shields.io/nuget/v/Athena.Cache.Analytics.svg)](https://www.nuget.org/packages/Athena.Cache.Analytics/) |
| **[Athena.Cache.SourceGenerator](https://www.nuget.org/packages/Athena.Cache.SourceGenerator/)** | 컴파일 타임 최적화 | [![NuGet](https://img.shields.io/nuget/v/Athena.Cache.SourceGenerator.svg)](https://www.nuget.org/packages/Athena.Cache.SourceGenerator/) |

## 🚀 Get Started

<div style="text-align: center; margin: 3rem 0;">
    <a href="{{ site.baseurl }}/getting-started/installation" style="display: inline-block; background-color: #3498db; color: white; padding: 1rem 2rem; text-decoration: none; border-radius: 5px; font-weight: 600; margin: 0.5rem;">
        <i class="fas fa-download"></i> Installation Guide
    </a>
    <a href="{{ site.baseurl }}/getting-started/basics" style="display: inline-block; background-color: #27ae60; color: white; padding: 1rem 2rem; text-decoration: none; border-radius: 5px; font-weight: 600; margin: 0.5rem;">
        <i class="fas fa-book"></i> Learn the Basics
    </a>
    <a href="{{ site.baseurl }}/fundamentals/cache-key-generation" style="display: inline-block; background-color: #e74c3c; color: white; padding: 1rem 2rem; text-decoration: none; border-radius: 5px; font-weight: 600; margin: 0.5rem;">
        <i class="fas fa-key"></i> Core Features
    </a>
</div>

## 🎯 Why Athena.Cache?

### Before Athena.Cache
```csharp
public async Task<UserDto> GetUser(int id)
{
    var cacheKey = $"user_{id}";
    
    if (_cache.TryGetValue(cacheKey, out UserDto cached))
        return cached;
    
    var user = await _userService.GetUserAsync(id);
    
    _cache.Set(cacheKey, user, TimeSpan.FromMinutes(30));
    
    return user;
}

// When user is updated, you need to remember to invalidate cache
public async Task UpdateUser(UserDto user)
{
    await _userService.UpdateUserAsync(user);
    
    // Don't forget this! (but you probably will)
    _cache.Remove($"user_{user.Id}");
    _cache.Remove("users_list");
    // ... and many more related caches
}
```

### With Athena.Cache
```csharp
[HttpGet("{id}")]
[AthenaCache(ExpirationMinutes = 30)]
public async Task<UserDto> GetUser(int id)
{
    return await _userService.GetUserAsync(id);
}

[HttpPut("{id}")]
[CacheInvalidateOn("Users")]  // Automatically invalidates all related caches
public async Task UpdateUser(int id, [FromBody] UserDto user)
{
    await _userService.UpdateUserAsync(user);
}
```

**그게 전부입니다!** 캐시 키 생성, 저장, 무효화가 모두 자동으로 처리됩니다.

## 🌟 Community & Support

- **📖 Documentation**: [Comprehensive guides and API reference]({{ site.baseurl }}/getting-started/basics)
- **🐛 Issues**: [Report bugs and request features](https://github.com/jhbrunoK/Athena.Cache/issues)
- **💬 Discussions**: [Ask questions and share experiences](https://github.com/jhbrunoK/Athena.Cache/discussions)
- **📊 Examples**: [Sample applications and use cases](https://github.com/jhbrunoK/Athena.Cache/tree/main/samples)

## 📄 License

Athena.Cache is open source software licensed under the [MIT License](https://github.com/jhbrunoK/Athena.Cache/blob/main/LICENSE.txt).

---

<div style="text-align: center; margin-top: 3rem; padding-top: 2rem; border-top: 1px solid #e1e8ed; color: #6c757d;">
    <p>Made with ❤️ by <a href="https://github.com/jhbrunoK" style="color: #3498db;">Bruno Kim</a></p>
    <p>⭐ Star us on <a href="https://github.com/jhbrunoK/Athena.Cache" style="color: #3498db;">GitHub</a> if you find Athena.Cache useful!</p>
</div>