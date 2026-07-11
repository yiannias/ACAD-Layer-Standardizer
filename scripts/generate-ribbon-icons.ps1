# Generates the ribbon button icons (32x32 large, 16x16 small).
#
# History: these used to be downscales of the full LayerStandardizer.png
# logo (magnifying glass over a layer stack) -- good art, but far too busy
# at ribbon sizes; at 16px it read as an unreadable blob (chris,
# 2026-07-11). Now draws a purpose-made minimal glyph instead: the classic
# stacked-layers diamond -- top layer filled solid (the "standard" layer),
# the two below as open chevron outlines. White line art on transparent,
# same treatment as before, since blue doesn't read against the dark
# ribbon. Drawn at 512px with antialiasing, then downscaled.
#
# The installer icons (regenerate-installer-icons.ps1) still use the full
# logo -- plenty of room at those sizes; only the ribbon glyph is
# simplified.
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$outDir = Join-Path $root "src\AcLayerStandardizer\Resources"
New-Item -ItemType Directory -Force $outDir | Out-Null

function New-LayerGlyph {
    param(
        [int]$Canvas = 512,
        [int]$LayerCount = 3   # 3 for 32px, 2 keeps 16px from mushing
    )

    $bmp = New-Object System.Drawing.Bitmap $Canvas, $Canvas, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $white = [System.Drawing.Color]::White
    $brush = New-Object System.Drawing.SolidBrush $white
    # ~2px stroke after downscale to 32; still a solid ~1px at 16.
    $pen = New-Object System.Drawing.Pen $white, ($Canvas * 0.06)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $cx = $Canvas * 0.5
    $halfW = $Canvas * 0.42
    $halfH = $Canvas * 0.21

    # Returned with the unary-comma trick so PowerShell's pipeline doesn't
    # unroll the typed array into object[] (DrawPolygon then fails overload
    # resolution).
    function Get-Diamond([double]$cy) {
        , [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF $cx, ($cy - $halfH)),
            (New-Object System.Drawing.PointF ($cx + $halfW), $cy),
            (New-Object System.Drawing.PointF $cx, ($cy + $halfH)),
            (New-Object System.Drawing.PointF ($cx - $halfW), $cy)
        )
    }

    # Vertical spacing: chevrons peek out below the filled top layer.
    $step = $Canvas * 0.19
    $topCy = if ($LayerCount -eq 3) { $Canvas * 0.29 } else { $Canvas * 0.38 }

    # Draw bottom-up so the filled top layer cleanly overlaps the outlines.
    for ($i = $LayerCount - 1; $i -ge 1; $i--) {
        $g.DrawPolygon($pen, [System.Drawing.PointF[]](Get-Diamond ($topCy + $step * $i)))
    }
    $g.FillPolygon($brush, [System.Drawing.PointF[]](Get-Diamond $topCy))
    $g.DrawPolygon($pen, [System.Drawing.PointF[]](Get-Diamond $topCy))

    $pen.Dispose(); $brush.Dispose(); $g.Dispose()
    return $bmp
}

function Save-Downscaled {
    param([System.Drawing.Bitmap]$Src, [int]$Size, [string]$Path)
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($Src, 0, 0, $Size, $Size)
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output "Wrote $Path"
}

$glyph3 = New-LayerGlyph -LayerCount 3
Save-Downscaled -Src $glyph3 -Size 32 -Path (Join-Path $outDir "ribbon32.png")
$glyph3.Dispose()

# 16px gets the 2-layer variant: three chevrons at 16px collapse into noise.
$glyph2 = New-LayerGlyph -LayerCount 2
Save-Downscaled -Src $glyph2 -Size 16 -Path (Join-Path $outDir "ribbon16.png")
$glyph2.Dispose()
