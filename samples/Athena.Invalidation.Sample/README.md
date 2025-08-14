# Athena Invalidation Sample

이 샘플 애플리케이션은 **Athena.Invalidation** 라이브러리의 모든 기능을 실제 시나리오에서 보여주는 종합적인 데모입니다.

## 🚀 빠른 시작

### 실행 방법
```bash
# 프로젝트 디렉토리로 이동
cd samples/Athena.Invalidation.Sample

# 애플리케이션 실행
dotnet run

# 또는 개발 모드로 실행
dotnet run --environment Development
```

애플리케이션이 실행되면:
- **Swagger UI**: https://localhost:7140 또는 http://localhost:5140
- **Health Check**: https://localhost:7140/health

## 📋 주요 기능

### 1. 기본 무효화 기능 (`/api/BasicInvalidation`)
- **테이블 기반 무효화**: 특정 테이블과 관련된 모든 캐시 제거
- **패턴 기반 무효화**: 와일드카드 패턴으로 캐시 키 그룹 제거
- **키 기반 무효화**: 정확한 캐시 키 제거
- **전체 캐시 초기화**: 모든 캐시 제거 (주의!)

### 2. 배치 무효화 기능 (`/api/BatchInvalidation`)
- **배치 테이블 무효화**: 여러 테이블을 한번에 무효화
- **배치 패턴 무효화**: 여러 패턴을 동시에 처리
- **배치 키 무효화**: 다수의 캐시 키를 효율적으로 제거
- **대량 업데이트 시나리오**: 실제 상황을 모방한 데모

## 🛠 아키텍처

### 도메인 모델
```
Category (카테고리)
├── Products (상품들)
    └── OrderItems (주문 항목들)
        └── Order (주문)
            └── User (사용자)
```

### 캐시 키 전략
- **상품**: `product:{id}`, `products:category:{categoryId}`, `products:featured`
- **카테고리**: `category:{id}`, `categories:all`, `categories:active`
- **사용자**: `user:{id}`, `user:email:{email}`, `users:active`
- **주문**: `order:{id}`, `orders:user:{userId}:*`

### 무효화 전략
1. **개별 키 무효화**: 특정 항목이 변경될 때
2. **패턴 무효화**: 관련 항목 그룹이 변경될 때
3. **테이블 무효화**: 대량 변경이나 스키마 변경시
4. **스마트 무효화**: 영향 범위를 분석하여 최적화된 무효화

## 📡 API 엔드포인트

### 기본 무효화
```http
# 테이블 기반 무효화
POST /api/BasicInvalidation/invalidate/table/products

# 패턴 기반 무효화
POST /api/BasicInvalidation/invalidate/pattern
{
  "pattern": "product:*"
}

# 키 기반 무효화
POST /api/BasicInvalidation/invalidate/key
{
  "key": "product:1"
}

# 상품 캐시 시나리오 데모
POST /api/BasicInvalidation/demo/product-cache-scenario
{
  "productId": 1,
  "invalidateType": "key"  # "key", "pattern", "table", or null
}
```

### 배치 무효화
```http
# 배치 테이블 무효화
POST /api/BatchInvalidation/invalidate/batch-tables
{
  "tableNames": ["products", "categories"]
}

# 배치 패턴 무효화
POST /api/BatchInvalidation/invalidate/batch-patterns
{
  "patterns": ["product:*", "products:category:*"]
}

# 배치 키 무효화
POST /api/BatchInvalidation/invalidate/batch-keys
{
  "keys": ["product:1", "product:2", "products:featured"]
}

# 대량 가격 업데이트 시나리오
POST /api/BatchInvalidation/demo/bulk-price-update
{
  "productUpdates": [
    {
      "productId": 1,
      "newPrice": 899.99,
      "newSalePrice": 799.99
    },
    {
      "productId": 2,
      "newPrice": 1299.99
    }
  ],
  "invalidationStrategy": "smart"  # "individual", "pattern", "table", "smart"
}
```

## 🔧 설정

### 개발 환경 (appsettings.Development.json)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=:memory:",
    "Redis": "localhost:6379"
  },
  "InvalidationOptions": {
    "EnablePerformanceOptimizations": false,
    "EnableBackgroundProcessing": false,
    "BatchSize": 10
  }
}
```

### 프로덕션 환경 (appsettings.json)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=.;Initial Catalog=AthenaInvalidationSample;Integrated Security=true;TrustServerCertificate=true",
    "Redis": "localhost:6379"
  },
  "InvalidationOptions": {
    "EnablePerformanceOptimizations": true,
    "EnableBackgroundProcessing": true,
    "BatchSize": 100,
    "MaxRetries": 3,
    "CircuitBreakerFailureThreshold": 5,
    "CircuitBreakerTimeoutSeconds": 30,
    "EnableDistributedInvalidation": true,
    "EnableTracking": true,
    "EnableMonitoring": true
  }
}
```

## 🧪 테스트 시나리오

### 1. 기본 캐시 시나리오
```bash
# 1. 상품 조회 (캐시됨)
curl -X GET "https://localhost:7140/api/products/1"

# 2. 같은 상품 재조회 (캐시 히트)
curl -X GET "https://localhost:7140/api/products/1"

# 3. 상품 캐시 무효화
curl -X POST "https://localhost:7140/api/BasicInvalidation/invalidate/key" \
  -H "Content-Type: application/json" \
  -d '{"key": "product:1"}'

# 4. 상품 재조회 (캐시 미스, DB에서 조회)
curl -X GET "https://localhost:7140/api/products/1"
```

### 2. 대량 업데이트 시나리오
```bash
# 대량 가격 업데이트 데모 실행
curl -X POST "https://localhost:7140/api/BatchInvalidation/demo/bulk-price-update" \
  -H "Content-Type: application/json" \
  -d '{
    "productUpdates": [
      {"productId": 1, "newPrice": 999.99},
      {"productId": 2, "newPrice": 899.99}
    ],
    "invalidationStrategy": "smart"
  }'
```

### 3. 카테고리 구조 변경 시나리오
```bash
# 카테고리 재구성 데모 실행
curl -X POST "https://localhost:7140/api/BatchInvalidation/demo/category-restructure" \
  -H "Content-Type: application/json" \
  -d '{
    "affectedCategoryIds": [1, 2, 5, 6]
  }'
```

## 📊 모니터링 및 진단

### Health Check
```bash
# 전체 상태 확인
curl https://localhost:7140/health

# 준비 상태 확인
curl https://localhost:7140/health/ready

# 살아있음 확인
curl https://localhost:7140/health/live
```

### 무효화 엔진 상태
```bash
curl https://localhost:7140/api/BasicInvalidation/status
```

### 추적된 캐시 키 확인
```bash
curl https://localhost:7140/api/BasicInvalidation/tracked-keys/products
```

## 📈 성능 권장사항

### 무효화 전략 선택
1. **키 기반** (`invalidateByKey`): 단일 항목 변경시 - 가장 정확하고 효율적
2. **패턴 기반** (`invalidateByPattern`): 관련 항목 그룹 변경시 - 균형잡힌 성능
3. **테이블 기반** (`invalidateByTable`): 대량 변경이나 스키마 변경시 - 가장 광범위
4. **스마트 전략**: 변경 범위에 따라 위 전략을 조합

### 배치 처리
- 대량 업데이트시 개별 무효화보다 배치 처리 사용
- 10-100개 항목 단위로 배치 크기 조정
- 성능과 메모리 사용량의 균형 고려

### 캐시 키 설계
- 계층적 키 구조 사용 (`table:id`, `table:group:id`)
- 와일드카드 패턴을 고려한 키 명명
- 관련 데이터 그룹화를 위한 일관된 네이밍

## 🐛 트러블슈팅

### 일반적인 문제
1. **Redis 연결 실패**: Redis 서버 상태 및 연결 문자열 확인
2. **캐시 무효화 실패**: 권한 및 네트워크 연결 확인
3. **성능 저하**: 배치 크기 및 무효화 전략 최적화

### 디버깅
- 로그 레벨을 `Debug`로 설정하여 상세 정보 확인
- Health Check 엔드포인트로 시스템 상태 모니터링
- 캐시 키 추적 기능으로 무효화 범위 확인

## 🔗 관련 문서
- [Athena.Invalidation 공식 문서](../../README.md)
- [API 문서](../../API-Documentation.md)
- [성능 가이드](../../docs/PERFORMANCE_GUIDE.md)

## 💡 라이센스
이 샘플 애플리케이션은 MIT 라이센스 하에 제공됩니다.