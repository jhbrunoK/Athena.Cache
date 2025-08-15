# 실제 비즈니스 시나리오 테스트 가이드

## 🎯 목적
실제 운영 환경과 동일한 상황에서 캐시 무효화가 어떻게 작동하는지 보여주는 실제적인 테스트 시나리오입니다.

## 🚀 빠른 시작

### 1. 애플리케이션 실행
```bash
dotnet run --project samples/Athena.Invalidation.Sample/Athena.Invalidation.Sample.csproj
```

애플리케이션이 `http://localhost:5140`에서 실행됩니다.

## 📊 시나리오 1: 캐시 워밍업과 실제 데이터 조회

### Step 1: 캐시 상태 확인 (비어있는 상태)
```bash
curl -X GET "http://localhost:5140/api/datapreload/status"
```

**예상 결과:**
```json
{
  "preloadStatus": {
    "isPreloaded": false,
    "recommendation": "캐시가 비어있습니다. POST /api/datapreload/warmup을 실행하세요"
  },
  "cachedCounts": {
    "products": 0,
    "categories": 0,
    "totalCached": 0
  }
}
```

### Step 2: 캐시 워밍업 실행
```bash
curl -X POST "http://localhost:5140/api/datapreload/warmup?includeAll=true"
```

**예상 결과:**
```json
{
  "message": "캐시 워밍업이 완료되었습니다",
  "statistics": {
    "productsLoaded": 16,
    "categoriesLoaded": 8,
    "usersLoaded": 3,
    "totalItems": 27,
    "elapsedTimeMs": 125.5
  }
}
```

### Step 3: 캐시된 상품 조회 (캐시 히트)
```bash
curl -X GET "http://localhost:5140/api/realworld/products/1"
```

**예상 결과:**
```json
{
  "product": {
    "id": 1,
    "name": "iPhone 14 Pro",
    "price": 999.99,
    "stockQuantity": 50
  },
  "cacheInfo": {
    "cacheKey": "product:1",
    "wasCached": true,
    "isCachedNow": true,
    "cacheHit": true,
    "fetchTimeMs": 0.5,
    "dataSource": "Cache"
  },
  "message": "데이터가 캐시에서 빠르게 조회되었습니다"
}
```

## 💼 시나리오 2: 가격 변경과 자동 캐시 무효화

### Step 1: 현재 상품 정보 확인
```bash
curl -X GET "http://localhost:5140/api/realworld/products/1"
```
- 캐시 히트 확인
- 현재 가격: $999.99

### Step 2: 가격 변경 (실제 데이터 수정 + 캐시 무효화)
```bash
curl -X PUT "http://localhost:5140/api/realworld/products/1/price" \
  -H "Content-Type: application/json" \
  -d '{"newPrice": 1199.99, "newSalePrice": 1099.99}'
```

**예상 결과:**
```json
{
  "message": "상품 가격이 성공적으로 변경되고 관련 캐시가 무효화되었습니다",
  "productId": 1,
  "priceChange": {
    "oldPrice": 999.99,
    "newPrice": 1199.99,
    "priceChanged": true
  },
  "cacheInvalidation": {
    "invalidatedKeys": ["product:1"],
    "invalidatedPatterns": ["products:category:5:*"],
    "wasCached": true,
    "cacheCleared": true,
    "cacheRebuilt": true
  },
  "updatedProduct": {
    "id": 1,
    "name": "iPhone 14 Pro",
    "price": 1199.99,
    "salePrice": 1099.99
  }
}
```

### Step 3: 변경된 상품 재조회 (캐시 재구성 확인)
```bash
curl -X GET "http://localhost:5140/api/realworld/products/1"
```
- 첫 번째 조회: 캐시 미스 (DB에서 조회)
- 새로운 가격 확인: $1199.99
- 캐시 재구성 완료

## 🛒 시나리오 3: 주문 생성과 재고 업데이트

### Step 1: 주문 전 재고 확인
```bash
curl -X GET "http://localhost:5140/api/realworld/products/1"
curl -X GET "http://localhost:5140/api/realworld/products/2"
```

### Step 2: 주문 생성 (재고 감소 + 캐시 무효화)
```bash
curl -X POST "http://localhost:5140/api/realworld/orders" \
  -H "Content-Type: application/json" \
  -d '{
    "customerName": "John Doe",
    "items": [
      {"productId": 1, "quantity": 2},
      {"productId": 2, "quantity": 1}
    ]
  }'
```

**예상 결과:**
```json
{
  "message": "주문이 성공적으로 생성되고 재고가 업데이트되었습니다",
  "orderId": "abc-123-def",
  "orderSummary": {
    "customerName": "John Doe",
    "itemCount": 2,
    "totalQuantity": 3
  },
  "stockUpdates": [
    {
      "productId": 1,
      "productName": "iPhone 14 Pro",
      "oldStock": 50,
      "orderQuantity": 2,
      "newStock": 48,
      "cacheKeyInvalidated": "product:1"
    },
    {
      "productId": 2,
      "productName": "Samsung Galaxy S23",
      "oldStock": 75,
      "orderQuantity": 1,
      "newStock": 74,
      "cacheKeyInvalidated": "product:2"
    }
  ],
  "cacheInvalidation": {
    "invalidatedKeys": ["product:1", "product:2", "pattern:products:category:5:*"],
    "affectedCategories": 1,
    "totalInvalidations": 3
  }
}
```

### Step 3: 재고 변경 확인
```bash
curl -X GET "http://localhost:5140/api/realworld/products/1"
```
- 새로운 재고: 48 (기존 50에서 2개 감소)
- 캐시 재구성 완료

## 🔄 시나리오 4: 통합 플로우 테스트

### 전체 플로우 한 번에 실행
```bash
curl -X POST "http://localhost:5140/api/realworld/scenario/price-update-flow" \
  -H "Content-Type: application/json" \
  -d '{"productId": 3, "newPrice": 599.99, "newSalePrice": 549.99}'
```

**예상 결과:**
```json
{
  "scenario": "가격 변경 및 캐시 무효화 전체 플로우",
  "productId": 3,
  "steps": [
    {
      "step": 1,
      "action": "초기 상품 조회",
      "cacheHit": true,
      "fetchTimeMs": 0.5,
      "dataSource": "Cache"
    },
    {
      "step": 2,
      "action": "두 번째 조회 (캐시 히트 예상)",
      "cacheHit": true,
      "fetchTimeMs": 0.2,
      "speedup": "2.5x faster"
    },
    {
      "step": 3,
      "action": "가격 변경 및 캐시 무효화",
      "priceChange": {"oldPrice": 499.99, "newPrice": 599.99},
      "cacheInvalidated": true
    },
    {
      "step": 4,
      "action": "가격 변경 후 조회 (캐시 미스 예상)",
      "cacheHit": false,
      "fetchTimeMs": 5.2,
      "dataSource": "Database",
      "priceUpdated": true
    },
    {
      "step": 5,
      "action": "최종 조회 (캐시 재구성 확인)",
      "cacheHit": true,
      "fetchTimeMs": 0.3,
      "cacheRebuilt": true
    }
  ],
  "summary": {
    "totalSteps": 5,
    "cacheHits": 3,
    "cacheMisses": 1,
    "priceSuccessfullyUpdated": true,
    "cacheSuccessfullyInvalidated": true,
    "cacheSuccessfullyRebuilt": true
  }
}
```

## 📈 시나리오 5: 캐시 상태 모니터링

### 실시간 캐시 상태 확인
```bash
curl -X GET "http://localhost:5140/api/realworld/cache-status"
```

**예상 결과:**
```json
{
  "cacheStatus": {
    "engineHealthy": true,
    "uptime": "00:05:32",
    "totalTrackedKeys": 24
  },
  "trackedKeys": {
    "products": ["product:1", "product:2", "product:3"],
    "categories": ["category:1", "category:2"],
    "users": ["user:1"]
  },
  "cachedProducts": [
    {
      "productId": 1,
      "productName": "iPhone 14 Pro",
      "cacheKey": "product:1",
      "price": 1199.99,
      "stock": 48
    }
  ],
  "message": "현재 3개의 상품이 캐시되어 있습니다"
}
```

## 🧪 성능 비교 테스트

### 캐시 없이 조회 (캐시 리셋 후)
```bash
# 1. 캐시 초기화
curl -X POST "http://localhost:5140/api/datapreload/reset?reloadAfterReset=false"

# 2. 상품 조회 (캐시 미스)
time curl -X GET "http://localhost:5140/api/realworld/products/1"
# 응답 시간: ~10ms (DB 조회)
```

### 캐시 있는 상태에서 조회
```bash
# 1. 같은 상품 재조회 (캐시 히트)
time curl -X GET "http://localhost:5140/api/realworld/products/1"
# 응답 시간: ~1ms (캐시 조회)
```

**성능 향상: 10배 빠른 응답 시간**

## 🔑 핵심 포인트

### 1. **실제 데이터 변경**
- 가격, 재고 등이 실제로 변경됨
- 변경 사항이 데이터베이스에 저장됨

### 2. **자동 캐시 무효화**
- 데이터 변경 시 자동으로 관련 캐시 무효화
- 패턴 기반 무효화로 관련 캐시도 정리

### 3. **캐시 재구성**
- 무효화 후 첫 조회 시 자동으로 캐시 재구성
- 이후 조회는 다시 캐시에서 빠르게 처리

### 4. **성능 측정 가능**
- 각 요청의 응답 시간 측정
- 캐시 히트/미스 통계 제공
- 실제 성능 향상 확인 가능

## 📝 테스트 체크리스트

- [ ] 캐시 워밍업 실행
- [ ] 캐시 히트 확인 (빠른 응답)
- [ ] 가격 변경 후 캐시 무효화 확인
- [ ] 주문 생성 후 재고 업데이트 확인
- [ ] 캐시 미스 후 재구성 확인
- [ ] 성능 향상 측정 (캐시 있음 vs 없음)
- [ ] 패턴 기반 무효화 동작 확인
- [ ] 캐시 상태 모니터링

이 시나리오들을 통해 Athena.Invalidation 라이브러리가 실제 운영 환경에서 어떻게 작동하는지 명확하게 확인할 수 있습니다.