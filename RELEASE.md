# 🚀 Athena.Cache 릴리즈 가이드

이 문서는 Athena.Cache 프로젝트의 릴리즈 관리 프로세스를 설명합니다.

## 🏷️ 버전 관리 전략

Athena.Cache는 **하이브리드 버전 관리** 시스템을 사용합니다:

### **개별 라이브러리 릴리즈**
각 패키지를 독립적으로 버전 관리하여 필요한 부분만 업데이트할 수 있습니다.

```bash
# Core 라이브러리만 업데이트 (v1.1.0)
git tag core-v1.1.0
git push origin core-v1.1.0

# Redis 제공자만 패치 (v1.0.2) 
git tag redis-v1.0.2
git push origin redis-v1.0.2

# Source Generator 릴리즈 (v1.0.0)
git tag generator-v1.0.0
git push origin generator-v1.0.0

# Analytics 모듈 업데이트 (v1.0.1)
git tag analytics-v1.0.1
git push origin analytics-v1.0.1
```

### **통합 릴리즈**
주요 기능 출시나 마케팅 목적으로 모든 패키지를 함께 릴리즈합니다.

```bash
# 모든 패키지를 v1.1.0으로 통합 릴리즈
git tag v1.1.0
git push origin v1.1.0
```

## 📦 패키지 구조

| 패키지 | 설명 | 의존성 |
|--------|------|--------|
| `Athena.Cache.Core` | 핵심 라이브러리 | - |
| `Athena.Cache.Redis` | Redis 제공자 | Core |
| `Athena.Cache.SourceGenerator` | 컴파일 타임 생성기 | - |
| `Athena.Cache.Analytics` | 분석 및 모니터링 | Core |

## 🔄 자동 배포 프로세스

### 1. 태그 생성
적절한 태그 패턴으로 버전 태그를 생성합니다.

### 2. GitHub Actions 트리거
- `.github/workflows/smart-release.yml` 워크플로우가 자동 실행
- 태그 패턴에 따라 빌드할 패키지 결정

### 3. 빌드 및 테스트
- .NET 8.0 환경에서 빌드
- 전체 테스트 스위트 실행
- GitVersion으로 버전 계산

### 4. NuGet 패키지 생성
- 해당 패키지의 .nupkg 파일 생성
- 버전별 릴리즈 노트 자동 생성

### 5. 배포
- NuGet.org에 패키지 배포
- GitHub Release 생성

## 📋 릴리즈 시나리오

### 시나리오 1: Core 라이브러리 기능 추가
```bash
# 변경사항 커밋
git add .
git commit -m "feat(core): add advanced caching patterns"

# Core만 버전업
git tag core-v1.2.0
git push origin core-v1.2.0

# 결과: Athena.Cache.Core 1.2.0만 NuGet에 배포
```

### 시나리오 2: Redis 버그 수정
```bash
# 버그 수정 커밋
git add .
git commit -m "fix(redis): resolve connection timeout issue"

# Redis만 패치 버전업
git tag redis-v1.0.3
git push origin redis-v1.0.3

# 결과: Athena.Cache.Redis 1.0.3만 NuGet에 배포
```

### 시나리오 3: 메이저 릴리즈
```bash
# 모든 변경사항이 완료된 후
git tag v2.0.0
git push origin v2.0.0

# 결과: 모든 패키지가 2.0.0으로 함께 배포
# - Athena.Cache.Core: 2.0.0
# - Athena.Cache.Redis: 2.0.0
# - Athena.Cache.SourceGenerator: 2.0.0
# - Athena.Cache.Analytics: 2.0.0
```

## 🛠️ 수동 배포 (긴급 상황)

GitHub Actions가 실패하거나 수동 배포가 필요한 경우:

```bash
# 1. 로컬에서 빌드
dotnet build --configuration Release

# 2. 패키지 생성
dotnet pack Athena.Cache.Core/Athena.Cache.Core.csproj \
  --configuration Release \
  -p:Version=1.1.0 \
  --output ./packages

# 3. NuGet 배포
dotnet nuget push ./packages/Athena.Cache.Core.1.1.0.nupkg \
  --api-key YOUR_NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

## 🔍 버전 확인

### 현재 태그 확인
```bash
# 모든 태그 보기
git tag --list

# 특정 패키지 태그만 보기
git tag --list | grep core-v
git tag --list | grep redis-v
```

### 다음 버전 계산
GitVersion을 사용하여 다음 버전을 미리 확인:

```bash
# GitVersion 설치
dotnet tool install --global GitVersion.Tool

# 현재 버전 확인
dotnet gitversion
```

## 🔙 롤백 프로세스

### 태그 삭제 (배포 전)
```bash
# 로컬 태그 삭제
git tag -d core-v1.1.0

# 원격 태그 삭제
git push origin :refs/tags/core-v1.1.0
```

### NuGet 패키지 삭제 (배포 후)
1. NuGet.org에서 패키지 관리 페이지 접속
2. 해당 버전을 "Unlist"로 설정
3. 새로운 패치 버전으로 수정사항 배포

## 📊 릴리즈 통계

### 배포 히스토리 확인
```bash
# 최근 릴리즈 태그 확인
git log --oneline --decorate --graph

# 특정 패키지의 변경 이력
git log --oneline --grep="feat(core)\|fix(core)"
```

### NuGet 다운로드 통계
- [Athena.Cache.Core](https://www.nuget.org/packages/Athena.Cache.Core/stats)
- [Athena.Cache.Redis](https://www.nuget.org/packages/Athena.Cache.Redis/stats)

## 🚨 주의사항

1. **Breaking Changes**: 메이저 버전 변경 시 충분한 문서화 필요
2. **의존성 관리**: Core 버전 변경 시 다른 패키지와의 호환성 확인
3. **테스트**: 모든 릴리즈 전 충분한 테스트 수행
4. **문서화**: 변경사항은 반드시 CHANGELOG.md에 기록

## 📞 지원

릴리즈 관련 문제가 있으면:
- GitHub Issues: [버그 리포트](https://github.com/jhbrunoK/Athena.Cache/issues)
- 이메일: bobhappy2000@gmail.com