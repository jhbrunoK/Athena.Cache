# Athena.Invalidation Release Notes

## v0.2.0-alpha (2025-01-31) - Phase 2: CQRS & Hierarchical Invalidation

### 🎯 주요 목표
CQRS 아키텍처와 복잡한 계층적 무효화를 위한 고급 기능 추가

### 🚀 새로운 기능

#### 🏗️ CQRS 패턴 완전 지원
- **명령 기반 무효화**: `InvalidateOnCommandAsync<TCommand>()`로 명령 실행 후 자동 무효화
- **이벤트 드리븐 무효화**: `InvalidateOnEventAsync<TEvent>()`로 도메인 이벤트 기반 무효화  
- **읽기 모델 무효화**: `InvalidateReadModelAsync<TReadModel>()`로 읽기 모델 특화 무효화
- **프로젝션 무효화**: `InvalidateProjectionAsync<TProjection>()`로 프로젝션 재구성 지원

#### 🧩 CQRS 구성요소
- **CommandInvalidationHandler**: 명령 실행 후 스마트 무효화 처리
- **EventDrivenInvalidation**: 도메인 이벤트 기반 무효화 엔진
- **ReadModelInvalidator**: 읽기 모델 및 프로젝션 캐시 관리
- **자동 타입 추론**: 명령/이벤트 타입에서 테이블명 자동 추론

#### 🌳 고급 계층적 무효화
- **DependencyGraphInvalidator**: 복잡한 의존성 그래프 관리
  - 강한 의존성 (Strong): 반드시 무효화
  - 약한 의존성 (Weak): 실패해도 계속 진행
  - 조건적 의존성 (Conditional): 조건부 무효화
  - 지연 의존성 (Delayed): 시간차 무효화
- **HierarchicalInvalidationTree**: 계층 구조 기반 무효화 계획
  - 계층 정의 및 테이블 할당
  - 다방향 무효화 (상위/하위/양방향/형제)
  - 무효화 전략별 실행 (즉시/배치/지연/조건부)

### 🔧 API 확장

#### CQRS API
```csharp
// 명령 기반 무효화
await engine.InvalidateOnCommandAsync(new CreateUserCommand { UserId = "user123" });

// 이벤트 기반 무효화  
await engine.InvalidateOnEventAsync(new UserCreatedEvent { UserId = "user123" });

// 읽기 모델 무효화
await engine.InvalidateReadModelAsync<UserProfileReadModel>("user123");

// 프로젝션 무효화
await engine.InvalidateProjectionAsync<UserSummaryProjection>();
```

#### 계층적 무효화 API
```csharp
// 의존성 그래프 구성
var graph = new DependencyGraphInvalidator(engine, logger);
graph.AddDependency("Users", "UserProfiles", DependencyType.Strong);
graph.AddDependency("Users", "UserSettings", DependencyType.Weak);

// 의존성 기반 무효화
await graph.InvalidateWithDependenciesAsync("Users");

// 계층 구조 무효화
var tree = new HierarchicalInvalidationTree(engine, logger);
tree.DefineLayer("DataLayer");
tree.DefineLayer("ServiceLayer", "DataLayer");
tree.AssignTableToLayer("Users", "DataLayer");

var plan = await tree.CreateInvalidationPlanAsync("Users", InvalidationDirection.Up);
await tree.ExecuteInvalidationPlanAsync(plan);
```

### 📦 새로운 패키지
- `Athena.Invalidation.CQRS` - CQRS 패턴 지원 및 이벤트 기반 무효화
- `Athena.Invalidation.Hierarchical` - 고급 계층적 무효화 및 의존성 그래프

### 🔧 기술적 혁신
- **스마트 타입 추론**: 명령/이벤트/읽기모델 타입에서 테이블명 자동 추론
- **동적 메서드 호출**: 제네릭 제약 없이 CQRS 컴포넌트 통합
- **메모리 효율적 그래프**: ConcurrentDictionary 기반 고성능 의존성 관리
- **순환 감지 알고리즘**: DFS 기반 안전한 그래프 구조 보장

### 📊 시각화 및 모니터링
- **의존성 그래프 시각화**: 노드와 엣지 데이터 제공
- **계층 구조 시각화**: 계층별 테이블 분포 표시
- **통계 정보**: 노드/엣지 수, 최대 깊이, 순환 검사
- **구조 검증**: 고아 테이블 감지, 순환 의존성 검사

### 🧪 테스트 강화
- CQRS 구성요소별 포괄적 단위 테스트 
- 계층적 무효화 시나리오 테스트
- 의존성 그래프 알고리즘 검증
- 실제 사용 사례 기반 통합 테스트

### 💡 실제 활용 시나리오
- **마이크로서비스 아키텍처**: 서비스 간 캐시 의존성 관리
- **CQRS/EventSourcing**: 명령/이벤트 기반 자동 캐시 무효화
- **복잡한 도메인 모델**: 엔티티 간 관계를 고려한 스마트 무효화
- **대용량 읽기 모델**: 프로젝션 재구성과 캐시 갱신 최적화

### 🎯 Phase 3 계획
- 분산 환경 지원 (클러스터링, 이벤트 버스)
- 실시간 모니터링 및 메트릭
- 성능 최적화 및 배치 처리
- 운영 도구 및 관리 대시보드

---

## v0.1.0-alpha (2025-01-31)

### 🚀 새로운 기능
- **범용 캐시 무효화 엔진**: 플러그인 아키텍처 기반 확장 가능한 엔진
- **기본 무효화 전략**: Table, Pattern, Key, Batch 기반 무효화
- **계층적 무효화**: 연관된 테이블들과 함께 계층적으로 무효화
- **캐시 프로바이더 추상화**: MemoryCache, Redis 지원
- **ASP.NET Core 통합**: 의존성 주입과 완전 통합
- **비동기 무효화**: 고성능 논블로킹 무효화 처리

### 🔧 API 소개
```csharp
// 기본 설정
services.AddInvalidationEngineComplete(options =>
{
    options.Logging.LogInvalidationEvents = true;
});

// 사용법
await engine.InvalidateByTableAsync("Users");
await engine.InvalidateByPatternAsync("user:*");
await engine.InvalidateBatchAsync(new[] { "Users", "Orders" });
await engine.InvalidateHierarchyAsync("Users", new[] { "UserProfiles" });
```

### 📦 패키지
- `Athena.Invalidation.Core` - 핵심 추상화 및 인터페이스
- `Athena.Invalidation.Engine` - 무효화 엔진 구현
- `Athena.Invalidation.Strategies` - 내장 무효화 전략
- `Athena.Invalidation.AspNetCore` - ASP.NET Core 통합

### 🧪 테스트 커버리지
- **단위 테스트**: 전략 구현체 테스트
- **통합 테스트**: 엔진과 프로바이더 연동 테스트
- **테스트 통과율**: 19/22 (86%)

### ⚠️ 알려진 제한사항
- CQRS 지원 미구현 (Phase 2에서 제공)
- 분산 무효화 미구현 (Phase 3에서 제공)
- 고급 모니터링 미구현 (Phase 4에서 제공)
- 일부 테스트 실패 (패턴 매칭 및 배치 처리)

### 🎯 사용 시나리오
이 alpha 버전은 다음과 같은 시나리오에 적합합니다:
- 단일 인스턴스 애플리케이션
- 기본적인 테이블 기반 무효화
- MemoryCache 또는 Redis 사용
- 프로토타입 및 개념 검증

### 📈 성능 특성
- **처리량**: 중간 규모 애플리케이션에 적합
- **메모리 사용량**: 최적화되지 않은 초기 구현
- **응답 시간**: 단순한 무효화 작업에서 빠른 응답

### 🔮 다음 계획 (v0.2.0-alpha)
- **CQRS 통합**: 명령 및 이벤트 기반 무효화
- **계층적 무효화**: 다층 캐시 아키텍처 지원
- **도메인 이벤트**: 이벤트 소싱과 완전 통합
- **성능 최적화**: 메모리 사용량 및 처리 속도 개선

---

**⚠️ 주의사항**: 이것은 alpha 버전입니다. 프로덕션 환경에서 사용하기 전에 충분한 테스트를 수행하세요. API가 변경될 수 있습니다.