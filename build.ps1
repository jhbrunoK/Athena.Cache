#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Athena.Cache 빌드 스크립트
.DESCRIPTION
    전체 솔루션을 빌드하고 테스트하는 자동화 스크립트
.PARAMETER Configuration
    빌드 구성 (Debug/Release)
.PARAMETER SkipTests
    테스트 실행을 건너뛸지 여부
.PARAMETER SkipPack
    패키지 생성을 건너뛸지 여부
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$SkipPack
)

$ErrorActionPreference = "Stop"

# 색상 출력을 위한 함수
function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) {
        Write-Output $args
    }
    else {
        $input | Write-Output
    }
    $host.UI.RawUI.ForegroundColor = $fc
}

Write-ColorOutput Green "🏛️  Athena.Cache 빌드 시작 - Configuration: $Configuration"

# 1. Restore
Write-ColorOutput Yellow "📦 의존성 복원 중..."
dotnet restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 2. Build
Write-ColorOutput Yellow "🔨 솔루션 빌드 중..."
dotnet build --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 3. Test
if (-not $SkipTests) {
    Write-ColorOutput Yellow "🧪 테스트 실행 중..."
    dotnet test --configuration $Configuration --no-build --verbosity normal --collect:"XPlat Code Coverage" --filter "Category!=Performance"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# 4. Pack
if (-not $SkipPack) {
    Write-ColorOutput Yellow "📦 NuGet 패키지 생성 중..."
    
    # artifacts/packages 디렉토리 정리
    if (Test-Path "artifacts/packages") {
        Remove-Item "artifacts/packages" -Recurse -Force
    }
    New-Item -ItemType Directory -Path "artifacts/packages" -Force | Out-Null
    
    dotnet pack --configuration $Configuration --no-build --output "artifacts/packages"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-ColorOutput Green "✅ 빌드 완료!"

# 결과 요약
Write-ColorOutput Cyan "📊 빌드 결과:"
Write-ColorOutput Cyan "  - Configuration: $Configuration"
if (-not $SkipTests) {
    Write-ColorOutput Cyan "  - 테스트: 실행됨"
} else {
    Write-ColorOutput Cyan "  - 테스트: 건너뜀"
}
if (-not $SkipPack) {
    Write-ColorOutput Cyan "  - 패키지: 생성됨 (artifacts/packages)"
    if (Test-Path "artifacts/packages") {
        $packages = Get-ChildItem "artifacts/packages" -Filter "*.nupkg" | Measure-Object
        Write-ColorOutput Cyan "  - 생성된 패키지 수: $($packages.Count)"
    }
} else {
    Write-ColorOutput Cyan "  - 패키지: 건너뜀"
}