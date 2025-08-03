# 🏛️ Athena.Cache

[![CI](https://github.com/jhbrunoK/Athena.Cache/actions/workflows/ci.yml/badge.svg)](https://github.com/jhbrunoK/Athena.Cache/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/jhbrunoK/Athena.Cache/graph/badge.svg)](https://codecov.io/gh/jhbrunoK/Athena.Cache)
[![NuGet Core](https://img.shields.io/nuget/v/Athena.Cache.Core.svg)](https://www.nuget.org/packages/Athena.Cache.Core/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**Smart caching library for ASP.NET Core with automatic query parameter key generation and table-based cache invalidation.**

Athena.Cache는 ASP.NET Core 애플리케이션을 위한 지능형 캐싱 라이브러리입니다. 어트리뷰트를 통한 선언적 캐싱과 자동 무효화를 제공합니다.

## 🚀 빠른 시작

### 설치

```bash
# 기본 패키지 (MemoryCache)
dotnet add package Athena.Cache.Core

# Redis 지원 (선택사항)
dotnet add package Athena.Cache.Redis

# Source Generator (컴파일 타임 최적화)
dotnet add package Athena.Cache.SourceGenerator
```

### 기본 설정

```csharp
// Program.cs
using Athena.Cache.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 캐시 서비스 추가
builder.Services.AddAthenaCacheComplete(options => {
    options.Namespace = "MyApp";
    options.DefaultExpirationMinutes = 30;
});

var app = builder.Build();

// 미들웨어 추가 (라우팅 후, 컨트롤러 전에)
app.UseRouting();
app.UseAthenaCache();
app.MapControllers();

app.Run();
```

### 컨트롤러에서 사용

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
        // 자동으로 캐시되고, Users 테이블 변경 시 무효화됨
        return Ok(await _userService.GetUsersAsync());
    }

    [HttpPost]
    [CacheInvalidateOn("Users")]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] User user)
    {
        // Users 테이블 관련 캐시가 자동 무효화됨
        return Ok(await _userService.CreateUserAsync(user));
    }
}
```

## 📦 패키지 구성

| 패키지 | 설명 | NuGet |
|--------|------|-------|
| **[Athena.Cache.Core](https://www.nuget.org/packages/Athena.Cache.Core/)** | 기본 캐싱 기능 (MemoryCache) | [![NuGet](https://img.shields.io/nuget/v/Athena.Cache.Core.svg)](https://www.nuget.org/packages/Athena.Cache.Core/) |
| **[Athena.Cache.Redis](https://www.nuget.org/packages/Athena.Cache.Redis/)** | Redis 분산 캐싱 지원 | [![NuGet](https://img.shields.io/nuget/v/Athena.Cache.Redis.svg)](https://www.nuget.org/packages/Athena.Cache.Redis/) |
| **[Athena.Cache.Monitoring](https://www.nuget.org/packages/Athena.Cache.Monitoring/)** | 실시간 모니터링 및 알림 | [![NuGet](https://img.shields.io/nuget/v/Athena.Cache.Monitoring.svg)](https://www.nuget.org/packages/Athena.Cache.Monitoring/) |
| **[Athena.Cache.Analytics](https://www.nuget.org/packages/Athena.Cache.Analytics/)** | 고급 분석 및 인사이트 | [![NuGet](https://img.shields.io/nuget/v/Athena.Cache.Analytics.svg)](https://www.nuget.org/packages/Athena.Cache.Analytics/) |
| **[Athena.Cache.SourceGenerator](https://www.nuget.org/packages/Athena.Cache.SourceGenerator/)** | 컴파일 타임 최적화 | [![NuGet](https://img.shields.io/nuget/v/Athena.Cache.SourceGenerator.svg)](https://www.nuget.org/packages/Athena.Cache.SourceGenerator/) |

> 각 패키지의 상세한 사용법과 고급 기능은 해당 패키지의 README를 참고하세요.

## ✨ 주요 기능

- 🎯 **어트리뷰트 기반 캐싱**: `[AthenaCache]`로 간단한 캐시 설정
- 🗂️ **자동 무효화**: `[CacheInvalidateOn]`으로 테이블 기반 캐시 무효화
- 🔑 **자동 키 생성**: 쿼리 파라미터에서 캐시 키 자동 생성
- 🚀 **다중 백엔드**: MemoryCache, Redis, Valkey 지원
- ⚡ **고성능**: 메모리 최적화 및 제로 할당 구현

## 🔧 Redis 설정

```csharp
// Redis 사용 시
builder.Services.AddAthenaCacheRedisComplete(
    athena => {
        athena.Namespace = "MyApp_PROD";
        athena.DefaultExpirationMinutes = 60;
    },
    redis => {
        redis.ConnectionString = "localhost:6379";
        redis.DatabaseId = 1;
    });
```

## 📄 라이선스

이 프로젝트는 MIT 라이선스 하에 배포됩니다. [LICENSE](LICENSE.txt) 파일을 참고하세요.

## 🐛 이슈 및 지원

- **Repository**: [https://github.com/jhbrunoK/Athena.Cache](https://github.com/jhbrunoK/Athena.Cache)
- **Issues**: [https://github.com/jhbrunoK/Athena.Cache/issues](https://github.com/jhbrunoK/Athena.Cache/issues)