Add-Type -AssemblyName System.Drawing

$srcPath = "C:\Users\Rush\.gemini\antigravity\brain\3e46f401-beb9-4846-919b-388b4422d461\rdp_mouse_bridge_icon_v2_1793013786119.jpg"
$src = [System.Drawing.Bitmap]::FromFile($srcPath)

# Squircle bounds in 1024x1024
$cropX = 63
$cropY = 64
$cropW = 896
$cropH = 896

$sizes = @(256, 128, 64, 48, 32, 16)
$pngDataList = New-Object System.Collections.Generic.List[byte[]]

function Get-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

foreach ($sz in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    # Smooth rounded corner squircle mask (22% radius)
    $path = Get-RoundedRectPath 0 0 $sz $sz ($sz * 0.22)
    $g.SetClip($path)
    $destRect = New-Object System.Drawing.Rectangle(0, 0, $sz, $sz)
    $srcRect = New-Object System.Drawing.Rectangle($cropX, $cropY, $cropW, $cropH)
    $g.DrawImage($src, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $path.Dispose()
    $g.Dispose()

    if ($sz -eq 256) {
        $bmp.Save((Join-Path $PSScriptRoot "app_256.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngDataList.Add($ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}
$src.Dispose()

# Save app.ico
$icoPath = Join-Path $PSScriptRoot "app.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Length)

$offset = 6 + (16 * $sizes.Length)
for ($i = 0; $i -lt $sizes.Length; $i++) {
    $sz = $sizes[$i]
    $bSz = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
    $bw.Write($bSz)
    $bw.Write($bSz)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bytesLen = [uint32]$pngDataList[$i].Length
    $bw.Write($bytesLen)
    $bw.Write([uint32]$offset)
    $offset += $bytesLen
}

for ($i = 0; $i -lt $sizes.Length; $i++) {
    $bw.Write($pngDataList[$i])
}

$bw.Flush()
$bw.Close()
$fs.Close()

# Copy original art as icon_minimal_art.png
Copy-Item "C:\Users\Rush\.gemini\antigravity\brain\3e46f401-beb9-4846-919b-388b4422d461\rdp_mouse_bridge_icon_v2_1793013786119.jpg" (Join-Path $PSScriptRoot "icon_minimal_art.png") -Force

Remove-Item "$PSScriptRoot\find_v2_bounds.ps1" -Force -ErrorAction SilentlyContinue
Write-Output "Generated new minimalist app.ico and app_256.png"
