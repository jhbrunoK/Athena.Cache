# Athena Cache Invalidation - 프로덕션 배포 가이드

## 📋 개요

이 문서는 Athena Cache Invalidation 라이브러리를 프로덕션 환경에서 안전하고 효율적으로 배포하기 위한 포괄적인 가이드입니다.

## 🏗️ 아키텍처 선택

### 1. 단일 캐시 제공자

**Memory Cache만 사용하는 경우:**
```csharp
services.AddMemoryCacheInvalidation(options =>
{
    options.MaxTrackedKeys = 50000;
    options.EnableTagging = true;
    options.AllowClearAll = false; // 프로덕션에서는 비활성화
});
```

**Redis만 사용하는 경우:**
```csharp
services.AddRedisInvalidation("localhost:6379", 
    providerOptions =>
    {
        options.Database = 0;
        options.ScanPageSize = 1000;
        options.UseTransaction = true;
    },
    engineOptions =>
    {
        options.AllowClearAll = false;
        options.EnableKeyTracking = true;
    });
```

**FusionCache 사용하는 경우:**
```csharp
services.AddFusionCache()
    .WithDefaultEntryOptions(new FusionCacheEntryOptions
    {
        Duration = TimeSpan.FromMinutes(30),
        FailSafeMaxDuration = TimeSpan.FromHours(2)
    });

services.AddFusionCacheInvalidation(options =>
{
    options.ConvertPatternToTag = true;
    options.EnableKeyTracking = true;
    options.MaxTrackedKeys = 100000;
});
```

### 2. 다중 캐시 제공자 (권장)

```csharp
services.CreateMultiProviderBuilder()
    .AddMemoryCache()
    .AddRedis("redis:6379")
    .AddFusionCache()
    .Build(options =>
    {
        options.DefaultFailurePolicy = FailureHandlingPolicy.AtLeastOneSucceeds;
        options.EnableParallelExecution = true;
        options.MaxConcurrency = Environment.ProcessorCount;
        options.AllowClearAll = false;
    });
```

## ⚙️ 핵심 설정

### 1. 로깅 설정

**appsettings.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Athena.Invalidation": "Warning",
      "Athena.Invalidation.Core": "Information",
      "Athena.Invalidation.Distributed": "Warning"
    }
  }
}
```

### 2. 헬스체크 설정

```csharp
services.AddHealthChecks()
    .AddMemoryCacheInvalidationHealthChecks()
    .AddRedisInvalidationHealthChecks()
    .AddMultiProviderInvalidationHealthChecks(
        healthCheckName: "cache_invalidation",
        timeout: TimeSpan.FromSeconds(30));

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

### 3. 모니터링 및 메트릭

```csharp
services.AddInvalidationMonitoring(options =>
{
    options.EnablePrometheusMetrics = true;
    options.EnableOpenTelemetry = true;
    options.MetricsCollectionInterval = TimeSpan.FromMinutes(1);
});

// Prometheus 메트릭 엔드포인트
app.MapMetrics("/metrics");
```

### 4. 분산 이벤트 버스 (대규모 환경)

```csharp
services.AddDistributedInvalidation(options =>
{
    options.NodeId = Environment.MachineName;
    options.EventBusType = EventBusType.Redis;
    options.RedisConnectionString = "redis-cluster:6379";
    options.EnableEventLoopback = false;
    options.MaxRetryAttempts = 3;
});
```

## 🚀 배포 전략

### 1. 단계별 배포

1. **개발 환경**
   - Memory Cache만 사용
   - 디버그 로깅 활성화
   - 모든 추적 기능 활성화

2. **스테이징 환경**
   - 프로덕션과 동일한 구성
   - Redis + Memory Cache 조합
   - 성능 테스트 수행

3. **프로덕션 환경**
   - 다중 제공자 구성
   - 최적화된 로깅
   - 모니터링 활성화

### 2. 컨테이너 배포

**Dockerfile:**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
COPY ["YourApp.csproj", "."]
COPY ["packages/", "./packages/"]
RUN dotnet restore "YourApp.csproj" --source "./packages" --source "https://api.nuget.org/v3/index.json"

COPY . .
RUN dotnet build "YourApp.csproj" -c Release -o /app/build
RUN dotnet publish "YourApp.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "YourApp.dll"]
```

**docker-compose.yml:**
```yaml
version: '3.8'

services:
  app:
    build: .
    environment:
      - ConnectionStrings__Redis=redis:6379
      - CacheInvalidation__DefaultFailurePolicy=AtLeastOneSucceeds
      - CacheInvalidation__EnableParallelExecution=true
    depends_on:
      - redis
    ports:
      - "8080:80"

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    command: redis-server --appendonly yes
    volumes:
      - redis-data:/data

  prometheus:
    image: prom/prometheus:latest
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml

volumes:
  redis-data:
```

## 📊 성능 최적화

### 1. 메모리 최적화

```csharp
services.Configure<CacheKeyTrackingOptions>(options =>
{
    options.MaxTrackedKeys = 50000;
    options.EnablePeriodicCleanup = true;
    options.CleanupInterval = TimeSpan.FromMinutes(15);
    options.MaxHistoryEntries = 5000;
    options.MemoryCompactionThreshold = 25000;
});
```

### 2. Redis 최적화

```csharp
services.Configure<RedisInvalidationOptions>(options =>
{
    options.ScanPageSize = 2000; // 더 큰 페이지 크기로 네트워크 호출 감소
    options.MaxScanResults = 20000;
    options.UseTransaction = true; // 배치 작업의 원자성 보장
    options.CommandTimeout = TimeSpan.FromSeconds(5);
});
```

### 3. 병렬 처리 최적화

```csharp
services.Configure<MultiProviderInvalidationOptions>(options =>
{
    options.EnableParallelExecution = true;
    options.MaxConcurrency = Math.Min(Environment.ProcessorCount * 2, 16);
    options.ProviderTimeout = TimeSpan.FromSeconds(30);
});
```

## 🛡️ 보안 고려사항

### 1. Redis 보안

```bash
# Redis 설정
requirepass your-strong-password
bind 127.0.0.1 10.0.0.0/8
protected-mode yes
maxmemory 2gb
maxmemory-policy allkeys-lru
```

### 2. 네트워크 보안

- Redis 접근을 VPN/내부 네트워크로 제한
- TLS 암호화 사용
- 방화벽 규칙 설정

### 3. 데이터 보안

```csharp
// 민감한 데이터가 키에 포함되지 않도록 주의
services.Configure<CacheKeyTrackingOptions>(options =>
{
    options.EnableHistory = false; // 프로덕션에서는 히스토리 비활성화 고려
    options.RegexTimeout = TimeSpan.FromSeconds(1); // DoS 공격 방지
});
```

## 📈 모니터링 및 알림

### 1. Prometheus 메트릭

주요 메트릭:
- `athena_invalidation_operations_total`
- `athena_invalidation_operations_duration_seconds`
- `athena_invalidation_cache_keys_total`
- `athena_invalidation_provider_health`

### 2. 알림 규칙

```yaml
# prometheus-alerts.yml
groups:
- name: athena-invalidation
  rules:
  - alert: CacheInvalidationDown
    expr: athena_invalidation_provider_health == 0
    for: 1m
    labels:
      severity: critical
    annotations:
      summary: "Cache invalidation provider is down"

  - alert: HighInvalidationErrors
    expr: rate(athena_invalidation_operations_total{status="error"}[5m]) > 0.1
    for: 2m
    labels:
      severity: warning
    annotations:
      summary: "High cache invalidation error rate"
```

### 3. 로그 집계

```csharp
services.AddSerilog((context, config) =>
{
    config
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.WithProperty("ApplicationName", "YourApp")
        .WriteTo.Console()
        .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri("http://elasticsearch:9200"))
        {
            IndexFormat = "athena-invalidation-{0:yyyy.MM.dd}",
            AutoRegisterTemplate = true
        });
});
```

## 🔧 문제 해결

### 1. 일반적인 문제

**메모리 사용량 증가:**
```csharp
// 정리 주기 단축
services.Configure<CacheKeyTrackingOptions>(options =>
{
    options.CleanupInterval = TimeSpan.FromMinutes(5);
    options.MaxTrackedKeys = 25000; // 제한 감소
});
```

**Redis 연결 문제:**
```csharp
services.Configure<RedisInvalidationOptions>(options =>
{
    options.ConnectTimeout = TimeSpan.FromSeconds(10);
    options.CommandTimeout = TimeSpan.FromSeconds(5);
});

// 연결 재시도 로직
services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = ConfigurationOptions.Parse(connectionString);
    configuration.ConnectRetry = 5;
    configuration.ReconnectRetryPolicy = new ExponentialRetry(1000, 30000);
    return ConnectionMultiplexer.Connect(configuration);
});
```

### 2. 성능 문제

**느린 패턴 매칭:**
```csharp
services.Configure<CacheKeyTrackingOptions>(options =>
{
    options.RegexTimeout = TimeSpan.FromMilliseconds(500); // 시간제한 단축
});
```

**높은 CPU 사용량:**
```csharp
services.Configure<MultiProviderInvalidationOptions>(options =>
{
    options.MaxConcurrency = Environment.ProcessorCount; // 병렬성 감소
    options.EnableParallelExecution = false; // 순차 실행으로 변경
});
```

## 📝 체크리스트

### 배포 전 확인사항

- [ ] 모든 제공자의 헬스체크가 통과하는가?
- [ ] 로그 레벨이 적절하게 설정되었는가?
- [ ] `AllowClearAll` 옵션이 비활성화되어 있는가?
- [ ] 메트릭 수집이 정상 작동하는가?
- [ ] 백업 및 복구 전략이 수립되었는가?
- [ ] 부하 테스트를 수행했는가?
- [ ] 장애 복구 절차가 문서화되었는가?

### 운영 중 모니터링

- [ ] 메모리 사용량 추이
- [ ] Redis 연결 상태
- [ ] 무효화 작업 성공률
- [ ] 응답 시간 지표
- [ ] 에러 로그 발생 패턴

## 🚨 긴급 대응

### 1. 시스템 다운 시

```bash
# 헬스체크 확인
curl http://your-app/health

# 메트릭 확인
curl http://your-app/metrics

# 로그 확인
docker logs your-app-container
```

### 2. 메모리 누수 의심 시

```csharp
// 임시로 추적 기능 비활성화
services.Configure<CacheKeyTrackingOptions>(options =>
{
    options.EnableHistory = false;
    options.MaxTrackedKeys = 1000;
});
```

### 3. Redis 연결 불안정 시

```csharp
// Memory Cache로 폴백
services.Configure<MultiProviderInvalidationOptions>(options =>
{
    options.DefaultFailurePolicy = FailureHandlingPolicy.IgnoreErrors;
});
```

이 가이드를 따라 안전하고 효율적인 프로덕션 배포를 수행하시기 바랍니다. 추가 질문이나 지원이 필요한 경우 개발팀에 문의해 주세요.