#!/bin/bash

# Athena.Cache 빌드 스크립트 (Unix/Linux/macOS)
set -e

# 색상 정의
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# 기본 값
CONFIGURATION="Release"
SKIP_TESTS=false
SKIP_PACK=false

# 사용법 출력
usage() {
    echo "사용법: $0 [옵션]"
    echo ""
    echo "옵션:"
    echo "  -c, --configuration  빌드 구성 (Debug/Release, 기본값: Release)"
    echo "  --skip-tests         테스트 건너뛰기"
    echo "  --skip-pack          패키지 생성 건너뛰기"
    echo "  -h, --help           도움말 표시"
    echo ""
}

# 명령행 인자 파싱
while [[ $# -gt 0 ]]; do
    case $1 in
        -c|--configuration)
            CONFIGURATION="$2"
            shift 2
            ;;
        --skip-tests)
            SKIP_TESTS=true
            shift
            ;;
        --skip-pack)
            SKIP_PACK=true
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "알 수 없는 옵션: $1"
            usage
            exit 1
            ;;
    esac
done

# 구성 검증
if [[ "$CONFIGURATION" != "Debug" && "$CONFIGURATION" != "Release" ]]; then
    echo -e "${RED}오류: 구성은 Debug 또는 Release여야 합니다.${NC}"
    exit 1
fi

echo -e "${GREEN}🏛️  Athena.Cache 빌드 시작 - Configuration: $CONFIGURATION${NC}"

# 1. Restore
echo -e "${YELLOW}📦 의존성 복원 중...${NC}"
dotnet restore

# 2. Build
echo -e "${YELLOW}🔨 솔루션 빌드 중...${NC}"
dotnet build --configuration "$CONFIGURATION" --no-restore

# 3. Test
if [[ "$SKIP_TESTS" == false ]]; then
    echo -e "${YELLOW}🧪 테스트 실행 중...${NC}"
    dotnet test --configuration "$CONFIGURATION" --no-build --verbosity normal --collect:"XPlat Code Coverage" --filter "Category!=Performance"
fi

# 4. Pack
if [[ "$SKIP_PACK" == false ]]; then
    echo -e "${YELLOW}📦 NuGet 패키지 생성 중...${NC}"
    
    # artifacts/packages 디렉토리 정리
    if [[ -d "artifacts/packages" ]]; then
        rm -rf "artifacts/packages"
    fi
    mkdir -p "artifacts/packages"
    
    dotnet pack --configuration "$CONFIGURATION" --no-build --output "artifacts/packages"
fi

echo -e "${GREEN}✅ 빌드 완료!${NC}"

# 결과 요약
echo -e "${CYAN}📊 빌드 결과:${NC}"
echo -e "${CYAN}  - Configuration: $CONFIGURATION${NC}"
if [[ "$SKIP_TESTS" == false ]]; then
    echo -e "${CYAN}  - 테스트: 실행됨${NC}"
else
    echo -e "${CYAN}  - 테스트: 건너뜀${NC}"
fi
if [[ "$SKIP_PACK" == false ]]; then
    echo -e "${CYAN}  - 패키지: 생성됨 (artifacts/packages)${NC}"
    if [[ -d "artifacts/packages" ]]; then
        PACKAGE_COUNT=$(find artifacts/packages -name "*.nupkg" | wc -l)
        echo -e "${CYAN}  - 생성된 패키지 수: $PACKAGE_COUNT${NC}"
    fi
else
    echo -e "${CYAN}  - 패키지: 건너뜀${NC}"
fi