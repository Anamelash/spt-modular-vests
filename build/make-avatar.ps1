# Modular Vests: makes the trader's avatar from the source portrait.
# A one-off step, not part of build/deploy: the result is committed.
# The trader list shows the avatar small, so the portrait is cropped to head and shoulders.
# Usage:  pwsh -File build\make-avatar.ps1 [-Source <png>] [-CropX 120 -CropY 0 -CropSize 880]
param(
    [string]$Source = "assets\trader\modular-vests-trader-portrait-1x1-painted.png",
    [string]$Target = "server\mod-files\trader\avatar.jpg",
    # crop square, in source pixels (the source is 1254x1254)
    [int]$CropX = 120,
    [int]$CropY = 0,
    [int]$CropSize = 880,
    [int]$Size = 512,
    [int]$Quality = 90
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repoRoot $Source
$targetPath = Join-Path $repoRoot $Target
if (-not (Test-Path $sourcePath)) { throw "Source portrait not found: $sourcePath" }

$image = [System.Drawing.Image]::FromFile($sourcePath)
try {
    if ($CropX + $CropSize -gt $image.Width -or $CropY + $CropSize -gt $image.Height) {
        throw "Crop $CropX,$CropY +$CropSize does not fit the $($image.Width)x$($image.Height) source"
    }

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $from = New-Object System.Drawing.Rectangle $CropX, $CropY, $CropSize, $CropSize
        $to = New-Object System.Drawing.Rectangle 0, 0, $Size, $Size
        $graphics.DrawImage($image, $to, $from, [System.Drawing.GraphicsUnit]::Pixel)

        $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq "image/jpeg" }
        $parameters = New-Object System.Drawing.Imaging.EncoderParameters 1
        $parameters.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality, [long]$Quality)

        New-Item -ItemType Directory -Force (Split-Path -Parent $targetPath) | Out-Null
        $bitmap.Save($targetPath, $codec, $parameters)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}
finally {
    $image.Dispose()
}

Write-Host ("Avatar: {0} ({1:N0} bytes)" -f $targetPath, (Get-Item $targetPath).Length) -ForegroundColor Green
