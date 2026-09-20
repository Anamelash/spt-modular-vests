# Modular Vests: builds the recoloured rig bundles.
#
# A rig of the line-up wears its donor's model in one of the mod's five colours. The bundle is
# not rebuilt in Unity: build/tools/ModularVests.BundleTool clones the donor's own bundle, puts
# the recoloured albedo in it and leaves the meshes and the other maps in the donor's bundle,
# which the game loads first (dependencyKeys).
#
# Inputs:  server/mod-files/items.jsonc (which rigs and colours exist)
#          assets/vests/<kit>/manifest.json + status.json (which textures, and which are approved)
#          the game and WTT bundles the donors use, read only
# Outputs: server/bundles/modularvests/vests/*.bundle and the vests half of server/bundles.json
#          (build-bundles.ps1 owns the pouch half; either script may run first)
#
# Usage:  pwsh -File build\build-vest-bundles.ps1 [-Carrier otv] [-GameDir <path>]
param(
    [string]$Carrier = "",
    [string]$GameDir = "",
    [string]$Configuration = "Release",
    # builds recolours the author has not signed off on yet ("review" in status.json), for looking
    # at them in the game before the renders are approved
    [switch]$IncludeReview
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $GameDir) {
    [xml]$props = Get-Content "$repoRoot\Directory.Build.props"
    $GameDir = $props.Project.PropertyGroup.SptGameDir
}
if (-not (Test-Path $GameDir)) { throw "Game directory not found: $GameDir" }

$gameBundles = "$GameDir\EscapeFromTarkov_Data\StreamingAssets\Windows"
$wttBundles = "$GameDir\SPT_Runtime\user\mods\WTT-ContentBackport\bundles"

# The bundles every armoured rig of the game leans on. They are named by the file inside them,
# so the clone's own references can be turned back into the keys the game loads by.
$commonBundles = @{
    "CAB-56d919bd5479d38f741da52a6beef92f" = "shaders"
    "CAB-4d8a4131cf377709ee7c7e960f65d349" = "cubemaps"
    "CAB-c4ae8b769ad32b6f6553d77ccc5aff87" = "assets/commonassets/physics/physicsmaterials.bundle"
}

$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet) -or
    -not (& $dotnet --list-sdks | Where-Object { [int]($_ -split '\.')[0] -ge 10 })) {
    $dotnet = "dotnet"
}

$toolProj = "$repoRoot\build\tools\ModularVests.BundleTool\ModularVests.BundleTool.csproj"
& $dotnet build $toolProj -c $Configuration -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "the bundle tool did not build" }
$tool = "$repoRoot\build\tools\ModularVests.BundleTool\bin\$Configuration\net10.0-windows\ModularVests.BundleTool.exe"
if (-not (Test-Path $tool)) { throw "the bundle tool is not at $tool" }

function Read-Jsonc([string]$path) {
    $text = (Get-Content $path -Raw) -split "`n" | ForEach-Object { $_ -replace '^\s*//.*$', '' }
    return ($text -join "`n") | ConvertFrom-Json -AsHashtable
}

$items = Read-Jsonc "$repoRoot\server\mod-files\items.jsonc"
$status = (Get-Content "$repoRoot\assets\vests\status.json" -Raw | ConvertFrom-Json -AsHashtable).assets
$levels = Get-Content "$repoRoot\assets\vests\levels.json" -Raw | ConvertFrom-Json -AsHashtable

# What the material multiplies the albedo by, so that a colour reaches the screen at the level
# levels.json asks for whichever carrier wears it. Replaces the donor's own _Color.
function Albedo-Tint([string]$png, [string]$colour) {
    $target = $levels.targetLuma[$colour]
    if (-not $target) { throw "levels.json has no target for '$colour'" }
    # the tool prints invariant numbers; parse them that way whatever the machine's locale is
    $measured = [double]::Parse((& $tool mean --png $png).Split(" ")[3],
        [Globalization.CultureInfo]::InvariantCulture)
    if ($measured -le 0) { throw "$png is black through and through" }
    $gain = $target / $measured
    if ($gain -lt $levels.limits.min -or $gain -gt $levels.limits.max) {
        throw ("{0} would need x{1:N2} to reach {2}, outside the range levels.json allows ({3}-{4}): " +
               "the recolour is wrong, not the level" -f
               $png, $gain, $target, $levels.limits.min, $levels.limits.max)
    }
    $text = $gain.ToString("0.####", [Globalization.CultureInfo]::InvariantCulture)
    return "$text,$text,$text"
}

# The donor's bundle: a kit built from one bundle has only that one; a carrier that shares a kit
# with its siblings (the three IOTV kits) is told apart by the bundle its own donor uses.
$gameItems = $null
function Donor-Bundle([hashtable]$manifest, [string]$cloneTpl) {
    $own = @($manifest.bundles | Where-Object { $_.role -eq "prefab+textures" })
    if ($own.Count -eq 1) { return $own[0] }

    if ($null -eq $script:gameItems) {
        Write-Host "Reading the game's item database..." -ForegroundColor DarkGray
        $script:gameItems = Get-Content "$GameDir\SPT_Runtime\SPT_Data\database\templates\items.json" -Raw |
            ConvertFrom-Json -AsHashtable
    }
    $donor = $script:gameItems[$cloneTpl]
    if (-not $donor) { throw "donor $cloneTpl is not in the game's item database" }
    $path = $donor._props.Prefab.path
    $bundle = $manifest.bundles | Where-Object { $_.key -eq $path }
    if (-not $bundle) { throw "the recolour kit does not list the donor's bundle $path" }
    return $bundle
}

function Source-Path([hashtable]$bundle) {
    $root = if ($bundle.origin -eq "wtt") { $wttBundles } else { $gameBundles }
    $path = Join-Path $root ($bundle.key -replace '/', '\')
    if (-not (Test-Path $path)) { throw "bundle not found: $path" }
    $actual = (Get-FileHash $path -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $bundle.sha256) {
        throw "$($bundle.key) is not the bundle the recolour kit was made from " +
              "(sha256 $actual, kit says $($bundle.sha256)). The game or WTT was updated: " +
              "the kit has to be remade before its recolours can be built."
    }
    return $path
}

$outDir = "$repoRoot\server\bundles\modularvests\vests"
New-Item -ItemType Directory -Force $outDir | Out-Null
$reportDir = Join-Path ([IO.Path]::GetTempPath()) "modularvests-bundles"
New-Item -ItemType Directory -Force $reportDir | Out-Null

$built = @{}          # bundle key -> dependencyKeys
$skipped = @()
$mapsCab = @{}        # "<kit>/<colour>" -> the CAB of our clone of the kit's texture bundle

function Invoke-Clone([string]$source, [string]$key, [string]$assetName, [string[]]$replace, [string[]]$reroute,
                      [string]$tint) {
    $out = Join-Path $outDir (Split-Path $key -Leaf)
    $report = Join-Path $reportDir ((Split-Path $key -Leaf) + ".json")
    $toolArgs = @("clone", "--source", $source, "--out", $out, "--key", $key, "--report", $report)
    if ($assetName) { $toolArgs += @("--name", $assetName) }
    if ($tint) { $toolArgs += @("--tint", $tint) }
    foreach ($r in $replace) { $toolArgs += @("--replace", $r) }
    foreach ($r in $reroute) { $toolArgs += @("--reroute", $r) }

    # Out-Host: what the tool prints is for the build log, not for the caller of this function
    & $tool @toolArgs | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "could not build $key" }
    & $tool verify --source $out | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$key came out broken" }
    return Get-Content $report -Raw | ConvertFrom-Json -AsHashtable
}

# The clone points at files, the manifest needs bundle keys: everything the clone still
# references has to be a bundle we can name, or the game would load it with holes in it.
function Dependency-Keys([hashtable]$result, [hashtable]$known) {
    $keys = @()
    foreach ($cab in $result.externals) {
        if ($known.ContainsKey($cab)) { $keys += $known[$cab]; continue }
        if ($commonBundles.ContainsKey($cab)) { $keys += $commonBundles[$cab]; continue }
        throw "$($result.key) points at $cab, which is not a bundle this script knows how to name"
    }
    return , @($keys | Select-Object -Unique)
}

foreach ($vest in $items.vests) {
    if ($Carrier -and $vest.key -ne $Carrier) { continue }
    $kit = if ($vest.model) { $vest.model } else { $vest.key }
    $manifestPath = "$repoRoot\assets\vests\$kit\manifest.json"
    if (-not (Test-Path $manifestPath)) { throw "rig '$($vest.key)': no recolour kit at assets\vests\$kit" }
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable

    $donorBundle = Donor-Bundle $manifest $vest.cloneTpl
    $donorPath = Source-Path $donorBundle

    # A kit whose textures sit in a bundle of their own (IOTV) gets one clone of that bundle per
    # colour, shared by the carriers built on it; the prefab clones are pointed at it.
    $textureBundle = $manifest.bundles | Where-Object { $_.role -eq "textures" } | Select-Object -First 1

    Write-Host "=== $($vest.key) ($kit, $(Split-Path $donorBundle.key -Leaf))" -ForegroundColor Cyan
    foreach ($variant in $vest.variants) {
        $colour = $variant.key
        $state = $status["$kit/$colour"]
        $usable = $state -and ($state.state -eq "approved" -or ($IncludeReview -and $state.state -eq "review"))
        if (-not $usable) {
            $why = "recolour is '$(if ($state) { $state.state } else { 'missing' })'"
            # a bundle from an earlier build is left where it is, but it no longer matches the kit
            $stale = Join-Path $outDir "$($vest.key)_$colour.bundle"
            if (Test-Path $stale) { $why += "; THE BUNDLE ON DISK IS FROM AN EARLIER RECOLOUR" }
            $skipped += "$($vest.key) $colour ($why)"
            continue
        }

        # the PNG a texture is replaced with, checked against the hashes the kit and the author recorded
        function Recolour([hashtable]$texture) {
            $variantFile = $texture.variants[$colour]
            if (-not $variantFile) { throw "kit '$kit' has no $colour for $($texture.name)" }
            $png = "$repoRoot\assets\vests\$kit\$($variantFile.file -replace '/', '\')"
            if (-not (Test-Path $png)) { throw "missing recolour $png" }
            $hash = (Get-FileHash $png -Algorithm SHA256).Hash.ToLower()
            if ($hash -ne $variantFile.sha256) { throw "$png does not match the hash the kit recorded" }
            if ($hash -ne $state.sha256) { throw "$png is not the recolour the author approved" }
            return $png
        }

        function Replacements([hashtable]$fromBundle) {
            $list = @()
            foreach ($texture in $manifest.textures | Where-Object { $_.bundle -eq $fromBundle.key }) {
                $list += "$($texture.pathId)=$(Recolour $texture)"
            }
            return , $list
        }

        # the albedo decides the level: the material multiplies it to reach the target colour
        $albedo = Recolour ($manifest.textures | Select-Object -First 1)
        $tint = Albedo-Tint $albedo $colour

        $reroute = @()
        $known = @{}
        $known[(& $tool cab --source $donorPath)] = $donorBundle.key

        if ($textureBundle) {
            $mapsKey = "modularvests/vests/${kit}_maps_$colour.bundle"
            $texturePath = Source-Path $textureBundle
            $textureCab = & $tool cab --source $texturePath
            if (-not $built.ContainsKey($mapsKey)) {
                # no material in a texture bundle, so nothing to tint there
                $maps = Invoke-Clone $texturePath $mapsKey "" (Replacements $textureBundle) @() ""
                $built[$mapsKey] = Dependency-Keys $maps @{ $textureCab = $textureBundle.key }
                $mapsCab["$kit/$colour"] = $maps.cab
            }

            # only the recoloured textures come from our clone: the normal and specular maps are
            # not in it, and taking them from it too would leave the material with no maps at all
            $moved = @($manifest.textures | Where-Object { $_.bundle -eq $textureBundle.key } |
                ForEach-Object { $_.pathId })
            $reroute += "$textureCab=$($mapsCab["$kit/$colour"]):$($moved -join ',')"
            $known[$mapsCab["$kit/$colour"]] = $mapsKey
            $known[$textureCab] = $textureBundle.key
        }

        $key = "modularvests/vests/$($vest.key)_$colour.bundle"
        $result = Invoke-Clone $donorPath $key "$($vest.key)_$colour" (Replacements $donorBundle) $reroute $tint
        $built[$key] = Dependency-Keys $result $known
    }
}

# --- the manifest: this script owns modularvests/vests/, build-bundles.ps1 owns the pouches ---
# A rig bundle this run did not build keeps its entry as long as its file is there: a run for one
# carrier must not throw the others out, and a variant waiting for approval keeps the bundle an
# earlier run made of it. An entry whose file is gone goes with it.
$manifestPath = "$repoRoot\server\bundles.json"
$entries = @()
$kept = 0
if (Test-Path $manifestPath) {
    foreach ($entry in (Get-Content $manifestPath -Raw | ConvertFrom-Json).manifest) {
        if (-not $entry.key.StartsWith("modularvests/vests/")) { $entries += $entry; continue }
        if ($built.ContainsKey($entry.key)) { continue }
        if (Test-Path (Join-Path $outDir (Split-Path $entry.key -Leaf))) { $entries += $entry; $kept++ }
    }
}
foreach ($key in $built.Keys) {
    $entries += [pscustomobject]@{ key = $key; dependencyKeys = $built[$key] }
}
$sorted = @($entries | Sort-Object { $_.key })
[pscustomobject]@{ manifest = $sorted } | ConvertTo-Json -Depth 6 |
    Set-Content $manifestPath -Encoding utf8NoBOM

$files = Get-ChildItem $outDir -Filter *.bundle
$size = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "Rig bundles ($($files.Count), $size MB) -> $outDir" -ForegroundColor Green
if ($kept -gt 0) {
    Write-Host "$kept bundle(s) kept from an earlier run" -ForegroundColor DarkGray
}
if ($skipped) {
    Write-Host "Not built ($($skipped.Count)):" -ForegroundColor Yellow
    $skipped | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}
Write-Host "Next: pwsh -File build\deploy.ps1" -ForegroundColor Cyan
