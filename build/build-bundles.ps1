# Modular Vests: builds the pouch model bundles in Unity (2022.3.43f1, the game's engine).
# Copies the pouch kits from assets/pouches into the Unity project, builds one bundle per
# colour variant and lays them out for the server: server/bundles/modularvests/*.bundle and
# server/bundles.json (deploy.ps1 and package.ps1 take them from there).
# The first build takes a while (8k textures are imported once).
# Usage:  pwsh -File build\build-bundles.ps1 [-UnityExe <path to Unity.exe>] [-Previews]
#   -Previews  renders front/side/top views of each model to unity/BundleOutput/previews instead
param(
    [string]$UnityExe = "",
    [switch]$Previews
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$unityProj = "$repoRoot\unity"
$kits = "$repoRoot\assets\pouches"

if (-not $UnityExe) {
    $candidates = @("C:\Program Files\Unity\Hub\Editor\2022.3.43f1\Editor\Unity.exe")
    $hubDir = "C:\Program Files\Unity\Hub\Editor"
    if (Test-Path $hubDir) {
        $candidates += Get-ChildItem $hubDir -Directory |
            Where-Object { $_.Name -like "2022.3.*" } |
            ForEach-Object { "$($_.FullName)\Editor\Unity.exe" }
    }
    $UnityExe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $UnityExe) {
        throw "Unity 2022.3.x not found. Install the 2022.3.43f1 editor, or pass -UnityExe <...>\Unity.exe"
    }
}

# --- the kits: only what the bundles need (the LOD FBX and the maps for the game's item shader:
# albedo with the reflection mask in alpha, normal, specular) ---
$models = "magpouch_07", "magpouch_02_opentop", "magpouch_03_flapper_close", "pouch_01_gadget", "pouch_03_admin", "pouch_06_survival", "frag_grenade_pouch",
           "small_pouch", "grenade_pouch", "vertical_pouch", "bottle_pouch", "med_pouch", "utility_pouch"
$source = "$unityProj\Assets\ModularVests\Source"
foreach ($model in $models) {
    $dst = "$source\$model"
    New-Item -ItemType Directory -Force "$dst\textures" | Out-Null
    $files = @(Get-Item "$kits\$model\${model}_lod_meshes.fbx") +
        @(Get-ChildItem "$kits\$model\textures" -Filter "*_albedo_*.png") +
        @(Get-ChildItem "$kits\$model\textures" -Filter "*_normal.png") +
        @(Get-ChildItem "$kits\$model\textures" -Filter "*_spec.png")
    # the old albedo jpgs of an earlier build would otherwise stay in the project, 8k each
    Get-ChildItem "$dst\textures" -Filter "*_albedo_*.jpg" -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem "$dst\textures" -Filter "*_albedo_*.jpg.meta" -ErrorAction SilentlyContinue | Remove-Item -Force
    foreach ($file in $files) {
        $target = if ($file.Extension -eq ".fbx") { "$dst\$($file.Name)" } else { "$dst\textures\$($file.Name)" }
        # unchanged files are left alone: Unity would otherwise re-import 8k textures every time
        if (-not (Test-Path $target) -or (Get-Item $target).Length -ne $file.Length -or
            (Get-Item $target).LastWriteTimeUtc -lt $file.LastWriteTimeUtc) {
            Copy-Item $file.FullName $target -Force
        }
    }
}

$method = if ($Previews) { "PouchBundleBuilder.RenderPreviews" } else { "PouchBundleBuilder.Build" }
Write-Host "=== Unity: $UnityExe -> $method" -ForegroundColor Cyan
$log = "$unityProj\build.log"
# Unity.exe is a GUI app: without Start-Process -Wait the shell would not wait for it to exit.
# No -nographics: the previews render with the GPU.
$proc = Start-Process -FilePath $UnityExe -Wait -PassThru -ArgumentList @(
    "-batchmode", "-quit",
    "-projectPath", "`"$unityProj`"",
    "-executeMethod", $method,
    "-logFile", "`"$log`""
)
if ($proc.ExitCode -ne 0) {
    if (Test-Path $log) {
        Write-Host "--- tail of $log :" -ForegroundColor Yellow
        Get-Content $log -Tail 40
    }
    throw "Unity failed (exit $($proc.ExitCode)), full log: $log"
}

Select-String -Path $log -Pattern "\[ModularVests\]" | ForEach-Object { $_.Line }
if ($Previews) {
    Write-Host "Previews -> $unityProj\BundleOutput\previews" -ForegroundColor Green
    return
}

# --- into the repository: deploy.ps1 / package.ps1 lay them out next to the server dll ---
$built = Get-ChildItem "$unityProj\BundleOutput\modularvests" -Filter *.bundle
if (-not $built) { throw "no bundles in $unityProj\BundleOutput\modularvests" }

$bundleDst = "$repoRoot\server\bundles\modularvests"
# only the pouch bundles here: build-vest-bundles.ps1 owns the vests subfolder
if (Test-Path $bundleDst) {
    Get-ChildItem $bundleDst -Filter *.bundle -File | Remove-Item -Force
}
New-Item -ItemType Directory -Force $bundleDst | Out-Null
$built | Copy-Item -Destination $bundleDst -Force

# one entry per bundle. The built-in Standard shader is packed into each bundle; the only
# dependencies are a model's shared maps bundle (normal + specular), listed by the Unity build.
$dependencies = @{}
foreach ($line in Get-Content "$unityProj\BundleOutput\dependencies.txt") {
    $key, $deps = $line -split "`t", 2
    $dependencies[$key] = @(if ($deps) { $deps -split "," })
}
$entries = @($built | Sort-Object Name | ForEach-Object {
    $key = "modularvests/$($_.Name)"
    if (-not $dependencies.ContainsKey($key)) { throw "the Unity build did not list $key in dependencies.txt" }
    [pscustomobject]@{ key = $key; dependencyKeys = @($dependencies[$key]) }
})

# the rig bundles are built by build-vest-bundles.ps1 and keep their entries: each script owns
# its own folder and its own part of the manifest, and either may run first
$manifestPath = "$repoRoot\server\bundles.json"
if (Test-Path $manifestPath) {
    $entries += @((Get-Content $manifestPath -Raw | ConvertFrom-Json).manifest |
        Where-Object { $_.key.StartsWith("modularvests/vests/") })
}
[pscustomobject]@{ manifest = @($entries | Sort-Object { $_.key }) } |
    ConvertTo-Json -Depth 5 | Set-Content $manifestPath -Encoding utf8NoBOM

$size = [math]::Round(($built | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "Bundles ($($built.Count), $size MB) -> $bundleDst, manifest -> server\bundles.json" -ForegroundColor Green
Write-Host "Next: pwsh -File build\deploy.ps1" -ForegroundColor Cyan
