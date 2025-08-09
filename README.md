# 🏛️ Athena.Cache

[![CI](https://github.com/jhbrunoK/Athena.Cache/actions/workflows/ci.yml/badge.svg)](https://github.com/jhbrunoK/Athena.Cache/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/jhbrunoK/Athena.Cache/graph/badge.svg)](https://codecov.io/gh/jhbrunoK/Athena.Cache)
[![NuGet Core](https://img.shields.io/nuget/v/Athena.Invalidation.Core.svg)](https://www.nuget.org/packages/Athena.Invalidation.Core/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**Enterprise-grade cache invalidation library for .NET with advanced distributed, hierarchical, and CQRS-based invalidation strategies.**

Athena.Cache는 대규모 분산 시스템을 위한 고급 캐시 무효화 라이브러리입니다. 복잡한 의존성 관리, 분산 이벤트 처리, 성능 최적화를 제공합니다.

## ✨ 주요 특징

- 🎯 **다중 무효화 전략**: 테이블 기반, 패턴 기반, 계층형, CQRS 기반 무효화
- 🌐 **분산 시스템 지원**: 클러스터 간 실시간 무효화 동기화
- 🏗️ **계층형 의존성 관리**: 복잡한 캐시 의존성 트리 자동 관리
- ⚡ **고성능 최적화**: 배치 처리, 백그라운드 큐, 서킷 브레이커
- 📊 **엔터프라이즈 모니터링**: OpenTelemetry, Prometheus, 헬스체크 통합
- 🔄 **다중 공급자**: Redis, MemoryCache, FusionCache 동시 지원
- 🎭 **CQRS 패턴**: Command/Query/Event 기반 무효화

## 🚀 빠른 시작

### 설치

```bash
# 핵심 라이브러리
dotnet add package Athena.Invalidation.Core
dotnet add package Athena.Invalidation.Engine

# ASP.NET Core 통합
dotnet add package Athena.Invalidation.AspNetCore

# 고급 기능 (선택)
dotnet add package Athena.Invalidation.Redis          # Redis 지원
dotnet add package Athena.Invalidation.FusionCache    # FusionCache 통합
dotnet add package Athena.Invalidation.Distributed    # 분산 시스템
dotnet add package Athena.Invalidation.CQRS           # CQRS 패턴
dotnet add package Athena.Invalidation.Monitoring     # 엔터프라이즈 모니터링
```

### 기본 설정

```csharp
// Program.cs
using Athena.Invalidation.AspNetCore.Extensions;
using Athena.Invalidation.Redis.Extensions;
using Athena.Invalidation.Monitoring.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 기본 무효화 엔진
builder.Services.AddInvalidationEngine(options => {
    options.DefaultStrategy = InvalidationStrategy.TableBased;
    options.EnableBackgroundProcessing = true;
    options.BatchSize = 100;
});

// Redis 분산 캐시 (선택)
builder.Services.AddRedisInvalidation(options => {
    options.ConnectionString = "localhost:6379";
    options.DatabaseId = 0;
});

// 엔터프라이즈 모니터링 (선택)
builder.Services.AddInvalidationMonitoring(options => {
    options.EnablePrometheusMetrics = true;
    options.EnableOpenTelemetry = true;
    options.HealthCheckInterval = TimeSpan.FromMinutes(1);
});

var app = builder.Build();
app.Run();
```

### 사용 예제

#### 1. 기본 테이블 기반 무효화
```csharp
public class UserService
{
    private readonly IInvalidationEngine _invalidationEngine;
    
    public async Task<User> UpdateUserAsync(int userId, UpdateUserDto dto)
    {
        var user = await _repository.UpdateAsync(userId, dto);
        
        // Users 테이블 관련 모든 캐시 무효화
        await _invalidationEngine.InvalidateAsync("Users");
        
        return user;
    }
}
```

#### 2. 계층형 의존성 무효화
```csharp
// 복잡한 캐시 의존성 설정
services.AddHierarchicalInvalidation(builder => {
    builder.AddDependency("Users", "UserProfiles", "UserSettings");
    builder.AddDependency("Products", "Categories", "Inventory");
    builder.AddDependency("Orders", "Users", "Products", "Payments");
});

// Orders 무효화 시 자동으로 Users, Products, Payments도 무효화
await _invalidationEngine.InvalidateHierarchicalAsync("Orders");
```

#### 3. CQRS 이벤트 기반 무효화
```csharp
public class UserCreatedEventHandler : INotificationHandler<UserCreatedEvent>
{
    private readonly IEventDrivenInvalidation _invalidation;
    
    public async Task Handle(UserCreatedEvent notification, CancellationToken cancellationToken)
    {
        await _invalidation.InvalidateByEventAsync(notification, new[] 
        { 
            "Users", 
            "UserStats", 
            "UserAnalytics" 
        });
    }
}
```

#### 4. 분산 시스템 무효화
```csharp
// 다중 노드 환경에서 실시간 무효화 동기화
services.AddDistributedInvalidation(options => {
    options.EventBusProvider = EventBusProvider.Redis;
    options.ClusterNodeId = Environment.MachineName;
    options.EnableEventSourcing = true;
});

// 한 노드에서 실행하면 모든 노드에서 자동 무효화
await _distributedEngine.BroadcastInvalidationAsync("GlobalCache");
```

## 📦 아키텍처 구성

```
Athena.Invalidation/
├── 🔧 Core/                    # 핵심 추상화 및 인터페이스
├── ⚙️ Engine/                  # 메인 무효화 엔진
├── 📋 Strategies/              # 다양한 무효화 전략
├── 🌐 AspNetCore/              # ASP.NET Core 통합
├── 🔴 Redis/                   # Redis 공급자
├── 💾 MemoryCache/             # IMemoryCache 공급자  
├── 🚀 FusionCache/             # FusionCache 통합
├── 🔄 MultiProvider/           # 다중 공급자 지원
├── 🎭 CQRS/                    # CQRS 패턴 구현
├── 🏗️ Hierarchical/           # 계층형 의존성 관리
├── 🌍 Distributed/             # 분산 시스템 지원
├── 📊 Monitoring/              # 엔터프라이즈 모니터링
└── 📈 Tracking/                # 고급 캐시 추적
```

## 🎛️ 고급 설정

### 성능 최적화
```csharp
services.AddInvalidationEngine(options => {
    options.EnableBatchProcessing = true;
    options.BatchSize = 500;
    options.BatchFlushInterval = TimeSpan.FromSeconds(5);
    options.BackgroundProcessorCount = Environment.ProcessorCount;
    options.EnableCircuitBreaker = true;
    options.CircuitBreakerThreshold = 10;
});
```

### 분산 환경 설정
```csharp
services.AddDistributedInvalidation(options => {
    options.EventBusProvider = EventBusProvider.Redis;
    options.RedisConnectionString = "cluster1:6379,cluster2:6379,cluster3:6379";
    options.EnableFailover = true;
    options.EventRetention = TimeSpan.FromHours(24);
    options.ConsistencyLevel = ConsistencyLevel.EventuallyConsistent;
});
```

### 모니터링 및 알림
```csharp
services.AddInvalidationMonitoring(options => {
    options.EnablePrometheusMetrics = true;
    options.EnableOpenTelemetry = true;
    options.MetricsPort = 9090;
    options.HealthCheckEndpoint = "/health/invalidation";
    options.AlertThresholds = new AlertThresholds
    {
        MaxProcessingTime = TimeSpan.FromSeconds(10),
        MaxQueueSize = 10000,
        MaxErrorRate = 0.05 // 5%
    };
});
```

## 📊 패키지 현황

| 패키지 | 설명 | NuGet | 상태 |
|--------|------|--------|------|
| **Athena.Invalidation.Core** | 핵심 추상화 및 인터페이스 | [![NuGet](https://img.shields.io/nuget/v/Athena.Invalidation.Core.svg)](https://www.nuget.org/packages/Athena.Invalidation.Core/) | ✅ 안정 |
| **Athena.Invalidation.Engine** | 메인 무효화 엔진 | [![NuGet](https://img.shields.io/nuget/v/Athena.Invalidation.Engine.svg)](https://www.nuget.org/packages/Athena.Invalidation.Engine/) | ✅ 안정 |
| **Athena.Invalidation.AspNetCore** | ASP.NET Core 통합 | [![NuGet](https://img.shields.io/nuget/v/Athena.Invalidation.AspNetCore.svg)](https://www.nuget.org/packages/Athena.Invalidation.AspNetCore/) | ✅ 안정 |
| **Athena.Invalidation.Redis** | Redis 분산 캐싱 | [![NuGet](https://img.shields.io/nuget/v/Athena.Invalidation.Redis.svg)](https://www.nuget.org/packages/Athena.Invalidation.Redis/) | ✅ 안정 |
| **Athena.Invalidation.FusionCache** | FusionCache 통합 | [![NuGet](https://img.shields.io/nuget/v/Athena.Invalidation.FusionCache.svg)](https://www.nuget.org/packages/Athena.Invalidation.FusionCache/) | 🚀 권장 |
| **Athena.Invalidation.Distributed** | 분산 시스템 지원 | [![NuGet](https://img.shields.io/nuget/v/Athena.Invalidation.Distributed.svg)](https://www.nuget.org/packages/Athena.Invalidation.Distributed/) | 🏢 엔터프라이즈 |
| **Athena.Invalidation.CQRS** | CQRS 패턴 구현 | [![NuGet](https://img.shields.io/nuget/v/Athena.Invalidation.CQRS.svg)](https://www.nuget.org/packages/Athena.Invalidation.CQRS/) | 🎭 고급 |
| **Athena.Invalidation.Hierarchical** | 계층형 의존성 관리 | [![NuGet](https://img.shields.io/nuget/v/Athena.Invalidation.Hierarchical.svg)](https://www.nuget.org/packages/Athena.Invalidation.Hierarchical/) | 🏗️ 고급 |
| **Athena.Invalidation.Monitoring** | 엔터프라이즈 모니터링 | [![NuGet](https://img.shields.io/nuget/v/Athena.Invalidation.Monitoring.svg)](https://www.nuget.org/packages/Athena.Invalidation.Monitoring/) | 📊 프로덕션 |

## 🚀 성능 벤치마크

```
BenchmarkDotNet v0.13.12, Windows 11
Intel Core i7-12700K (12 cores), 32GB RAM

| Method                    | Mean      | Error    | StdDev   | Allocated |
|-------------------------- |----------:|---------:|---------:|----------:|
| BasicInvalidation         |  12.45 μs | 0.089 μs | 0.079 μs |     256 B |
| BatchInvalidation_100     |  89.32 μs | 1.234 μs | 1.154 μs |   2.1 KB  |
| HierarchicalInvalidation  |  34.67 μs | 0.432 μs | 0.404 μs |     512 B |
| DistributedInvalidation   |  156.8 μs | 2.341 μs | 2.190 μs |   4.2 KB  |
| CQRSEventInvalidation     |  45.23 μs | 0.687 μs | 0.643 μs |     768 B |
```

## 📚 문서 및 가이드

- **[📖 API 문서](API-Documentation.md)** - 전체 API 레퍼런스
- **[🔧 프로덕션 가이드](docs/PRODUCTION_GUIDE.md)** - 운영 환경 배포 가이드
- **[📝 변경 로그](CHANGELOG.md)** - 버전별 변경 사항
- **[🎯 사용 예제](USAGE_EXAMPLES.md)** - 실제 사용 사례
- **[📊 모니터링 가이드](src/Athena.Invalidation.Monitoring/README.md)** - 모니터링 설정

## 🏗️ 기여하기

1. **이슈 확인**: [GitHub Issues](https://github.com/jhbrunoK/Athena.Cache/issues)에서 버그 리포트나 기능 요청 확인
2. **포크 및 브랜치**: 본 리포지토리를 포크하고 기능 브랜치 생성
3. **개발**: 코드 스타일 가이드를 따르며 개발
4. **테스트**: `dotnet test` 실행하여 모든 테스트 통과 확인
5. **풀 리퀘스트**: 상세한 설명과 함께 PR 생성

### 개발 환경 설정
```bash
git clone https://github.com/jhbrunoK/Athena.Cache.git
cd Athena.Cache
dotnet restore
dotnet build
dotnet test
```

## 📄 라이선스

이 프로젝트는 MIT 라이선스 하에 배포됩니다. [LICENSE](LICENSE.txt) 파일을 참고하세요.

## 🆘 지원 및 커뮤니티

- **Repository**: [https://github.com/jhbrunoK/Athena.Cache](https://github.com/jhbrunoK/Athena.Cache)
- **Issues**: [https://github.com/jhbrunoK/Athena.Cache/issues](https://github.com/jhbrunoK/Athena.Cache/issues)
- **Discussions**: [https://github.com/jhbrunoK/Athena.Cache/discussions](https://github.com/jhbrunoK/Athena.Cache/discussions)

---

**Athena.Cache** - *지혜의 여신이 주는 완벽한 캐시 무효화* 🏛️