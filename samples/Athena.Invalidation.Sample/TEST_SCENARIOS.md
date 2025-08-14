# Athena.Invalidation Sample - 종합 테스트 시나리오

## 개요
Athena.Invalidation 라이브러리의 모든 주요 기능을 시연하는 실제 테스트 시나리오들입니다.

## 🚀 빠른 시작

### 1. 애플리케이션 실행
```bash
dotnet run --project samples/Athena.Invalidation.Sample/Athena.Invalidation.Sample.csproj
```

애플리케이션이 `http://localhost:5140`에서 실행됩니다.

## 📋 테스트 시나리오

### 시나리오 1: 기본 무효화 기능 테스트

#### 1.1 테이블 기반 무효화
```bash
curl -X POST "http://localhost:5140/api/BasicInvalidation/invalidate/table/products"
```

**예상 결과:**
```json
{
  "message": "Successfully invalidated all cache entries for table 'products'",
  "tableName": "products",
  "timestamp": "2025-08-13T11:30:35.620557Z"
}
```

#### 1.2 패턴 기반 무효화
```bash
curl -X POST "http://localhost:5140/api/BasicInvalidation/invalidate/pattern" \
  -H "Content-Type: application/json" \
  -d '{"pattern":"products:category:*"}'
```

**예상 결과:**
```json
{
  "message": "Successfully invalidated cache entries matching pattern 'products:category:*'",
  "pattern": "products:category:*",
  "timestamp": "2025-08-13T11:30:39.383697Z"
}
```

#### 1.3 키 기반 무효화
```bash
curl -X POST "http://localhost:5140/api/BasicInvalidation/invalidate/key" \
  -H "Content-Type: application/json" \
  -d '{"key":"product:1"}'
```

### 시나리오 2: 실제 비즈니스 로직과 캐싱 통합 테스트

#### 2.1 상품 캐시 시나리오 - 키 기반 무효화
```bash
curl -X POST "http://localhost:5140/api/BasicInvalidation/demo/product-cache-scenario" \
  -H "Content-Type: application/json" \
  -d '{"productId":1,"invalidateType":"key"}'
```

**기대되는 동작:**
1. 상품 데이터 조회 (캐시됨)
2. 같은 상품 재조회 (캐시 히트)
3. 카테고리별 상품 목록 조회 (캐시됨)
4. 특정 키로 캐시 무효화
5. 상품 재조회 (캐시 미스 → 데이터베이스에서 조회)

**예상 결과:**
```json
{
  "scenario": "Product Cache Demonstration",
  "productId": 1,
  "invalidationType": "key",
  "steps": [
    {
      "step": 1,
      "action": "fetch_product",
      "productId": 1,
      "productName": "iPhone 14 Pro",
      "cached": "Data is now cached"
    },
    {
      "step": 2,
      "action": "fetch_product_again",
      "result": "Cache hit - data retrieved from cache"
    },
    {
      "step": 3,
      "action": "fetch_category_products",
      "categoryId": 5,
      "productCount": 3,
      "cached": "Category products list cached"
    },
    {
      "step": 4,
      "action": "invalidate_by_key",
      "key": "product:1",
      "result": "Specific product cache invalidated"
    },
    {
      "step": 5,
      "action": "fetch_after_invalidation",
      "result": "Cache miss - data fetched from database"
    }
  ],
  "summary": {
    "message": "Demonstrated basic cache operations and invalidation strategies",
    "recommendation": "Use key-based invalidation for specific items, pattern-based for related groups, table-based for bulk changes"
  }
}
```

#### 2.2 패턴 기반 무효화 시나리오
```bash
curl -X POST "http://localhost:5140/api/BasicInvalidation/demo/product-cache-scenario" \
  -H "Content-Type: application/json" \
  -d '{"productId":2,"invalidateType":"pattern"}'
```

#### 2.3 테이블 기반 무효화 시나리오
```bash
curl -X POST "http://localhost:5140/api/BasicInvalidation/demo/product-cache-scenario" \
  -H "Content-Type: application/json" \
  -d '{"productId":3,"invalidateType":"table"}'
```

### 시나리오 3: 캐시 추적 기능 테스트

#### 3.1 추적된 캐시 키 조회
```bash
curl -X GET "http://localhost:5140/api/BasicInvalidation/tracked-keys/products"
```

**예상 결과:**
```json
{
  "tableName": "products",
  "trackedKeys": ["product:1", "products:category:5", "product:2"],
  "keyCount": 3,
  "timestamp": "2025-08-13T11:34:02.265234Z"
}
```

### 시나리오 4: 무효화 엔진 상태 모니터링

#### 4.1 엔진 상태 확인
```bash
curl -X GET "http://localhost:5140/api/BasicInvalidation/status"
```

**예상 결과:**
```json
{
  "engineStatus": {
    "isHealthy": true,
    "activeRules": 0,
    "trackedKeys": 1,
    "trackedKeysCount": 0,
    "registeredRulesCount": 0,
    "lastActivity": "0001-01-01T00:00:00+00:00",
    "uptime": "00:00:28.0283890",
    "metrics": {
      "Memory_Healthy": true,
      "Memory_TotalKeys": 1,
      "Memory_HitRatio": 0.6666666666666666
    }
  },
  "timestamp": "2025-08-13T11:34:06.958497Z"
}
```

### 시나리오 5: 규칙 기반 무효화 테스트

#### 5.1 기본 규칙 조회
```bash
curl -X GET "http://localhost:5140/api/Rules/list"
```

**예상 결과:**
```json
{
  "totalRules": 2,
  "activeRules": 2,
  "inactiveRules": 0,
  "rules": [
    {
      "id": 1,
      "name": "상품 가격 변경 - 기본",
      "description": "상품 가격이 변경되면 관련 캐시를 무효화합니다",
      "triggerTable": "products",
      "triggerEvent": "PriceChanged",
      "isActive": true,
      "priority": 1,
      "targetCount": 3
    },
    {
      "id": 2,
      "name": "카테고리 변경 - 계층적",
      "description": "카테고리가 변경되면 계층 구조 관련 캐시를 무효화합니다",
      "triggerTable": "categories",
      "triggerEvent": "Updated",
      "isActive": true,
      "priority": 1,
      "targetCount": 2
    }
  ]
}
```

#### 5.2 이벤트 기반 규칙 실행
```bash
curl -X POST "http://localhost:5140/api/Rules/execute-by-event" \
  -H "Content-Type: application/json" \
  -d '{"tableName":"products","eventType":"PriceChanged"}'
```

**예상 결과:**
```json
{
  "scenario": "이벤트 기반 규칙 실행",
  "eventInfo": {
    "tableName": "products",
    "eventType": "PriceChanged"
  },
  "execution": {
    "applicableRules": 1,
    "executedRules": 1,
    "totalInvalidations": 3
  },
  "ruleExecutions": [
    {
      "ruleId": 1,
      "ruleName": "상품 가격 변경 - 기본",
      "conditionMet": true,
      "invalidationsExecuted": 3,
      "details": [
        {
          "type": "key",
          "target": "product:{productId}",
          "executed": true
        },
        {
          "type": "pattern",
          "target": "products:category:*",
          "executed": true
        },
        {
          "type": "key",
          "target": "products:featured",
          "executed": true
        }
      ]
    }
  ]
}
```

#### 5.3 새로운 규칙 생성
```bash
curl -X POST "http://localhost:5140/api/Rules/create" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "상품 재고 변경 규칙",
    "description": "재고가 변경되면 관련 캐시를 무효화",
    "triggerTable": "products",
    "triggerEvent": "StockChanged",
    "condition": "always",
    "invalidationTargets": [
      {
        "type": "key",
        "target": "product:{productId}",
        "priority": 1
      },
      {
        "type": "pattern",
        "target": "products:category:*",
        "priority": 2
      }
    ],
    "isActive": true,
    "priority": 2
  }'
```

#### 5.4 새 규칙으로 멀티 규칙 실행
```bash
curl -X POST "http://localhost:5140/api/Rules/execute-by-event" \
  -H "Content-Type: application/json" \
  -d '{"tableName":"products","eventType":"StockChanged"}'
```

**예상 결과: 2개 규칙이 실행되어 총 5번의 무효화 작업 수행**

## 🔍 로그 모니터링

애플리케이션 실행 중 다음과 같은 로그를 확인할 수 있습니다:

### 성공적인 캐시 작업 로그
```
info: InvalidationEngine initialized with 1 cache providers and 1 strategies
info: Starting invalidation: Key - product:1
dbug: Removed key 'product:1' from memory cache
info: Invalidation completed successfully: Key - product:1 | Strategy: Basic | Count: 1 | Time: 6.418ms
```

### 캐시 히트/미스 로그
```
dbug: Cache hit for product 1
dbug: Cache miss for key 'invalidation:tracking:products'
```

## 📊 성능 지표

테스트 중 확인된 성능 지표:

- **캐시 히트율**: 66.7% (테스트 시나리오에서)
- **무효화 처리 시간**: 평균 1-18ms
- **엔진 상태**: Healthy
- **메모리 사용**: 효율적인 키 관리

## ✅ 검증 체크리스트

다음 기능들이 모두 정상 작동함을 확인했습니다:

- [x] 테이블 기반 무효화
- [x] 패턴 기반 무효화  
- [x] 키 기반 무효화
- [x] 실제 비즈니스 로직과 캐싱 통합
- [x] 캐시 추적 기능
- [x] 엔진 상태 모니터링
- [x] 규칙 기반 자동 무효화
- [x] 동적 규칙 생성/실행
- [x] 멀티 규칙 실행 (2개 규칙 → 5번 무효화)
- [x] 로그 기반 디버깅
- [x] 성능 메트릭 수집

## 🎯 실제 사용 권장사항

1. **키 기반 무효화**: 특정 항목 수정 시
2. **패턴 기반 무효화**: 관련 그룹 데이터 변경 시
3. **테이블 기반 무효화**: 대량 변경 작업 시
4. **규칙 기반 무효화**: 자동화된 비즈니스 로직에 따른 캐시 관리

이 샘플은 Athena.Invalidation 라이브러리의 완전한 기능을 실제 환경과 유사한 시나리오로 검증합니다.