#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build script for PLCInfo - Creates self-contained executables
.DESCRIPTION
    Publishes self-contained plcinfo.exe that doesn't require .NET runtime installation.
    Creates binaries for Windows, Linux, and macOS.
.PARAMETER Runtime
    Target runtime identifier (win-x64, linux-x64, osx-x64, osx-arm64)
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release
.PARAMETER OutputDir
    Output directory for published files. Default: ./publish
.PARAMETER Clean
    Clean previous build artifacts before building
.EXAMPLE
    .\Build.ps1
    Builds for Windows x64 (default)
.EXAMPLE
    .\Build.ps1 -Runtime linux-x64
    Builds for Linux x64
.EXAMPLE
    .\Build.ps1 -Clean
    Cleans and builds for Windows x64
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [ValidateSet('win-x64', 'win-x86', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime = 'win-x64',

    [Parameter(Mandatory=$false)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter(Mandatory=$false)]
    [string]$OutputDir = 'publish',

    [Parameter(Mandatory=$false)]
    [switch]$Clean
)

# Script configuration
$ErrorActionPreference = 'Stop'
$ProjectPath = Join-Path (Join-Path $PSScriptRoot 'PlcInfo') 'PlcInfo.csproj'
$OutputPath = Join-Path (Join-Path $PSScriptRoot $OutputDir) $Runtime

# Display banner
Write-Host ""
Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "           PLCInfo Build Script                        " -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host ""

# Verify project file exists
if (-not (Test-Path $ProjectPath)) {
    Write-Error "Project file not found: $ProjectPath"
    exit 1
}

Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Runtime:        $Runtime" -ForegroundColor Gray
Write-Host "  Configuration:  $Configuration" -ForegroundColor Gray
Write-Host "  Output:         $OutputPath" -ForegroundColor Gray
Write-Host ""

# Clean if requested
if ($Clean) {
    Write-Host "Cleaning previous build artifacts..." -ForegroundColor Yellow

    if (Test-Path $OutputPath) {
        Remove-Item -Path $OutputPath -Recurse -Force
        Write-Host "  [OK] Removed $OutputPath" -ForegroundColor Green
    }

    # Clean bin and obj directories
    $binPath = Join-Path (Split-Path $ProjectPath) 'bin'
    $objPath = Join-Path (Split-Path $ProjectPath) 'obj'

    if (Test-Path $binPath) {
        Remove-Item -Path $binPath -Recurse -Force
        Write-Host "  [OK] Removed bin directory" -ForegroundColor Green
    }

    if (Test-Path $objPath) {
        Remove-Item -Path $objPath -Recurse -Force
        Write-Host "  [OK] Removed obj directory" -ForegroundColor Green
    }

    Write-Host ""
}

# Restore dependencies
Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore $ProjectPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to restore NuGet packages"
    exit $LASTEXITCODE
}
Write-Host "  [OK] Packages restored" -ForegroundColor Green
Write-Host ""

# Build the project
Write-Host "Building project..." -ForegroundColor Yellow
dotnet build $ProjectPath `
    --configuration $Configuration `
    --no-restore

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit $LASTEXITCODE
}
Write-Host "  [OK] Build successful" -ForegroundColor Green
Write-Host ""

# Publish self-contained executable
Write-Host "Publishing self-contained executable..." -ForegroundColor Yellow
Write-Host "  Target: $Runtime" -ForegroundColor Gray

$publishArgs = @(
    'publish',
    $ProjectPath,
    '--configuration', $Configuration,
    '--runtime', $Runtime,
    '--self-contained', 'true',
    '--output', $OutputPath,
    '/p:PublishSingleFile=true',
    '/p:DebugType=None',
    '/p:DebugSymbols=false'
)

dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed"
    exit $LASTEXITCODE
}

Write-Host "  [OK] Published successfully" -ForegroundColor Green
Write-Host ""

# Display output information
Write-Host "Build complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Output directory: $OutputPath" -ForegroundColor Cyan

# Find the executable
$exeName = if ($Runtime -like 'win-*') { 'plcinfo.exe' } else { 'plcinfo' }
$exePath = Join-Path $OutputPath $exeName

if (Test-Path $exePath) {
    $exeInfo = Get-Item $exePath
    $sizeMB = [math]::Round($exeInfo.Length / 1MB, 2)

    Write-Host ""
    Write-Host "Executable details:" -ForegroundColor Yellow
    Write-Host "  Name:  $($exeInfo.Name)" -ForegroundColor Gray
    Write-Host "  Size:  $sizeMB MB" -ForegroundColor Gray
    Write-Host "  Path:  $($exeInfo.FullName)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "The executable is self-contained and does not require" -ForegroundColor Gray
    Write-Host ".NET runtime to be installed on the target system." -ForegroundColor Gray
    Write-Host ""

    # Test the executable
    Write-Host "Testing executable..." -ForegroundColor Yellow
    $testOutput = & $exePath --help 2>&1
    if ($LASTEXITCODE -eq 0 -or $LASTEXITCODE -eq 1) {
        Write-Host "  [OK] Executable runs successfully" -ForegroundColor Green
    } else {
        Write-Warning "Executable test failed with exit code: $LASTEXITCODE"
    }
} else {
    Write-Warning "Executable not found at expected path: $exePath"
}

Write-Host ""
Write-Host "To build for other platforms:" -ForegroundColor Cyan
Write-Host "  .\Build.ps1 -Runtime win-x64      # Windows 64-bit" -ForegroundColor Gray
Write-Host "  .\Build.ps1 -Runtime linux-x64    # Linux 64-bit" -ForegroundColor Gray
Write-Host "  .\Build.ps1 -Runtime osx-x64      # macOS Intel" -ForegroundColor Gray
Write-Host "  .\Build.ps1 -Runtime osx-arm64    # macOS Apple Silicon" -ForegroundColor Gray
Write-Host ""
