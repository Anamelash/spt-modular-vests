# Modular Vests: builds a release archive ready to unpack into the SPT installation root.
# Usage:  pwsh -File build\package.ps1 [-Configuration Release] [-SkipBuild]
param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet) -or
    -not (& $dotnet --list-sdks | Where-Object { [int]($_ -split '\.')[0] -ge 10 })) {
    $dotnet = "dotnet"
}

# version comes from Directory.Build.props - single source of truth
[xml]$props = Get-Content "$repoRoot\Directory.Build.props"
$version = $props.Project.PropertyGroup.ModVersion
if (-not $version) { throw "ModVersion not found in Directory.Build.props" }

if (-not $SkipBuild) {
    foreach ($proj in "server\ModularVests.Server\ModularVests.Server.csproj",
                      "client\ModularVests.Client\ModularVests.Client.csproj") {
        Write-Host "=== Building $proj ($Configuration)" -ForegroundColor Cyan
        & $dotnet build "$repoRoot\$proj" -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $proj" }
    }
}

# staging mirrors the game root, so the user unpacks the archive over it
$stage = Join-Path $repoRoot "dist\stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$clientDst = New-Item -ItemType Directory -Force "$stage\BepInEx\plugins\ModularVests"
$serverDst = New-Item -ItemType Directory -Force "$stage\SPT_Runtime\user\mods\ModularVests"
New-Item -ItemType Directory -Force "$serverDst\mod-files" | Out-Null

Copy-Item "$repoRoot\client\ModularVests.Client\bin\$Configuration\ModularVests.Client.dll" $clientDst -Force
# the plugin and its data only: the dev tools (ModularVests.DevTools) never ship
Copy-Item "$repoRoot\client\ModularVests.Client\bones.json" $clientDst -Force
Copy-Item "$repoRoot\client\ModularVests.Client\mounts.json" $clientDst -Force
Copy-Item "$repoRoot\server\ModularVests.Server\bin\$Configuration\ModularVests.Server.dll" $serverDst -Force
Copy-Item "$repoRoot\server\mod-files\*" "$serverDst\mod-files" -Recurse -Force

# pouch models (build-bundles.ps1): a release without them has pouches with no model
if (-not (Test-Path "$repoRoot\server\bundles.json")) { throw "server\bundles.json not found - run build\build-bundles.ps1" }

# rig models (build-vest-bundles.ps1): a rig whose bundle is missing loads as a red error item
$manifestKeys = @((Get-Content "$repoRoot\server\bundles.json" -Raw | ConvertFrom-Json).manifest |
    ForEach-Object { $_.key })
$itemsText = ((Get-Content "$repoRoot\server\mod-files\items.jsonc" -Raw) -split "`n" |
    ForEach-Object { $_ -replace '^\s*//.*$', '' }) -join "`n"
$items = $itemsText | ConvertFrom-Json -AsHashtable
foreach ($vest in $items.vests) {
    foreach ($variant in $vest.variants) {
        $key = $vest.prefab -replace '\{variant\}', $variant.key
        if (-not $key) { continue }
        if ($manifestKeys -notcontains $key) { throw "$key is not in server\bundles.json - run build\build-vest-bundles.ps1" }
        $file = "$repoRoot\server\bundles\" + ($key -replace '/', '\')
        if (-not (Test-Path $file)) { throw "$key is in the manifest but the file is missing - run build\build-vest-bundles.ps1" }
    }
}

Copy-Item "$repoRoot\server\bundles" $serverDst -Recurse -Force
Copy-Item "$repoRoot\server\bundles.json" $serverDst -Force

Copy-Item "$repoRoot\README.md", "$repoRoot\CHANGELOG.md" $stage -Force

$zip = "$repoRoot\dist\ModularVests-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

$size = [math]::Round((Get-Item $zip).Length / 1KB, 1)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host "Release -> $zip ($size KB)" -ForegroundColor Green
Write-Host "SHA256: $hash" -ForegroundColor Green
