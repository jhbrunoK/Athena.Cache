# 🏛️ Athena.Cache

[![CI](https://github.com/jhbrunoK/Athena.Cache/actions/workflows/ci.yml/badge.svg)](https://github.com/jhbrunoK/Athena.Cache/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/jhbrunoK/Athena.Cache/graph/badge.svg)](https://codecov.io/gh/jhbrunoK/Athena.Cache)
[![NuGet Core](https://img.shields.io/nuget/v/Athena.Cache.Core.svg)](https://www.nuget.org/packages/Athena.Cache.Core/)
[![Downloads](https://img.shields.io/nuget/dt/Athena.Cache.Core.svg)](https://www.nuget.org/packages/Athena.Cache.Core/)
[![NuGet Redis](https://img.shields.io/nuget/v/Athena.Cache.Redis.svg)](https://www.nuget.org/packages/Athena.Cache.Redis/)
[![Downloads](https://img.shields.io/nuget/dt/Athena.Cache.Redis.svg)](https://www.nuget.org/packages/Athena.Cache.Redis/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**Smart caching library for ASP.NET Core with automatic query parameter key generation and table-based cache invalidation.**

Athena.Cache는 ASP.NET Core 애플리케이션을 위한 지능형 캐싱 라이브러리입니다. 쿼리 파라미터를 자동으로 캐시 키로 변환하고, 데이터베이스 테이블 변경 시 관련 캐시를 자동으로 무효화합니다.

## ✨ 주요 기능

- 🎯 **Source Generator**: 컴파일 타임에 캐시 설정 자동 생성, AOT 호환
- 🔑 **자동 캐시 키 생성**: 쿼리 파라미터 → SHA256 해시 키 자동 변환
- 🗂️ **테이블 기반 무효화**: 데이터베이스 테이블 변경 시 관련 캐시 자동 삭제
- 🏗️ **Convention 기반 추론**: 컨트롤러명에서 테이블명 자동 추론, 중복 선언 불필요
- 🚀 **다중 백엔드 지원**: MemoryCache, Redis, Valkey 지원
- 🎨 **선언적 캐싱**: `[AthenaCache]`, `[CacheInvalidateOn]` 어트리뷰트
- ⚡ **고성능**: 대용량 트래픽 환경에 최적화, Primary Constructor 사용
- 🧠 **제로 메모리 할당**: 5단계 최적화로 메모리 할당 90-98% 감소
- 🔄 **자동 메모리 관리**: 실시간 GC 모니터링 및 자동 캐시 정리
- 🔧 **쉬운 통합**: 단일 패키지 설치로 모든 기능 사용 가능
- 🧪 **완전한 테스트**: 포괄적인 단위 및 통합 테스트

## 🚀 빠른 시작

### 설치

```bash
# 기본 패키지 (MemoryCache + Source Generator 포함)
dotnet add package Athena.Cache.Core

# Redis 지원 (선택사항)
dotnet add package Athena.Cache.Redis
```

> **🎯 통합 패키지**: `Athena.Cache.Core`만 설치하면 Source Generator가 자동으로 포함되어 컴파일 타임에 캐시 설정을 자동 생성합니다.

### 기본 설정

```csharp
// Program.cs
using Athena.Cache.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 개발 환경 (MemoryCache)
builder.Services.AddAthenaCacheComplete(options => {
    options.Namespace = "MyApp_DEV";
    options.DefaultExpirationMinutes = 30;
    options.Logging.LogCacheHitMiss = true; // 캐시 히트/미스 로깅
});

// 운영 환경 (Redis)
builder.Services.AddAthenaCacheRedisComplete(
    athena => {
        athena.Namespace = "MyApp_PROD";
        athena.DefaultExpirationMinutes = 60;
    },
    redis => {
        redis.ConnectionString = "localhost:6379";
        redis.DatabaseId = 1;
    });

var app = builder.Build();

// 🔧 미들웨어 추가 (중요: 라우팅 후, 컨트롤러 전에 추가)
app.UseRouting();
app.UseAthenaCache();  // 라우팅 후에 추가
app.MapControllers();

app.Run();
```

### 컨트롤러에서 사용

```csharp
using Athena.Cache.Core.Attributes;
using Athena.Cache.Core.Enums;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    [CacheInvalidateOn("Users")]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1)
    {
        // 🚀 Source Generator가 컴파일 타임에 이 어트리뷰트를 스캔하여
        // 캐시 설정을 자동 생성합니다. 별도 설정 불필요!
        var users = await _userService.GetUsersAsync(search, page);
        return Ok(users);
    }

    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 60)]
    [CacheInvalidateOn("Users")]
    [CacheInvalidateOn("Orders", InvalidationType.Pattern, "User_*")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        if (user == null) return NotFound();
        return Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] User user)
    {
        var createdUser = await _userService.CreateUserAsync(user);
        // Users 테이블 관련 캐시가 자동 무효화됨
        return CreatedAtAction(nameof(GetUser), new { id = createdUser.Id }, createdUser);
    }

    // 캐시 비활성화 예제
    [HttpGet("no-cache")]
    [NoCache]  // 이 액션은 캐싱하지 않음
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsersWithoutCache()
    {
        var users = await _userService.GetUsersAsync();
        return Ok(users);
    }
}
```

### 📊 캐시 상태 확인

Athena.Cache는 HTTP 헤더를 통해 캐시 상태를 확인할 수 있습니다:

```bash
# 첫 번째 요청 (캐시 미스)
curl -v http://localhost:5000/api/users
# 응답 헤더: X-Athena-Cache: MISS

# 두 번째 요청 (캐시 히트)
curl -v http://localhost:5000/api/users  
# 응답 헤더: X-Athena-Cache: HIT
```

## 🎯 Convention 기반 캐싱 (권장)

Convention 기반 캐싱을 사용하면 `CacheInvalidateOn` 어트리뷰트를 반복 선언할 필요 없이, 컨트롤러명에서 테이블명을 자동으로 추론합니다.

### 기본 설정 (Convention 활성화)

```csharp
// Program.cs
builder.Services.AddAthenaCacheComplete(options => {
    options.Namespace = "MyApp";
    options.DefaultExpirationMinutes = 30;
    
    // Convention 기반 테이블명 추론 활성화 (기본값: true)
    options.Convention.Enabled = true;
    options.Convention.UsePluralizer = true; // UsersController → Users
});
```

### 간단한 사용법

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase  // 자동으로 "Users" 테이블과 연결
{
    // ✅ Convention 사용: CacheInvalidateOn 중복 선언 불필요
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
    {
        // 자동으로 Users 테이블 변경 시 캐시 무효화
        return Ok(await _userService.GetUsersAsync());
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] User user)
    {
        // 자동으로 Users 테이블 관련 캐시 무효화
        var createdUser = await _userService.CreateUserAsync(user);
        return CreatedAtAction(nameof(GetUser), new { id = createdUser.Id }, createdUser);
    }

    // 추가 테이블이 필요한 경우에만 명시적 선언
    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 60)]
    [CacheInvalidateOn("Orders", InvalidationType.Pattern, "User_*")] // Users는 자동, Orders만 명시
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        return Ok(await _userService.GetUserByIdAsync(id));
    }
}
```

### 고급 Convention 설정

```csharp
// 다중 테이블 매핑
builder.Services.AddAthenaCacheComplete(options => {
    options.Convention.ControllerTableMappings = new Dictionary<string, string[]>
    {
        ["UsersController"] = ["Users", "UserProfiles"],
        ["OrdersController"] = ["Orders", "OrderItems", "Users"]
    };
    
    // 커스텀 추론 함수
    options.Convention.CustomMultiTableInferrer = controllerName =>
    {
        // UsersOrdersController → ["Users", "Orders"]
        if (controllerName.Contains("Users") && controllerName.Contains("Orders"))
            return ["Users", "Orders"];
            
        return [controllerName.Replace("Controller", "")];
    };
});
```

### Convention vs 명시적 선언 비교

```csharp
// ❌ 기존 방식: 모든 메서드에 중복 선언
public class UsersController : ControllerBase
{
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    [CacheInvalidateOn("Users")]  // 중복
    public async Task<IActionResult> GetUsers() { ... }

    [HttpPost]
    [CacheInvalidateOn("Users")]  // 중복
    public async Task<IActionResult> CreateUser() { ... }

    [HttpPut("{id}")]
    [CacheInvalidateOn("Users")]  // 중복
    public async Task<IActionResult> UpdateUser() { ... }
}

// ✅ Convention 방식: 자동 추론으로 간소화
public class UsersController : ControllerBase
{
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    public async Task<IActionResult> GetUsers() { ... }  // Users 테이블 자동 연결

    [HttpPost]
    public async Task<IActionResult> CreateUser() { ... }  // Users 테이블 자동 무효화

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser() { ... }  // Users 테이블 자동 무효화
}
```

### 마이그레이션 가이드

기존 프로젝트에서 Convention으로 전환하는 방법:

1. **Convention 활성화**
```csharp
options.Convention.Enabled = true;
```

2. **중복 선언 제거** (선택사항)
```csharp
// 제거 가능: 컨트롤러명과 동일한 테이블의 CacheInvalidateOn
[CacheInvalidateOn("Users")] // UsersController에서 제거 가능
```

3. **점진적 마이그레이션**
   - Convention과 명시적 선언이 함께 작동
   - 명시적 선언이 우선 적용되어 기존 코드 영향 없음

### Convention 비활성화

특정 컨트롤러에서 Convention 추론을 비활성화하고 수동으로 캐시를 제어하려는 경우:

#### 방법 1: NoConventionInvalidation 어트리뷰트
```csharp
[ApiController]
[NoConventionInvalidation]  // Convention 추론 비활성화
public class ReportsController : ControllerBase
{
    private readonly ICacheInvalidator _cacheInvalidator;

    [HttpGet]
    [AthenaCache(ExpirationMinutes = 120)]
    public async Task<IActionResult> GetReport()
    {
        // 캐싱만 하고, 자동 무효화 없음
        return Ok(data);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshData()
    {
        // 수동으로 캐시 무효화
        await _cacheInvalidator.InvalidateByPatternAsync("*GetReport*");
        return Ok();
    }
}
```

#### 방법 2: 전역 설정으로 제외
```csharp
builder.Services.AddAthenaCacheComplete(options => {
    options.Convention.Enabled = true;
    options.Convention.ExcludedControllers.Add("ReportsController");
    options.Convention.ExcludedControllers.Add("AnalyticsController");
});
```

#### 방법 3: 전체 Convention 비활성화
```csharp
builder.Services.AddAthenaCacheComplete(options => {
    options.Convention.Enabled = false;  // 모든 컨트롤러에서 비활성화
});
```

### 수동 캐시 제어 패턴
```csharp
public class ManualCacheController : ControllerBase
{
    private readonly ICacheInvalidator _invalidator;

    [HttpGet]
    [AthenaCache]
    [NoConventionInvalidation]
    public async Task<IActionResult> GetData() { ... }

    [HttpPost]
    [NoConventionInvalidation]
    public async Task<IActionResult> UpdateData()
    {
        // 비즈니스 로직 실행
        var result = await _service.UpdateAsync();
        
        // 선택적 캐시 무효화
        if (result.ShouldInvalidateCache)
        {
            await _invalidator.InvalidateByPatternAsync("GetData*");
        }
        
        return Ok(result);
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

### 🚀 기본 성능 지표
- **높은 처리량**: Redis 기준 10,000+ requests/second
- **낮은 지연시간**: 캐시 키 생성 1ms 미만
- **메모리 효율성**: 최적화된 직렬화 및 압축
- **확장 가능**: 다중 인스턴스 분산 무효화 지원

### 🧠 제로 메모리 할당 최적화 결과
| 최적화 영역 | 개선율 | 주요 기법 |
|------------|-------|-----------|
| **메모리 할당** | **90-98% 감소** | 컬렉션 풀링, Span/Memory, 캐싱 |
| **GC 압박** | **~90% 감소** | 값 타입, 문자열 인터닝, 자동 정리 |
| **문자열 처리** | **~98% 감소** | StringBuilder 풀링, 약한 참조 인터닝 |
| **컬렉션 연산** | **~95% 감소** | LINQ 제거, 수동 루프, ArrayPool |
| **박싱/언박싱** | **100% 제거** | 값 타입 구조체, 제네릭 통계 |

### 📈 성능 테스트 데모
```bash
# 성능 비교 테스트 실행
curl -X POST "http://localhost:5000/api/ZeroMemoryDemo/performance-comparison?iterations=10000"

# 메모리 상태 확인
curl "http://localhost:5000/api/ZeroMemoryDemo/memory-status"

# 메모리 압박 시뮬레이션
curl -X POST "http://localhost:5000/api/ZeroMemoryDemo/memory-pressure-test"
```

> 📋 **상세 가이드**: 제로 메모리 최적화 기법에 대한 자세한 설명은 [docs/ZeroMemoryOptimization.md](docs/ZeroMemoryOptimization.md)를 참조하세요.

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

## 🏗️ 아키텍처

### 핵심 컴포넌트
- **🎯 Source Generator**: 컴파일 타임에 Controller 어트리뷰트를 스캔하여 캐시 설정 자동 생성
- **ICacheConfigurationRegistry**: 생성된 캐시 설정을 관리하는 레지스트리
- **ICacheKeyGenerator**: 쿼리 파라미터 → 캐시 키 변환
- **ICacheInvalidator**: 테이블 기반 캐시 무효화 관리
- **IAthenaCache**: 캐시 제공자 추상화 (Memory/Redis/Valkey)
- **AthenaCacheMiddleware**: HTTP 요청 가로채기 및 캐싱 (Primary Constructor 사용)

### 동작 원리
1. **🔧 컴파일 타임**: Source Generator가 Controller를 스캔하여 캐시 설정 클래스 자동 생성
2. **🚀 런타임 초기화**: 생성된 설정이 있으면 사용, 없으면 Reflection 백업 사용
3. **📡 요청 가로채기**: 미들웨어가 GET 요청을 라우팅 정보와 함께 가로챔
4. **🔑 키 생성**: 쿼리 파라미터를 정렬하여 SHA256 해시 생성
5. **💾 캐시 확인**: 생성된 키로 캐시에서 응답 조회
6. **📦 응답 캐싱**: 캐시 미스 시 응답을 캐시에 저장 후 테이블 추적 등록
7. **🔄 무효화**: 테이블 변경 시 관련 캐시 자동 삭제

### 🎯 Source Generator 장점
- **⚡ 성능**: 런타임 Reflection 불필요, 컴파일 타임 최적화
- **🛡️ 타입 안전성**: 컴파일 타임에 어트리뷰트 검증
- **🚀 AOT 호환**: Native AOT 배포 지원
- **🔧 자동 관리**: 별도 설정 파일 불필요, 어트리뷰트만으로 완성

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

## 📦 **추가 패키지**

```bash
# Source Generator (선택적 - Core에 포함되지만 독립 설치 가능)
dotnet add package Athena.Cache.SourceGenerator

# 분석 및 모니터링 (선택적)
dotnet add package Athena.Cache.Analytics
```

> **📋 릴리즈 정보**: 자세한 릴리즈 프로세스와 버전 관리 전략은 [RELEASE.md](RELEASE.md)를 참조하세요.

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
