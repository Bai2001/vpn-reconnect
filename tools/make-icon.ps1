# 生成应用图标与托盘状态图标（多尺寸 ICO，标准 DIB 帧，兼容所有 GDI+ 版本）
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\VpnReconnect.App\Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

function New-GlyphBitmap([int]$size, [string]$colorHex, [string]$glyph) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [single]($size / 256.0)
    $center = 128 * $s
    $radius = 124 * $s
    $rectF = [System.Drawing.RectangleF]::new($center - $radius, $center - $radius, $radius * 2, $radius * 2)

    if ($glyph -eq 'shield') {
        # 应用图标：蓝色渐变圆底 + 白色盾牌 + 蓝色对勾
        $bg = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $rectF,
            [System.Drawing.ColorTranslator]::FromHtml('#3B82F6'),
            [System.Drawing.ColorTranslator]::FromHtml('#1D4ED8'),
            [single]90)
        $g.FillEllipse($bg, $rectF)
        $bg.Dispose()

        $shield = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $shield.StartFigure()
        $shield.AddLine(78 * $s, 88 * $s, 178 * $s, 88 * $s)
        $shield.AddLine(178 * $s, 88 * $s, 178 * $s, 140 * $s)
        $shield.AddBezier(178 * $s, 140 * $s, 178 * $s, 172 * $s, 156 * $s, 190 * $s, 128 * $s, 200 * $s)
        $shield.AddBezier(128 * $s, 200 * $s, 100 * $s, 190 * $s, 78 * $s, 172 * $s, 78 * $s, 140 * $s)
        $shield.CloseFigure()
        $g.FillPath([System.Drawing.Brushes]::White, $shield)
        $shield.Dispose()

        $pen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#1D4ED8'), [single]([Math]::Max(1.6, 19 * $s)))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        [System.Drawing.PointF[]]$pts = @(
            [System.Drawing.PointF]::new(103 * $s, 136 * $s),
            [System.Drawing.PointF]::new(121 * $s, 154 * $s),
            [System.Drawing.PointF]::new(154 * $s, 116 * $s))
        $g.DrawLines($pen, $pts)
        $pen.Dispose()
    }
    else {
        $color = [System.Drawing.ColorTranslator]::FromHtml($colorHex)
        $brush = [System.Drawing.SolidBrush]::new($color)
        $g.FillEllipse($brush, $rectF)
        $brush.Dispose()

        $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, [single]([Math]::Max(1.4, 30 * $s)))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        switch ($glyph) {
            'check' {
                [System.Drawing.PointF[]]$pts = @(
                    [System.Drawing.PointF]::new(92 * $s, 130 * $s),
                    [System.Drawing.PointF]::new(118 * $s, 156 * $s),
                    [System.Drawing.PointF]::new(166 * $s, 104 * $s))
                $g.DrawLines($pen, $pts)
            }
            'cross' {
                $g.DrawLine($pen, 92 * $s, 92 * $s, 164 * $s, 164 * $s)
                $g.DrawLine($pen, 164 * $s, 92 * $s, 92 * $s, 164 * $s)
            }
            'bang' {
                $g.DrawLine($pen, 128 * $s, 74 * $s, 128 * $s, 152 * $s)
                $dotR = 19 * $s
                $g.FillEllipse([System.Drawing.Brushes]::White, 128 * $s - $dotR, 178 * $s - $dotR, $dotR * 2, $dotR * 2)
            }
            default {
                $g.DrawLine($pen, 92 * $s, 128 * $s, 164 * $s, 128 * $s)
            }
        }
        $pen.Dispose()
    }

    $g.Dispose()
    return $bmp
}

# 把位图转成 ICO 里的 DIB 帧：BITMAPINFOHEADER + 自下而上 BGRA 像素 + 全 0 AND 掩码
function Get-DibFrame([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $rect = [System.Drawing.Rectangle]::new(0, 0, $size, $size)
    $bits = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $bits.Stride
    $pixels = New-Object byte[] ($stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($bits.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($bits)

    $ms = [System.IO.MemoryStream]::new()
    $w = [System.IO.BinaryWriter]::new($ms)
    $w.Write([UInt32]40)
    $w.Write([Int32]$size)
    $w.Write([Int32]($size * 2))
    $w.Write([UInt16]1)
    $w.Write([UInt16]32)
    $w.Write([UInt32]0)
    $w.Write([UInt32]($size * $size * 4))
    $w.Write([Int32]0)
    $w.Write([Int32]0)
    $w.Write([UInt32]0)
    $w.Write([UInt32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        $w.Write($pixels, $y * $stride, $size * 4)
    }
    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskStride * $size)), 0, $maskStride * $size)
    $w.Flush()

    $frame = $ms.ToArray()
    $w.Dispose()
    $ms.Dispose()
    return $frame
}

function Write-Ico([string]$path, [int[]]$sizes, [string]$colorHex, [string]$glyph) {
    $frames = @()
    foreach ($size in $sizes) {
        $bmp = New-GlyphBitmap $size $colorHex $glyph
        $frames += , @($size, (Get-DibFrame $bmp))
        $bmp.Dispose()
    }

    $out = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($out)
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $side = 0
        if ($frame[0] -lt 256) { $side = $frame[0] }
        $bw.Write([Byte]$side)
        $bw.Write([Byte]$side)
        $bw.Write([Byte]0)
        $bw.Write([Byte]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]32)
        $bw.Write([UInt32]$frame[1].Length)
        $bw.Write([UInt32]$offset)
        $offset += $frame[1].Length
    }
    foreach ($frame in $frames) {
        $bw.Write([Byte[]]$frame[1])
    }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($path, $out.ToArray())
    $bw.Dispose()
    $out.Dispose()
    "生成 $(Split-Path $path -Leaf)：$($frames.Count) 个尺寸，$([System.IO.File]::ReadAllBytes($path).Length) 字节"
}

$traySizes = @(16, 20, 24, 32, 48)
Write-Ico (Join-Path $assets 'app.ico') @(16, 24, 32, 48, 64, 128) '#3B82F6' 'shield'
Write-Ico (Join-Path $assets 'tray-ok.ico') $traySizes '#16A34A' 'check'
Write-Ico (Join-Path $assets 'tray-bad.ico') $traySizes '#DC2626' 'cross'
Write-Ico (Join-Path $assets 'tray-warn.ico') $traySizes '#D97706' 'bang'
Write-Ico (Join-Path $assets 'tray-idle.ico') $traySizes '#6B7280' 'dash'

# 自检预览图，看完可删（tools/icon-preview.png）
$preview = [System.Drawing.Bitmap]::new(260, 64)
$pg = [System.Drawing.Graphics]::FromImage($preview)
$pg.Clear([System.Drawing.Color]::White)
$names = @('tray-idle.ico', 'tray-ok.ico', 'tray-bad.ico', 'tray-warn.ico', 'app.ico')
for ($i = 0; $i -lt $names.Count; $i++) {
    $icon = [System.Drawing.Icon]::new((Join-Path $assets $names[$i]), 32, 32)
    $pg.DrawIcon($icon, 12 + $i * 48, 16)
    $icon.Dispose()
}
$preview.Save((Join-Path $PSScriptRoot 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$pg.Dispose()
$preview.Dispose()
