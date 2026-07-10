Add-Type -AssemblyName System.Drawing

function Convert-IconToTransparentBlue {
    param(
        [string]$Path,
        [double]$PlateLum,
        [byte]$R = 70, [byte]$G = 130, [byte]$B = 180
    )

    $src = [System.Drawing.Bitmap]::FromFile($Path)
    $w = $src.Width
    $h = $src.Height
    $dst = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $src.GetPixel($x, $y)
            if ($c.A -eq 0) {
                $dst.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, $R, $G, $B))
                continue
            }
            $lum = 0.3 * $c.R + 0.59 * $c.G + 0.11 * $c.B
            $t = ($lum - $PlateLum) / (255 - $PlateLum)
            if ($t -lt 0) { $t = 0 }
            if ($t -gt 1) { $t = 1 }
            $newAlpha = [byte]([Math]::Round($c.A * $t))
            $dst.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($newAlpha, $R, $G, $B))
        }
    }
    $src.Dispose()

    $dst.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $dst.Dispose()
}

$dir = "D:\Projects\ACAD-Layer-Standardizer\installer\assets"

# Plate luminance sampled from each source image before conversion
Convert-IconToTransparentBlue -Path (Join-Path $dir "LayerStandardizer_header.png") -PlateLum (0.3*46 + 0.59*53 + 0.11*66)
Convert-IconToTransparentBlue -Path (Join-Path $dir "LayerStandardizer_sidebar.png") -PlateLum (0.3*35 + 0.59*42 + 0.11*53)

Write-Output "Done."
