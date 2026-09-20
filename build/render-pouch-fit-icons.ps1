# Generate square, grayscale pouch-size placeholders as editable SVGs and transparent PNGs.
# The first dimension is horizontal (columns); the second is vertical (rows).

param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\assets\icons')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($outputPath) | Out-Null

$canvas = 128
$renderScale = 4
$sizes = @(
    @{ Columns = 1; Rows = 1 },
    @{ Columns = 1; Rows = 2 },
    @{ Columns = 2; Rows = 1 },
    @{ Columns = 2; Rows = 2 }
)

function New-RoundedRectanglePath([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

foreach ($size in $sizes) {
    $columns = [int]$size.Columns
    $rows = [int]$size.Rows
    $label = "${columns}×${rows}"
    $name = "pouch-fit-${columns}x${rows}"
    $width = 8 + 42 * $columns
    $height = 8 + 32 * $rows
    $x = ($canvas - $width) / 2
    $y = 9 + (76 - $height) / 2
    $flapX = $x + 3
    $flapY = $y + 3
    $flapWidth = $width - 6
    $buckleX = 59
    $buckleY = $y + 13

    $svgLines = @(
        '<svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="0 0 128 128" role="img">',
        "  <title>Pouch fit ${columns}x${rows}</title>",
        "  <rect x=`"$x`" y=`"$y`" width=`"$width`" height=`"$height`" rx=`"5`" fill=`"#242424`" stroke=`"#F2F2F2`" stroke-width=`"2.5`"/>"
    )
    if ($columns -eq 2) {
        $svgLines += "  <path d=`"M 64 $($y + 23) V $($y + $height - 4)`" stroke=`"#686868`" stroke-width=`"1.4`"/>"
    }
    if ($rows -eq 2) {
        $svgLines += "  <path d=`"M $($x + 4) $($y + 45) H $($x + $width - 4)`" stroke=`"#686868`" stroke-width=`"1.4`"/>"
    }
    for ($row = 0; $row -lt $rows; $row++) {
        $barY = if ($rows -eq 1) { $y + 28 } else { $y + 31 + 22 * $row }
        for ($column = 0; $column -lt $columns; $column++) {
            $barX = $x + 11 + 42 * $column
            $svgLines += "  <rect x=`"$barX`" y=`"$barY`" width=`"28`" height=`"6`" rx=`"1.5`" fill=`"#A0A0A0`"/>"
        }
    }
    $svgLines += "  <rect x=`"$flapX`" y=`"$flapY`" width=`"$flapWidth`" height=`"15`" rx=`"3`" fill=`"#676767`" stroke=`"#D0D0D0`" stroke-width=`"1.2`"/>"
    $svgLines += "  <rect x=`"$buckleX`" y=`"$buckleY`" width=`"10`" height=`"10`" rx=`"2`" fill=`"#101010`" stroke=`"#F2F2F2`" stroke-width=`"1.5`"/>"
    $svgLines += '  <rect x="26" y="93" width="76" height="28" rx="4" fill="#101010" stroke="#F0F0F0" stroke-width="2"/>'
    $svgLines += "  <text x=`"64`" y=`"114`" text-anchor=`"middle`" font-family=`"Arial, sans-serif`" font-size=`"24`" font-weight=`"700`" fill=`"#FFFFFF`">$label</text>"
    $svgLines += '</svg>'
    [System.IO.File]::WriteAllLines((Join-Path $outputPath "$name.svg"), $svgLines, [System.Text.UTF8Encoding]::new($false))

    $large = [System.Drawing.Bitmap]::new($canvas * $renderScale, $canvas * $renderScale, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($large)
    $bodyBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(36, 36, 36))
    $flapBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(103, 103, 103))
    $barBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(160, 160, 160))
    $blackBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(16, 16, 16))
    $whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $bodyPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(242, 242, 242), [single]2.5)
    $flapPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(208, 208, 208), [single]1.2)
    $seamPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(104, 104, 104), [single]1.4)
    $bucklePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(242, 242, 242), [single]1.5)
    $labelPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(240, 240, 240), [single]2)
    $font = [System.Drawing.Font]::new('Arial', [single]24, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $textFormat = [System.Drawing.StringFormat]::new()
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.ScaleTransform([single]$renderScale, [single]$renderScale)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

        $body = New-RoundedRectanglePath ([single]$x) ([single]$y) ([single]$width) ([single]$height) ([single]5)
        try {
            $graphics.FillPath($bodyBrush, $body)
            $graphics.DrawPath($bodyPen, $body)
        }
        finally { $body.Dispose() }

        if ($columns -eq 2) {
            $graphics.DrawLine($seamPen, [single]64, [single]($y + 23), [single]64, [single]($y + $height - 4))
        }
        if ($rows -eq 2) {
            $graphics.DrawLine($seamPen, [single]($x + 4), [single]($y + 45), [single]($x + $width - 4), [single]($y + 45))
        }
        for ($row = 0; $row -lt $rows; $row++) {
            $barY = if ($rows -eq 1) { $y + 28 } else { $y + 31 + 22 * $row }
            for ($column = 0; $column -lt $columns; $column++) {
                $barX = $x + 11 + 42 * $column
                $bar = New-RoundedRectanglePath ([single]$barX) ([single]$barY) ([single]28) ([single]6) ([single]1.5)
                try { $graphics.FillPath($barBrush, $bar) }
                finally { $bar.Dispose() }
            }
        }

        $flap = New-RoundedRectanglePath ([single]$flapX) ([single]$flapY) ([single]$flapWidth) ([single]15) ([single]3)
        try {
            $graphics.FillPath($flapBrush, $flap)
            $graphics.DrawPath($flapPen, $flap)
        }
        finally { $flap.Dispose() }

        $buckle = New-RoundedRectanglePath ([single]$buckleX) ([single]$buckleY) ([single]10) ([single]10) ([single]2)
        try {
            $graphics.FillPath($blackBrush, $buckle)
            $graphics.DrawPath($bucklePen, $buckle)
        }
        finally { $buckle.Dispose() }

        $labelBand = New-RoundedRectanglePath ([single]26) ([single]93) ([single]76) ([single]28) ([single]4)
        try {
            $graphics.FillPath($blackBrush, $labelBand)
            $graphics.DrawPath($labelPen, $labelBand)
        }
        finally { $labelBand.Dispose() }

        $textFormat.Alignment = [System.Drawing.StringAlignment]::Center
        $textFormat.LineAlignment = [System.Drawing.StringAlignment]::Center
        $graphics.DrawString($label, $font, $whiteBrush, [System.Drawing.RectangleF]::new(26, 91, 76, 30), $textFormat)

        $small = [System.Drawing.Bitmap]::new($canvas, $canvas, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $scaledGraphics = [System.Drawing.Graphics]::FromImage($small)
        try {
            $scaledGraphics.Clear([System.Drawing.Color]::Transparent)
            $scaledGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $scaledGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $scaledGraphics.DrawImage($large, 0, 0, $canvas, $canvas)
            $small.Save((Join-Path $outputPath "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $scaledGraphics.Dispose()
            $small.Dispose()
        }
    }
    finally {
        $textFormat.Dispose()
        $font.Dispose()
        $labelPen.Dispose()
        $bucklePen.Dispose()
        $seamPen.Dispose()
        $flapPen.Dispose()
        $bodyPen.Dispose()
        $whiteBrush.Dispose()
        $blackBrush.Dispose()
        $barBrush.Dispose()
        $flapBrush.Dispose()
        $bodyBrush.Dispose()
        $graphics.Dispose()
        $large.Dispose()
    }
}
