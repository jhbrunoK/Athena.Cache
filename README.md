# 🏛️ Athena.Cache

[![CI](https://github.com/jhbrunoK/Athena.Cache/actions/workflows/ci.yml/badge.svg)](https://github.com/jhbrunoK/Athena.Cache/actions/workflows/ci.yml)
[![NuGet Core](https://img.shields.io/nuget/v/Athena.Cache.Core.svg)](https://www.nuget.org/packages/Athena.Cache.Core/)
[![Downloads](https://img.shields.io/nuget/dt/Athena.Cache.Core.svg)](https://www.nuget.org/packages/Athena.Cache.Core/)
[![NuGet Redis](https://img.shields.io/nuget/v/Athena.Cache.Redis.svg)](https://www.nuget.org/packages/Athena.Cache.Redis/)
[![Downloads](https://img.shields.io/nuget/dt/Athena.Cache.Core.svg)](https://www.nuget.org/packages/Athena.Cache.Redis/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**Smart caching library for ASP.NET Core with automatic query parameter key generation and table-based cache invalidation.**

Athena.Cache는 ASP.NET Core 애플리케이션을 위한 지능형 캐싱 라이브러리입니다. 쿼리 파라미터를 자동으로 캐시 키로 변환하고, 데이터베이스 테이블 변경 시 관련 캐시를 자동으로 무효화합니다.

## ✨ 주요 기능

    - 🔑 **자동 캐시 키 생성**: 쿼리 파라미터 → SHA256 해시 키 자동 변환
    - 🗂️ **테이블 기반 무효화**: 데이터베이스 테이블 변경 시 관련 캐시 자동 삭제
    - 🚀 **다중 백엔드 지원**: MemoryCache, Redis, Valkey 지원
    - 🎯 **선언적 캐싱**: `[AthenaCache]`, `[CacheInvalidateOn]` 어트리뷰트
    - ⚡ **고성능**: 대용량 트래픽 환경에 최적화
    - 🔧 **쉬운 통합**: 미들웨어와 액션 필터로 간단한 설정
    - 🧪 **완전한 테스트**: 포괄적인 단위 및 통합 테스트

## 🚀 빠른 시작

### 설치

```bash
# 기본 패키지 (MemoryCache 포함)
    dotnet add package Athena.Cache.Core

# Redis 지원
    dotnet add package Athena.Cache.Redis
```

### 기본 설정

```csharp
// Program.cs
using Athena.Cache.Core.Extensions;

// 개발 환경 (MemoryCache)
services.AddAthenaCacheComplete(options => {
    options.Namespace = "MyApp_DEV";
    options.DefaultExpirationMinutes = 30;
});

// 운영 환경 (Redis)
services.AddAthenaCacheRedisComplete(
    athena => {
        athena.Namespace = "MyApp_PROD";
        athena.DefaultExpirationMinutes = 60;
    },
    redis => {
        redis.ConnectionString = "localhost:6379";
        redis.DatabaseId = 1;
    });

// 미들웨어 추가
app.UseAthenaCache();
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
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1)
    {
        // 비즈니스 로직
        // 쿼리 파라미터로부터 캐시 키 자동 생성됨
        var users = await _userService.GetUsersAsync(search, page);
        return Ok(users);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] User user)
    {
        var createdUser = await _userService.CreateUserAsync(user);
        // Users 테이블 관련 캐시 자동 무효화됨
        return CreatedAtAction(nameof(GetUser), new { id = createdUser.Id }, createdUser);
    }

    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 60)]
    [CacheInvalidateOn("Users")]
    [CacheInvalidateOn("Orders", InvalidationType.Pattern, "User_*")]
    public async Task<IActionResult> GetUser(int id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        if (user == null) return NotFound();
        return Ok(user);
    }
}
```

## 🛠️ 고급 기능

### 커스텀 캐시 키
```csharp
[AthenaCache(
    ExpirationMinutes = 45,
    CustomKeyPrefix = "UserStats",
    ExcludeParameters = new[] { "debug", "trace" }
)]
public async Task<IActionResult> GetUserStatistics(int userId, bool debug = false)
{
    // debug 파라미터는 캐시 키 생성에서 제외됨
    return Ok(stats);
}
```

### 패턴 기반 무효화
```csharp
[CacheInvalidateOn("Users", InvalidationType.All)]                      // 모든 Users 관련 캐시
[CacheInvalidateOn("Orders", InvalidationType.Pattern, "User_*")]       // User_* 패턴 캐시
[CacheInvalidateOn("Products", InvalidationType.Related, "Categories")] // 연관 테이블까지
public async Task<IActionResult> GetUserOrders(int userId) { ... }
```

### 수동 캐시 관리
```csharp
public class UserService
{
    private readonly IAthenaCache _cache;
    private readonly ICacheInvalidator _invalidator;

    public UserService(IAthenaCache cache, ICacheInvalidator invalidator)
    {
        _cache = cache;
        _invalidator = invalidator;
    }

    public async Task InvalidateUserCaches(int userId)
    {
        // 특정 사용자 관련 캐시만 삭제
        await _invalidator.InvalidateByPatternAsync($"User_{userId}_*");
    }

    public async Task<CacheStatistics> GetCacheStats()
    {
        return await _cache.GetStatisticsAsync();
    }
}
```

## 📊 성능

- **높은 처리량**: Redis 기준 10,000+ requests/second
- **낮은 지연시간**: 캐시 키 생성 1ms 미만
- **메모리 효율성**: 최적화된 직렬화 및 압축
- **확장 가능**: 다중 인스턴스 분산 무효화 지원

## 🔧 설정 옵션

### 전역 설정
```csharp
services.AddAthenaCacheComplete(options => {
    options.Namespace = "MyApp";              // 네임스페이스 (환경 분리)
    options.VersionKey = "v1.0";              // 버전 키 (배포 시 캐시 분리)
    options.DefaultExpirationMinutes = 30;    // 기본 만료 시간
    options.MaxRelatedDepth = 3;              // 연쇄 무효화 최대 깊이
    options.StartupCacheCleanup = CleanupMode.ExpireShorten; // 시작 시 정리 방식
    
    // 로깅 설정
    options.Logging.LogCacheHitMiss = true;   // 히트/미스 로깅
    options.Logging.LogInvalidation = true;   // 무효화 로깅
    
    // 에러 처리
    options.ErrorHandling.SilentFallback = true; // 조용한 폴백
});
```

### Redis 설정
```csharp
services.AddAthenaCacheRedisComplete(
    athena => { /* Athena 설정 */ },
    redis => {
        redis.ConnectionString = "localhost:6379";
        redis.DatabaseId = 1;
        redis.KeyPrefix = "MyApp";
        redis.BatchSize = 1000;
        redis.ConnectTimeoutSeconds = 5;
        redis.RetryCount = 3;
    });
```

## 🧪 테스트

```bash
# 모든 테스트 실행
dotnet test

# 커버리지 포함
dotnet test --collect:"XPlat Code Coverage"

# 통합 테스트 (Redis 필요)
docker run -d -p 6379:6379 redis:7-alpine
dotnet test --filter Category=Integration
```

## 📖 추가 문서

- [설치 가이드](docs/installation.md)
- [설정 방법](docs/configuration.md)
- [캐시 무효화](docs/invalidation.md)
- [성능 최적화](docs/performance.md)
- [API 레퍼런스](docs/api-reference.md)
- [예제 모음](samples/)

## 🏗️ 아키텍처

### 핵심 컴포넌트
- **ICacheKeyGenerator**: 쿼리 파라미터 → 캐시 키 변환
- **ICacheInvalidator**: 테이블 기반 캐시 무효화 관리
- **IAthenaCache**: 캐시 제공자 추상화 (Memory/Redis/Valkey)
- **AthenaCacheMiddleware**: HTTP 요청 가로채기 및 캐싱
- **AthenaCacheActionFilter**: 어트리뷰트 메타데이터 수집

### 동작 원리
1. **요청 가로채기**: 미들웨어가 GET 요청을 가로챔
2. **키 생성**: 쿼리 파라미터를 정렬하여 SHA256 해시 생성
3. **캐시 확인**: 생성된 키로 캐시에서 응답 조회
4. **응답 캐싱**: 캐시 미스 시 응답을 캐시에 저장
5. **무효화**: 테이블 변경 시 관련 캐시 자동 삭제

## 🔄 캐시 무효화 전략

### 1. 즉시 무효화 (Immediate)
```csharp
[CacheInvalidateOn("Users", InvalidationType.All)]
```

### 2. 패턴 무효화 (Pattern-based)
```csharp
[CacheInvalidateOn("Users", InvalidationType.Pattern, "User_*")]
```

### 3. 연관 무효화 (Related)
```csharp
[CacheInvalidateOn("Users", InvalidationType.Related, "Orders", "Profiles")]
```

## 🤝 기여하기

1. 저장소 포크
2. 기능 브랜치 생성 (`git checkout -b feature/amazing-feature`)
3. 변경사항 커밋 (`git commit -m 'Add amazing feature'`)
4. 브랜치에 푸시 (`git push origin feature/amazing-feature`)
5. Pull Request 오픈

### 개발 환경 설정
```bash
git clone https://github.com/jhbrunoK/Athena.Cache.git
cd Athena.Cache
dotnet restore
dotnet build
dotnet test
```

## 📝 라이센스

이 프로젝트는 MIT 라이센스 하에 배포됩니다. 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.

## 🙏 감사의 말

- 전략과 지혜의 여신 아테나에서 영감을 받았습니다
- ASP.NET Core 커뮤니티를 위해 제작되었습니다
- 모든 기여자분들께 감사드립니다

## 📞 지원 및 문의

- 🐛 **버그 리포트**: [GitHub Issues](https://github.com/jhbrunoK/Athena.Cache/issues)
- 💡 **기능 요청**: [GitHub Discussions](https://github.com/jhbrunoK/Athena.Cache/discussions)
- 📧 **이메일**: bobhappy2000@gmail.com

---

**❤️ 고성능 ASP.NET Core 애플리케이션을 위해 제작되었습니다**
