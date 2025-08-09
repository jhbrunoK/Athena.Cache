# Athena.Invalidation.Monitoring

실시간 캐시 무효화 모니터링 및 관찰성(Observability) 라이브러리

## 개요

Athena.Invalidation.Monitoring은 캐시 무효화 시스템의 성능, 상태, 동작을 실시간으로 모니터링하고 추적할 수 있는 포괄적인 관찰성 솔루션을 제공합니다.

## 주요 기능

### 🔍 실시간 메트릭 수집
- 무효화 작업 성공률 및 응답시간 추적
- 캐시 히트/미스 비율 모니터링
- 분산 이벤트 처리 성능 측정
- 시스템 상태 및 리소스 사용량 추적

### 📊 다양한 메트릭 백엔드 지원
- **OpenTelemetry**: 표준 관찰성 프레임워크
- **Prometheus**: 시계열 데이터베이스 및 메트릭 수집
- **커스텀 수집기**: 사용자 정의 메트릭 저장소

### ⚕️ 포괄적인 헬스체크
- 무효화 엔진 상태 확인
- 분산 이벤트 버스 연결 상태
- 캐시 제공자 상태 모니터링
- 데이터베이스 연결 확인

### 🔄 분산 추적 (Distributed Tracing)
- 무효화 작업의 전체 생명주기 추적
- 분산 환경에서의 이벤트 전파 경로 시각화
- 성능 병목 지점 식별

## 빠른 시작

### 1. 패키지 설치

```bash
dotnet add package Athena.Invalidation.Monitoring
```

### 2. 기본 모니터링 설정

```csharp
// Program.cs 또는 Startup.cs
using Athena.Invalidation.Monitoring.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 기본 모니터링 추가
builder.Services.AddInvalidationMonitoring(options =>
{
    options.MeterName = "MyApp.Invalidation";
    options.EnableDetailedMetrics = true;
});

// 기존 무효화 엔진을 모니터링 데코레이터로 감싸기
builder.Services.DecorateInvalidationEngineWithMonitoring();

var app = builder.Build();
```

### 3. OpenTelemetry 통합

```csharp
// OpenTelemetry와 함께 사용
builder.Services.AddInvalidationOpenTelemetry(meter =>
{
    meter.AddConsoleExporter()
          .AddOtlpExporter(); // Jaeger, Zipkin 등으로 전송
});
```

### 4. Prometheus 메트릭 익스포트

```csharp
// Prometheus 메트릭 서버 활성화
builder.Services.AddInvalidationPrometheus(options =>
{
    options.EnableMetricServer = true;
    options.Port = 9090;
});
```

### 5. 전체 모니터링 스택 설정

```csharp
// 모든 모니터링 기능을 한 번에 활성화
builder.Services.AddInvalidationFullMonitoring(
    monitoring => 
    {
        monitoring.EnableDetailedMetrics = true;
        monitoring.RecentEventsSampleSize = 1000;
    },
    prometheus => 
    {
        prometheus.Port = 9090;
        prometheus.UpdateInterval = TimeSpan.FromSeconds(5);
    },
    openTelemetry => 
    {
        openTelemetry.AddConsoleExporter();
    }
);
```

## 사용 예시

### 메트릭 조회

```csharp
public class InvalidationController : ControllerBase
{
    private readonly IInvalidationMetricsCollector _metricsCollector;

    public InvalidationController(IInvalidationMetricsCollector metricsCollector)
    {
        _metricsCollector = metricsCollector;
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics()
    {
        var snapshot = await _metricsCollector.GetMetricsAsync();
        
        return Ok(new
        {
            TotalInvalidations = snapshot.TotalInvalidations,
            SuccessRate = snapshot.InvalidationSuccessRate,
            AverageResponseTime = snapshot.AverageInvalidationTime.TotalMilliseconds,
            CacheHitRatio = snapshot.CacheHitRatio,
            Uptime = snapshot.Uptime.TotalSeconds
        });
    }
}
```

### 헬스체크 확인

```csharp
// 헬스체크 엔드포인트 추가
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                data = e.Value.Data
            })
        });
        
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(result);
    }
});
```

### 분산 환경 모니터링

```csharp
// 분산 이벤트 모니터링 활성화
builder.Services.IntegrateDistributedMonitoring(options =>
{
    options.LogDistributedEvents = true;
    options.MaxTrackedNodes = 50;
});

// 분산 메트릭 조회
public class DistributedMetricsController : ControllerBase
{
    private readonly IDistributedEventMonitor _monitor;

    [HttpGet("distributed-metrics")]
    public async Task<IActionResult> GetDistributedMetrics()
    {
        var metrics = await _monitor.GetDistributedMetricsAsync();
        return Ok(metrics);
    }
}
```

## 메트릭 종류

### 무효화 메트릭
- `athena_invalidation_total`: 총 무효화 작업 수
- `athena_invalidation_errors_total`: 실패한 무효화 작업 수
- `athena_invalidation_duration_seconds`: 무효화 작업 소요시간

### 캐시 메트릭
- `athena_cache_accesses_total`: 총 캐시 접근 수
- `athena_cache_hits_total`: 캐시 히트 수
- `athena_cache_hit_ratio`: 캐시 히트 비율

### 분산 메트릭
- `athena_distributed_events_total`: 분산 이벤트 처리 수
- `athena_distributed_event_duration_seconds`: 분산 이벤트 처리 시간

## 설정 옵션

### MonitoringOptions
```csharp
public class MonitoringOptions
{
    public string MeterName { get; set; } = "Athena.Invalidation";
    public string MeterVersion { get; set; } = "1.0.0";
    public int RecentEventsSampleSize { get; set; } = 1000;
    public bool EnableDetailedMetrics { get; set; } = true;
    public TimeSpan MetricsRetentionPeriod { get; set; } = TimeSpan.FromHours(24);
}
```

### PrometheusOptions
```csharp
public class PrometheusOptions
{
    public bool EnableMetricServer { get; set; } = true;
    public string Hostname { get; set; } = "*";
    public int Port { get; set; } = 9090;
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(10);
}
```

### HealthCheckOptions
```csharp
public class HealthCheckOptions
{
    public double MinSuccessRate { get; set; } = 0.95;
    public int MaxResponseTimeMs { get; set; } = 5000;
    public double MaxErrorRate { get; set; } = 0.05;
    public double MinCacheHitRatio { get; set; } = 0.70;
}
```

## 대시보드 구성

### Grafana 대시보드 예시

```json
{
  "dashboard": {
    "title": "Athena Cache Invalidation Monitoring",
    "panels": [
      {
        "title": "Invalidation Success Rate",
        "type": "stat",
        "targets": [
          {
            "expr": "athena_invalidation_success_rate"
          }
        ]
      },
      {
        "title": "Response Times",
        "type": "graph", 
        "targets": [
          {
            "expr": "histogram_quantile(0.95, athena_invalidation_duration_seconds_bucket)"
          }
        ]
      }
    ]
  }
}
```

## 모범 사례

### 1. 적절한 샘플링 설정
```csharp
services.AddInvalidationMonitoring(options =>
{
    // 고성능 환경에서는 샘플링 크기 조정
    options.RecentEventsSampleSize = 500;
});
```

### 2. 프로덕션 환경 설정
```csharp
// 프로덕션에서는 상세 로깅 비활성화
services.Configure<DistributedMonitoringOptions>(options =>
{
    options.LogDistributedEvents = false;
});
```

### 3. 알림 설정
```yaml
# Prometheus 알림 규칙 예시
groups:
- name: athena_invalidation
  rules:
  - alert: HighInvalidationFailureRate
    expr: athena_invalidation_success_rate < 0.95
    for: 5m
    labels:
      severity: warning
    annotations:
      summary: "High invalidation failure rate detected"
```

## 성능 고려사항

- 메트릭 수집은 비동기로 처리되어 주 작업에 영향을 최소화
- 메모리 기반 집계로 빠른 성능 제공
- 샘플링을 통한 메모리 사용량 제어
- 백그라운드에서 주기적인 메트릭 정리

## 문제 해결

### 일반적인 문제들

**Q: Prometheus 메트릭이 노출되지 않음**
A: 메트릭 서버 포트가 올바르게 설정되었는지 확인하고, 방화벽 설정을 점검하세요.

**Q: 메트릭 정확도가 떨어짐**
A: `RecentEventsSampleSize`를 늘려서 더 많은 샘플을 수집하도록 설정하세요.

**Q: 메모리 사용량이 과도함**
A: 메트릭 보존 기간(`MetricsRetentionPeriod`)을 줄이거나 샘플 크기를 조정하세요.

## 라이선스

MIT License - 자세한 내용은 [LICENSE](../../LICENSE.txt) 파일을 참조하세요.