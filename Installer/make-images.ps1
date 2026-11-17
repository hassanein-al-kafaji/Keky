# Generates the installer wizard images (run once, outputs are committed next to this script).
Add-Type -AssemblyName System.Drawing

function New-Badge([System.Drawing.Graphics]$g, [float]$x, [float]$y, [float]$size) {
    $radius = $size * 0.29
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $size - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $size - $d, $y + $size - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF($x, $y, $size, $size)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(55, 148, 255), [System.Drawing.Color]::FromArgb(40, 104, 180), 55)
    $g.FillPath($brush, $path)
    $font = New-Object System.Drawing.Font('Segoe UI', ($size * 0.5), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.DrawString('K', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF($x, ($y - $size * 0.02), $size, $size)), $fmt)
}

function New-Large([int]$w, [int]$h, [string]$file) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(9, 12, 22), [System.Drawing.Color]::FromArgb(20, 52, 110), 65)
    $g.FillRectangle($bg, $rect)
    # soft glow circles
    foreach ($c in @(@(0.85, 0.80, 0.9, 40), @(0.10, 0.95, 0.7, 28), @(0.95, 0.15, 0.5, 22))) {
        $r = $w * $c[2]
        $b = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($c[3], 55, 148, 255))
        $g.FillEllipse($b, ($w * $c[0] - $r / 2), ($h * $c[1] - $r / 2), $r, $r)
    }
    $size = $w * 0.56
    New-Badge $g (($w - $size) / 2) ($h * 0.16) $size
    # thin accent line under the badge
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(120, 55, 148, 255), [Math]::Max(1, $w / 82))
    $g.DrawLine($pen, $w * 0.28, $h * 0.50, $w * 0.72, $h * 0.50)
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $g.Dispose(); $bmp.Dispose()
}

function New-Small([int]$s, [string]$file) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::White)
    New-Badge $g ($s * 0.10) ($s * 0.10) ($s * 0.80)
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $g.Dispose(); $bmp.Dispose()
}

$dir = $PSScriptRoot
New-Large 164 314 (Join-Path $dir 'wizard-large-100.bmp')
New-Large 246 471 (Join-Path $dir 'wizard-large-150.bmp')
New-Large 328 628 (Join-Path $dir 'wizard-large-200.bmp')
New-Small 55 (Join-Path $dir 'wizard-small-100.bmp')
New-Small 83 (Join-Path $dir 'wizard-small-150.bmp')
New-Small 110 (Join-Path $dir 'wizard-small-200.bmp')
