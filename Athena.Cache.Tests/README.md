/*
   # Athena.Cache 테스트 가이드
   
   ## 테스트 구조
   
   ### Unit Tests (단위 테스트)
   - `CacheKeyGeneratorTests`: 키 생성 로직 테스트
   - `CacheInvalidatorTests`: 무효화 관리자 테스트  
   - `MemoryCacheProviderTests`: MemoryCache 구현체 테스트
   
   ### Integration Tests (통합 테스트)
   - `RedisCacheProviderIntegrationTests`: Redis 통합 테스트 (Testcontainers 사용)
   - `WebApplicationIntegrationTests`: ASP.NET Core 전체 통합 테스트
   
   ### Performance Tests (성능 테스트)
   - `PerformanceTests`: 대용량 작업 성능 테스트
   - `StressTests`: 동시성 및 안정성 테스트
   
   ### End-to-End Tests (E2E 테스트)
   - `EndToEndScenarioTests`: 전체 워크플로우 테스트
   
   ## 실행 방법
   
   ```bash
   # 모든 테스트 실행
   dotnet test
   
   # 특정 카테고리만 실행
   dotnet test --filter Category=Unit
   dotnet test --filter Category=Integration
   
   # 커버리지 포함 실행
   dotnet test --collect:"XPlat Code Coverage"
   ```
   
   ## 필수 조건
   
   ### Redis Integration Tests
   - Docker 설치 필요 (Testcontainers 사용)
   - 또는 로컬 Redis 서버 (localhost:6379)
   
   ### Performance Tests  
   - Release 모드에서 실행 권장
   - 충분한 메모리 (최소 4GB)
   
   ## 테스트 데이터
   
   통합 테스트는 자동으로 테스트 데이터를 생성하고 정리합니다.
   Redis 테스트는 Testcontainers로 격리된 환경에서 실행됩니다.
*/