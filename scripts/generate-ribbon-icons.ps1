# Generates the ribbon button icons (32x32 large, 16x16 small) from the
# high-res master logo. Same transparent-background treatment as the
# installer icons (see regenerate-installer-icons.ps1) but recolored to
# white line art -- blue doesn't read against the dark ribbon.
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

$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$masterPath = Join-Path $root "installer\assets\LayerStandardizer.png"
$outDir = Join-Path $root "src\AcLayerStandardizer\Resources"
New-Item -ItemType Directory -Force $outDir | Out-Null

$master = [System.Drawing.Bitmap]::FromFile($masterPath)
$plateCorner = $master.GetPixel(2, 2)
$plateLum = 0.3*$plateCorner.R + 0.59*$plateCorner.G + 0.11*$plateCorner.B

$converted = Convert-ToTransparentBlue -Src $master -PlateLum $plateLum -R 255 -G 255 -B 255
$master.Dispose()

# Content bounding box, same crop as the installer icons.
$cropRect = New-Object System.Drawing.Rectangle 232, 206, (1072-232+16), (1038-206+16)
$content = $converted.Clone($cropRect, $converted.PixelFormat)
$converted.Dispose()

foreach ($size in 32, 16) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($content, 0, 0, $size, $size)
    $g.Dispose()
    $out = Join-Path $outDir "ribbon$size.png"
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output "Wrote $out"
}
$content.Dispose()
