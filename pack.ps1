#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Athena.Cache 패키지 생성 스크립트
.DESCRIPTION
    NuGet 패키지를 생성하고 검증하는 스크립트
.PARAMETER Configuration
    빌드 구성 (Debug/Release)
.PARAMETER OutputPath
    패키지 출력 경로
.PARAMETER IncludeSymbols
    심볼 패키지 포함 여부
.PARAMETER Push
    NuGet에 패키지 푸시 여부 (위험!)
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts/packages",
    [switch]$IncludeSymbols,
    [switch]$Push
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

Write-ColorOutput Green "📦 Athena.Cache 패키지 생성 - Configuration: $Configuration"

# 출력 디렉토리 정리
if (Test-Path $OutputPath) {
    Write-ColorOutput Yellow "🧹 기존 패키지 정리 중..."
    Remove-Item $OutputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

# 빌드 먼저 수행
Write-ColorOutput Yellow "🔨 패키지용 빌드 실행 중..."
dotnet build --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 패키지 생성 명령어 구성
$packArgs = @(
    "pack"
    "--configuration", $Configuration
    "--no-build"
    "--output", $OutputPath
)

if ($IncludeSymbols) {
    $packArgs += @("--include-symbols", "--include-source")
}

Write-ColorOutput Yellow "📦 NuGet 패키지 생성 중..."
Write-ColorOutput Cyan "실행 명령: dotnet $($packArgs -join ' ')"

& dotnet @packArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 생성된 패키지 정보 출력
Write-ColorOutput Yellow "📋 생성된 패키지 목록:"
$packages = Get-ChildItem $OutputPath -Filter "*.nupkg" | Sort-Object Name

foreach ($package in $packages) {
    $size = [math]::Round($package.Length / 1MB, 2)
    Write-ColorOutput Cyan "  - $($package.Name) ($size MB)"
}

# 패키지 검증
Write-ColorOutput Yellow "🔍 패키지 검증 중..."
foreach ($package in $packages) {
    if ($package.Name -notlike "*.symbols.nupkg") {
        Write-ColorOutput Cyan "검증 중: $($package.Name)"
        
        # 기본적인 패키지 구조 검증
        $tempPath = Join-Path $env:TEMP "athena-verify-$(Get-Random)"
        try {
            # NuGet 패키지 압축 해제
            Expand-Archive -Path $package.FullName -DestinationPath $tempPath
            
            # 필수 파일 확인
            $requiredFiles = @("*.nuspec")
            foreach ($pattern in $requiredFiles) {
                $files = Get-ChildItem $tempPath -Filter $pattern -Recurse
                if ($files.Count -eq 0) {
                    Write-ColorOutput Red "⚠️  필수 파일 누락: $pattern"
                } else {
                    Write-ColorOutput Green "✅ $pattern 확인됨"
                }
            }
        }
        finally {
            if (Test-Path $tempPath) {
                Remove-Item $tempPath -Recurse -Force
            }
        }
    }
}

# NuGet 푸시 (매우 위험한 작업이므로 확인 필요)
if ($Push) {
    Write-ColorOutput Red "⚠️  NuGet 푸시는 위험한 작업입니다!"
    $confirmation = Read-Host "정말로 NuGet에 푸시하시겠습니까? (yes/no)"
    
    if ($confirmation -eq "yes") {
        Write-ColorOutput Yellow "🚀 NuGet에 패키지 푸시 중..."
        
        foreach ($package in $packages) {
            if ($package.Name -notlike "*.symbols.nupkg") {
                Write-ColorOutput Cyan "푸시 중: $($package.Name)"
                dotnet nuget push $package.FullName --source https://api.nuget.org/v3/index.json
                if ($LASTEXITCODE -ne 0) { 
                    Write-ColorOutput Red "❌ 푸시 실패: $($package.Name)"
                    exit $LASTEXITCODE 
                }
            }
        }
    } else {
        Write-ColorOutput Yellow "푸시 작업이 취소되었습니다."
    }
}

Write-ColorOutput Green "✅ 패키지 생성 완료!"

# 결과 요약
Write-ColorOutput Cyan "📊 패키지 생성 결과:"
Write-ColorOutput Cyan "  - Configuration: $Configuration"
Write-ColorOutput Cyan "  - 출력 경로: $OutputPath"
Write-ColorOutput Cyan "  - 생성된 패키지 수: $($packages.Count)"
if ($IncludeSymbols) {
    Write-ColorOutput Cyan "  - 심볼 패키지: 포함됨"
}
if ($Push) {
    Write-ColorOutput Cyan "  - NuGet 푸시: 실행됨"
}