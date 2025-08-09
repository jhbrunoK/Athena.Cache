# Athena.Cache 개발 도구

이 디렉토리에는 Athena.Cache 프로젝트 개발을 위한 유용한 도구와 스크립트가 포함되어 있습니다.

## 빌드 스크립트

### PowerShell 스크립트 (Windows/Cross-platform)

#### `build.ps1` - 전체 빌드 자동화
```powershell
# 기본 Release 빌드
./build.ps1

# Debug 빌드
./build.ps1 -Configuration Debug

# 테스트 없이 빌드
./build.ps1 -SkipTests

# 패키징 없이 빌드
./build.ps1 -SkipPack
```

#### `test.ps1` - 테스트 실행
```powershell
# 모든 테스트 실행 (Performance 제외)
./test.ps1

# 특정 카테고리 테스트
./test.ps1 -Category Unit
./test.ps1 -Category Integration
./test.ps1 -Category Performance

# 코드 커버리지 포함
./test.ps1 -Coverage

# 상세 출력
./test.ps1 -Verbose
```

#### `pack.ps1` - 패키지 생성
```powershell
# 기본 패키지 생성
./pack.ps1

# 심볼 패키지 포함
./pack.ps1 -IncludeSymbols

# 커스텀 출력 경로
./pack.ps1 -OutputPath "custom/path"

# NuGet에 푸시 (위험!)
./pack.ps1 -Push
```

### Bash 스크립트 (Unix/Linux/macOS)

#### `build.sh` - 전체 빌드 자동화
```bash
# 기본 Release 빌드
./build.sh

# Debug 빌드
./build.sh --configuration Debug

# 테스트 없이 빌드
./build.sh --skip-tests

# 패키징 없이 빌드
./build.sh --skip-pack
```

## 개발 환경

### Docker Compose
로컬 개발을 위한 서비스들을 쉽게 실행할 수 있습니다:

```bash
# Redis + SQL Server 시작
docker-compose -f docker-compose.dev.yml up -d

# 서비스 중지
docker-compose -f docker-compose.dev.yml down

# 볼륨까지 삭제
docker-compose -f docker-compose.dev.yml down -v
```

포함된 서비스:
- **Redis** (localhost:6379) - 패스워드: `devpassword`
- **Redis Commander** (localhost:8081) - 관리자: admin/admin  
- **SQL Server** (localhost:1433) - SA 패스워드: `DevPassword123!`

### 개발 환경 설정

프로젝트에는 다음 설정 파일들이 포함되어 있습니다:

- **`.editorconfig`** - 코드 스타일 통일
- **`global.json`** - .NET SDK 버전 고정
- **`Directory.Build.props`** - MSBuild 공통 설정

## 권장 개발 워크플로

1. **코드 변경 후 빌드**:
   ```bash
   ./build.sh
   ```

2. **특정 테스트 실행**:
   ```bash
   ./test.ps1 -Category Unit -Verbose
   ```

3. **성능 테스트 실행**:
   ```bash
   ./test.ps1 -Category Performance
   ```

4. **패키지 생성 및 검증**:
   ```bash
   ./pack.ps1 -IncludeSymbols
   ```

## 디렉토리 구조

```
Athena.Cache/
├── src/                    # 소스 코드
├── tests/                  # 테스트 코드  
├── samples/               # 샘플 애플리케이션
├── tools/                 # 개발 도구 (이 디렉토리)
├── artifacts/             # 빌드 산출물
│   ├── packages/         # NuGet 패키지
│   └── coverage/         # 커버리지 리포트
├── docs/                  # 문서
└── dashboards/           # 모니터링 대시보드
```

## 성능 프로파일링

### BenchmarkDotNet
성능 테스트는 BenchmarkDotNet을 사용합니다:

```bash
dotnet run --project tests/Athena.Cache.Tests --configuration Release --filter "Category=Performance"
```

### 메모리 프로파일링
dotMemory나 PerfView를 사용하여 메모리 사용량을 분석할 수 있습니다.

## CI/CD 통합

GitHub Actions는 이러한 스크립트들을 사용합니다:
- 빌드: `./build.ps1`
- 테스트: `./test.ps1 -Coverage`
- 패키징: `./pack.ps1`

로컬에서 CI/CD와 동일한 환경으로 테스트할 수 있습니다.