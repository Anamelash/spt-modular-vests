# Modular Vests build & deploy: backs up the profiles, builds both projects and lays them
# out into the game install.
# Usage:  pwsh -File build\deploy.ps1 [-Configuration Release] [-SkipBuild] [-OverwriteBones]
#
# bones.json and mounts.json are edited in place by the in-game editor (DevTools, which is
# deployed here and never packaged). A deployed copy that differs
# from the repository's is pulled back into the repository before deploying, so a deploy
# never throws away a layout made in the game; -OverwriteBones pushes the repository copies
# over it instead.
param(
    [string]$Configuration = "Release",
    [string]$GameDir = "",
    [switch]$SkipBuild,
    [switch]$OverwriteBones
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

# game path: -GameDir wins, otherwise SptGameDir from Directory.Build.props
if ($GameDir) {
    $gameDir = $GameDir
} else {
    [xml]$props = Get-Content "$repoRoot\Directory.Build.props"
    $gameDir = $props.Project.PropertyGroup.SptGameDir
    if (-not $gameDir) { throw "SptGameDir not set in Directory.Build.props" }
}
if (-not (Test-Path $gameDir)) { throw "Game directory not found: $gameDir" }

# The server half targets net10: the user-local SDK is preferred when new enough
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet) -or
    -not (& $dotnet --list-sdks | Where-Object { [int]($_ -split '\.')[0] -ge 10 })) {
    $dotnet = "dotnet"
}

foreach ($proc in "EscapeFromTarkov", "SPT.Server") {
    if (Get-Process -Name $proc -ErrorAction SilentlyContinue) {
        Write-Warning ("$proc is running - close it before deploying: files may be locked, " +
                       "and a running server rewrites the profiles on exit.")
    }
}

# --- Profile backup: before anything of the mod can touch a profile ---
$profiles = "$gameDir\SPT_Runtime\user\profiles"
if (Test-Path $profiles) {
    $files = Get-ChildItem $profiles -Filter *.json -File
    if ($files) {
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        # not under profiles\backups: SPT's own backup service owns that folder and parses its names
        $backup = "$gameDir\SPT_Runtime\user\modularvests-backups\$stamp"
        New-Item -ItemType Directory -Force $backup | Out-Null
        $files | Copy-Item -Destination $backup
        Write-Host "Profiles ($($files.Count)) backed up -> $backup" -ForegroundColor Green
    }
}

if (-not $SkipBuild) {
    Write-Host "=== Building ModularVests.Server ($Configuration)" -ForegroundColor Cyan
    & $dotnet build "$repoRoot\server\ModularVests.Server\ModularVests.Server.csproj" -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Server build failed" }

    Write-Host "=== Building ModularVests.Client ($Configuration)" -ForegroundColor Cyan
    & $dotnet build "$repoRoot\client\ModularVests.Client\ModularVests.Client.csproj" -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Client build failed" }

    Write-Host "=== Building ModularVests.DevTools ($Configuration)" -ForegroundColor Cyan
    & $dotnet build "$repoRoot\client\ModularVests.DevTools\ModularVests.DevTools.csproj" -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "DevTools build failed" }
}

# --- Server -> SPT_Runtime/user/mods/ModularVests ---
$serverDst = "$gameDir\SPT_Runtime\user\mods\ModularVests"
New-Item -ItemType Directory -Force "$serverDst\mod-files" | Out-Null
Copy-Item "$repoRoot\server\ModularVests.Server\bin\$Configuration\ModularVests.Server.dll" $serverDst -Force
Copy-Item "$repoRoot\server\mod-files\*" "$serverDst\mod-files" -Recurse -Force

# pouch models (build-bundles.ps1): the server picks up bundles.json next to the dll
if (Test-Path "$repoRoot\server\bundles.json") {
    if (Test-Path "$serverDst\bundles") { Remove-Item "$serverDst\bundles" -Recurse -Force }
    Copy-Item "$repoRoot\server\bundles" $serverDst -Recurse -Force
    Copy-Item "$repoRoot\server\bundles.json" $serverDst -Force
} else {
    Write-Warning "server\bundles.json not found - pouch models are missing, run build\build-bundles.ps1"
}
Write-Host "Server -> $serverDst" -ForegroundColor Green

# --- Client -> BepInEx/plugins/ModularVests ---
$clientDst = "$gameDir\BepInEx\plugins\ModularVests"
New-Item -ItemType Directory -Force $clientDst | Out-Null
Copy-Item "$repoRoot\client\ModularVests.Client\bin\$Configuration\ModularVests.Client.dll" $clientDst -Force

# the dev tools (bone editor) go to the game for development - never into a release
Copy-Item "$repoRoot\client\ModularVests.DevTools\bin\$Configuration\ModularVests.DevTools.dll" $clientDst -Force

# bones.json and mounts.json are written by the editor in the game
foreach ($name in "bones.json", "mounts.json") {
    $repoFile = "$repoRoot\client\ModularVests.Client\$name"
    $gameFile = "$clientDst\$name"
    if ((Test-Path $gameFile) -and -not $OverwriteBones -and
        (Get-FileHash $gameFile).Hash -ne (Get-FileHash $repoFile).Hash) {
        Copy-Item $gameFile $repoFile -Force
        Write-Host "${name}: the in-game copy differs - pulled into the repository (use -OverwriteBones to discard it)" -ForegroundColor Yellow
    } else {
        Copy-Item $repoFile $gameFile -Force
    }
}
Write-Host "Client + DevTools -> $clientDst" -ForegroundColor Green

Write-Host "Deploy OK" -ForegroundColor Green
