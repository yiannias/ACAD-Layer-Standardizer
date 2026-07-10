Add-Type -AssemblyName System.Drawing

function Convert-ToTransparentBlue {
    param(
        [System.Drawing.Bitmap]$Src,
        [double]$PlateLum,
        [byte]$R = 70, [byte]$G = 130, [byte]$B = 180
    )
    $w = $Src.Width
    $h = $Src.Height
    $dst = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $srcData = $Src.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $dstData = $dst.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    $bytes = $w * $h * 4
    $srcBuf = New-Object byte[] $bytes
    [System.Runtime.InteropServices.Marshal]::Copy($srcData.Scan0, $srcBuf, 0, $bytes)
    $dstBuf = New-Object byte[] $bytes

    for ($i = 0; $i -lt $bytes; $i += 4) {
        $bch = $srcBuf[$i]; $gch = $srcBuf[$i+1]; $rch = $srcBuf[$i+2]; $ach = $srcBuf[$i+3]
        if ($ach -eq 0) {
            $dstBuf[$i] = $B; $dstBuf[$i+1] = $G; $dstBuf[$i+2] = $R; $dstBuf[$i+3] = 0
            continue
        }
        $lum = 0.3*$rch + 0.59*$gch + 0.11*$bch
        $t = ($lum - $PlateLum) / (255 - $PlateLum)
        if ($t -lt 0) { $t = 0 }
        if ($t -gt 1) { $t = 1 }
        $newAlpha = [byte]([Math]::Round($ach * $t))
        $dstBuf[$i] = $B; $dstBuf[$i+1] = $G; $dstBuf[$i+2] = $R; $dstBuf[$i+3] = $newAlpha
    }

    [System.Runtime.InteropServices.Marshal]::Copy($dstBuf, 0, $dstData.Scan0, $bytes)
    $Src.UnlockBits($srcData)
    $dst.UnlockBits($dstData)
    return $dst
}

function New-TransparentCanvas {
    param([int]$Width, [int]$Height)
    $bmp = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.Dispose()
    return $bmp
}

$dir = "D:\Projects\ACAD-Layer-Standardizer\installer\assets"
$masterPath = Join-Path $dir "LayerStandardizer.png"

$master = [System.Drawing.Bitmap]::FromFile($masterPath)
$plateCorner = $master.GetPixel(2, 2)
$plateLum = 0.3*$plateCorner.R + 0.59*$plateCorner.G + 0.11*$plateCorner.B

$converted = Convert-ToTransparentBlue -Src $master -PlateLum $plateLum
$master.Dispose()

# Content bounding box (previously measured: x=240..1072 y=214..1038, add small margin)
$cropRect = New-Object System.Drawing.Rectangle 232, 206, (1072-232+16), (1038-206+16)
$content = $converted.Clone($cropRect, $converted.PixelFormat)
$converted.Dispose()

$hq = {
    param($g)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
}

# --- Header icon: crisp, larger native resolution (4x the old 55x58) ---
$headerW = 220; $headerH = [int]($headerW * $content.Height / $content.Width)
$header = New-TransparentCanvas -Width $headerW -Height $headerH
$g = [System.Drawing.Graphics]::FromImage($header)
& $hq $g
$g.DrawImage($content, 0, 0, $headerW, $headerH)
$g.Dispose()
$header.Save((Join-Path $dir "LayerStandardizer_header.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$header.Dispose()

# --- Sidebar: same 164x314 canvas as before, icon resized to same proportions
# measured from the prior sidebar (content spanned x=1..163, y=76..238 -> 162x162,
# centered horizontally and vertically) but sourced from the high-res master.
$sidebarCanvasW = 164; $sidebarCanvasH = 314
$iconSize = 162
$sidebar = New-TransparentCanvas -Width $sidebarCanvasW -Height $sidebarCanvasH
$g = [System.Drawing.Graphics]::FromImage($sidebar)
& $hq $g
$destX = [int](($sidebarCanvasW - $iconSize) / 2)
$destY = [int](($sidebarCanvasH - $iconSize) / 2)
$g.DrawImage($content, $destX, $destY, $iconSize, $iconSize)
$g.Dispose()
$sidebar.Save((Join-Path $dir "LayerStandardizer_sidebar.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$sidebar.Dispose()

$content.Dispose()

Write-Output "Done. Header: ${headerW}x${headerH}, Sidebar: ${sidebarCanvasW}x${sidebarCanvasH}"
