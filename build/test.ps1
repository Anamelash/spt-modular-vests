# Modular Vests: runs the test suite.
# Usage:  pwsh -File build\test.ps1 [-Configuration Debug]
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet) -or
    -not (& $dotnet --list-sdks | Where-Object { [int]($_ -split '\.')[0] -ge 10 })) {
    $dotnet = "dotnet"
}

& $dotnet test "$repoRoot\tests\ModularVests.Server.Tests\ModularVests.Server.Tests.csproj" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

Write-Host "Tests OK" -ForegroundColor Green
