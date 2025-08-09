#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Athena.Cache 테스트 실행 스크립트
.DESCRIPTION
    다양한 테스트 카테고리를 실행하는 스크립트
.PARAMETER Category
    실행할 테스트 카테고리
.PARAMETER Configuration
    빌드 구성 (Debug/Release)
.PARAMETER Coverage
    코드 커버리지 수집 여부
.PARAMETER Verbose
    상세 출력 여부
#>

param(
    [ValidateSet("All", "Unit", "Integration", "Performance", "Stress", "EndToEnd")]
    [string]$Category = "All",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Coverage,
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

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

Write-ColorOutput Green "🧪 Athena.Cache 테스트 실행 - Category: $Category, Configuration: $Configuration"

# 빌드 먼저 수행
Write-ColorOutput Yellow "🔨 테스트용 빌드 실행 중..."
dotnet build --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 테스트 필터 설정
$filter = ""
switch ($Category) {
    "Unit" { $filter = "--filter Category=Unit" }
    "Integration" { $filter = "--filter Category=Integration" }
    "Performance" { $filter = "--filter Category=Performance" }
    "Stress" { $filter = "--filter Category=Stress" }
    "EndToEnd" { $filter = "--filter Category=EndToEnd" }
    "All" { $filter = "--filter Category!=Performance" }  # Performance 제외
}

# 테스트 명령어 구성
$testArgs = @(
    "test"
    "--configuration", $Configuration
    "--no-build"
)

if ($filter) {
    $testArgs += $filter.Split(" ")
}

if ($Coverage) {
    $testArgs += @("--collect:XPlat Code Coverage")
    
    # artifacts/coverage 디렉토리 정리
    if (Test-Path "artifacts/coverage") {
        Remove-Item "artifacts/coverage" -Recurse -Force
    }
    New-Item -ItemType Directory -Path "artifacts/coverage" -Force | Out-Null
}

if ($Verbose) {
    $testArgs += @("--verbosity", "normal")
}

Write-ColorOutput Yellow "🧪 테스트 실행 중..."
Write-ColorOutput Cyan "실행 명령: dotnet $($testArgs -join ' ')"

& dotnet @testArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 커버리지 리포트 생성 (Coverage가 활성화된 경우)
if ($Coverage) {
    Write-ColorOutput Yellow "📊 커버리지 리포트 생성 중..."
    
    # TestResults에서 coverage 파일 찾기
    $coverageFiles = Get-ChildItem -Recurse -Path . -Filter "coverage.cobertura.xml" | Select-Object -ExpandProperty FullName
    
    if ($coverageFiles) {
        # 커버리지 파일을 artifacts로 복사
        foreach ($file in $coverageFiles) {
            $destinationFile = "artifacts/coverage/coverage-$(Get-Date -Format 'yyyyMMdd-HHmmss').cobertura.xml"
            Copy-Item $file $destinationFile
            Write-ColorOutput Cyan "커버리지 파일 생성: $destinationFile"
        }
        
        # reportgenerator가 있는 경우 HTML 리포트 생성
        if (Get-Command "reportgenerator" -ErrorAction SilentlyContinue) {
            Write-ColorOutput Yellow "📈 HTML 커버리지 리포트 생성 중..."
            reportgenerator "-reports:artifacts/coverage/*.xml" "-targetdir:artifacts/coverage/html" "-reporttypes:Html"
            Write-ColorOutput Cyan "HTML 리포트: artifacts/coverage/html/index.html"
        }
    }
}

Write-ColorOutput Green "✅ 테스트 완료!"

# 결과 요약
Write-ColorOutput Cyan "📊 테스트 결과:"
Write-ColorOutput Cyan "  - Category: $Category"
Write-ColorOutput Cyan "  - Configuration: $Configuration" 
if ($Coverage) {
    Write-ColorOutput Cyan "  - 커버리지: 수집됨 (artifacts/coverage)"
}