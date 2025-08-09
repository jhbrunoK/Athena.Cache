# 🔗 Athena.Cache.FusionCache

**FusionCache integration adapter for Athena.Cache library**

Athena.Cache.FusionCache는 FusionCache의 강력한 기능들과 Athena.Cache의 고급 무효화 및 키 관리 시스템을 결합한 통합 패키지입니다.

## ✨ 주요 기능

### FusionCache의 강력한 기능
- 🛡️ **Fail-Safe 메커니즘**: 일시적 장애 시 만료된 캐시를 재사용
- ⚡ **Circuit Breaker**: 연속 실패 시 자동 차단 및 복구
- 🏗️ **Hybrid L1+L2 캐싱**: 메모리 + 분산 캐시 계층
- 🔄 **Cache Stampede 방지**: 동시 요청 최적화
- 🏷️ **태그 기반 무효화**: 그룹별 캐시 관리

### Athena.Cache의 고유 기능
- 🎯 **어트리뷰트 기반 캐싱**: 선언적 캐시 설정
- 🔑 **정교한 키 맹글링**: 고성능 해시 기반 키 생성
- 📊 **테이블 기반 무효화**: 데이터 의존성 자동 관리
- 🚀 **제로 할당 최적화**: 메모리 효율성

## 🚀 빠른 시작

### 설치

```bash
# FusionCache 통합 패키지
dotnet add package Athena.Cache.FusionCache

# 기본 패키지 (자동으로 포함됨)
# dotnet add package Athena.Cache.Core
```

### 기본 설정

```csharp
// Program.cs
using Athena.Cache.FusionCache.Extensions;

var builder = WebApplication.CreateBuilder(args);

// FusionCache + Athena.Cache 통합 설정
builder.Services.AddAthenaCacheFusionComplete(
    athena => {
        athena.Namespace = "MyApp";
        athena.DefaultExpirationMinutes = 30;
        athena.Logging.LogCacheHitMiss = true;
    },
    fusion => {
        fusion.WithOptions(options => {
            options.DefaultEntryOptions.Duration = TimeSpan.FromMinutes(30);
        });
        fusion.WithFailSafe(TimeSpan.FromHours(1));
        fusion.WithCircuitBreaker(TimeSpan.FromMinutes(2));
    }
);

// 태그 헬퍼 서비스 추가
builder.Services.AddAthenaCacheTaggedInvalidation();

var app = builder.Build();

// 미들웨어 추가
app.UseAthenaCache();
app.MapControllers();

app.Run();
```

## 📖 사용 방법

### 1. 어트리뷰트 기반 캐싱 (기존 방식 유지)

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 15)]
    [CacheInvalidateOn("Users")]
    public async Task<UserDto> GetUser(int id)
    {
        // FusionCache가 백엔드로 자동 사용됨
        // Fail-Safe, Circuit Breaker 등 고급 기능 자동 적용
        return await _userService.GetUserByIdAsync(id);
    }
}
```

### 2. FusionCache GetOrSet 패턴 직접 사용

```csharp
public class ProductController : ControllerBase
{
    private readonly IFusionCache _fusionCache;

    [HttpGet("{id}")]
    public async Task<ProductDto> GetProduct(int id)
    {
        var cacheKey = $"product:{id}";
        
        return await _fusionCache.GetOrSetAsync<ProductDto>(
            cacheKey,
            async _ => await _productService.GetProductAsync(id),
            options => options
                .SetDuration(TimeSpan.FromMinutes(30))
                .SetTags("Products") // 태그로 무효화 가능
                .SetFailSafe(true, TimeSpan.FromHours(2))
                .SetPriority(CacheItemPriority.High)
        );
    }
}
```

### 3. 태그 헬퍼를 사용한 고급 캐싱

```csharp
public class OrderController : ControllerBase
{
    private readonly IAthenaCacheTagHelper _tagHelper;

    [HttpGet("complex-query")]
    public async Task<IEnumerable<OrderDto>> GetOrdersComplex(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] string? status)
    {
        var cacheKey = $"orders:complex:{startDate:yyyyMMdd}:{endDate:yyyyMMdd}:{status}";
        var tags = new[] { "Orders", "OrderReports" };

        return await _tagHelper.GetOrSetWithTagsAsync(
            cacheKey,
            async _ => {
                // 복잡한 쿼리 실행
                return await _orderService.GetOrdersComplexAsync(startDate, endDate, status);
            },
            tags,
            TimeSpan.FromMinutes(10)
        );
    }
}
```

### 4. 고급 무효화

```csharp
public class AdminController : ControllerBase
{
    private readonly ICacheInvalidator _invalidator;

    [HttpPost("bulk-update-orders")]
    public async Task<IActionResult> BulkUpdateOrders([FromBody] BulkOrderUpdate request)
    {
        await _orderService.BulkUpdateAsync(request);
        
        // 여러 태그를 동시에 무효화
        await _invalidator.InvalidateBatchAsync(new[] { "Orders", "OrderReports", "Dashboard" });
        
        return Ok();
    }
}
```

## 🔧 고급 설정

### Redis 분산 캐시와 함께 사용

```csharp
builder.Services.AddAthenaCacheFusionDistributed(
    "localhost:6379", // Redis 연결 문자열
    athena => {
        athena.Namespace = "MyApp_PROD";
        athena.DefaultExpirationMinutes = 60;
    },
    fusion => {
        fusion.WithOptions(options => {
            options.DefaultEntryOptions.Duration = TimeSpan.FromHours(1);
        });
        fusion.WithBackplane(); // Redis 백플레인으로 다중 인스턴스 동기화
        fusion.WithDistributedCache(); // L2 캐시로 Redis 사용
    }
);
```

### 커스텀 설정

```csharp
builder.Services.AddAthenaCacheFusion(
    athena => {
        athena.Namespace = "CustomApp";
        athena.DefaultExpirationMinutes = 45;
        athena.Logging.LogInvalidation = true;
        athena.CircuitBreaker.FailureThreshold = 3;
        athena.CircuitBreaker.Timeout = TimeSpan.FromMinutes(5);
    },
    fusion => {
        fusion.WithOptions(options => {
            options.DefaultEntryOptions.Duration = TimeSpan.FromMinutes(45);
            options.DefaultEntryOptions.Priority = CacheItemPriority.High;
        });
        
        // Fail-Safe 설정
        fusion.WithFailSafe(
            maxDuration: TimeSpan.FromHours(6),
            throttleDuration: TimeSpan.FromSeconds(30)
        );
        
        // Circuit Breaker 설정
        fusion.WithCircuitBreaker(
            duration: TimeSpan.FromMinutes(2),
            threshold: 3
        );
        
        // Logging 및 OpenTelemetry
        fusion.WithLogging();
        fusion.WithOpenTelemetry();
    },
    useFusionOptimizedKeyGenerator: true // FusionCache 최적화된 키 생성기 사용
);
```

## 🏷️ 태그 시스템

FusionCache의 태그 시스템과 Athena.Cache의 테이블 기반 무효화를 결합:

```csharp
// 태그와 함께 캐시
await _tagHelper.SetWithTagsAsync("user:123:profile", userProfile, 
    new[] { "Users", "UserProfiles" }, TimeSpan.FromMinutes(30));

// 태그로 무효화
await _invalidator.InvalidateAsync("Users"); // Users 태그를 가진 모든 항목 삭제

// 배치 태그 무효화
await _invalidator.InvalidateBatchAsync(new[] { "Users", "Products", "Orders" });
```

## 📊 성능 및 모니터링

### 통계 정보 확인

```csharp
[ApiController]
public class CacheStatsController : ControllerBase
{
    private readonly IAthenaCache _cache;

    [HttpGet("stats")]
    public async Task<CacheStatistics> GetStats()
    {
        return await _cache.GetStatisticsAsync();
    }
}
```

### 로깅 및 디버깅

```csharp
// appsettings.json
{
  "Logging": {
    "LogLevel": {
      "ZiggyCreatures.Caching.Fusion": "Debug",
      "Athena.Cache": "Debug"
    }
  }
}
```

## 🆚 기존 구현체와 비교

| 기능 | MemoryCache | FusionCache 통합 |
|-----|-------------|-----------------|
| 기본 캐싱 | ✅ | ✅ |
| 태그 기반 무효화 | ❌ | ✅ |
| Fail-Safe | ❌ | ✅ |
| Circuit Breaker | ❌ | ✅ |
| Hybrid L1+L2 | ❌ | ✅ |
| Cache Stampede 방지 | ❌ | ✅ |
| 분산 동기화 | ❌ | ✅ |
| OpenTelemetry | 기본 | 고급 |

## 🔄 마이그레이션

기존 Athena.Cache 코드는 수정 없이 그대로 작동합니다:

```csharp
// 기존 코드 - 수정 불필요
builder.Services.AddAthenaCacheMemory(); // ← 기존
builder.Services.AddAthenaCacheFusionComplete(); // ← 업그레이드
```

모든 기존 어트리뷰트와 API가 그대로 작동하며, FusionCache의 고급 기능이 자동으로 적용됩니다.

## 📝 예제 프로젝트

`Athena.Cache.Sample` 프로젝트의 `FusionCacheDemoController`에서 다양한 사용 예제를 확인할 수 있습니다.

## 🐛 문제 해결

### 일반적인 문제들

1. **태그 기반 무효화가 작동하지 않는 경우**:
   ```csharp
   // 캐시 설정 시 태그를 반드시 지정
   await _tagHelper.SetWithTagsAsync(key, value, new[] { "YourTable" });
   ```

2. **분산 환경에서 동기화 문제**:
   ```csharp
   // Redis 백플레인 설정 확인
   builder.Services.AddAthenaCacheFusionDistributed(connectionString);
   ```

3. **성능 문제**:
   ```csharp
   // Circuit Breaker 및 Fail-Safe 설정 조정
   fusion.WithCircuitBreaker(TimeSpan.FromMinutes(1));
   fusion.WithFailSafe(TimeSpan.FromHours(2));
   ```

## 📚 추가 자료

- [FusionCache 공식 문서](https://github.com/ZiggyCreatures/FusionCache)
- [Athena.Cache Core 문서](../Athena.Cache.Core/README.md)
- [Performance Benchmarks](../docs/performance/fusion-cache-benchmarks.md)

---

**라이선스**: MIT  
**Repository**: https://github.com/jhbrunoK/Athena.Cache