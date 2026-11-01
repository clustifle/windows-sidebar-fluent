param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$iconPath = Join-Path $PSScriptRoot '..\Assets\Sidebar.ico'
$icon = [Drawing.Icon]::new((Resolve-Path -LiteralPath $iconPath).Path, 96, 96)
$logo = $icon.ToBitmap()
try {
    foreach ($kind in @('Dialog','Banner')) {
        $height = if ($kind -eq 'Dialog') { 312 } else { 58 }
        $bitmap = [Drawing.Bitmap]::new(493, $height, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
        $canvas = [Drawing.Graphics]::FromImage($bitmap)
        $tint = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(226, 241, 244))
        $ink = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(30, 70, 80))
        $font = [Drawing.Font]::new('Segoe UI', 13, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
        try {
            $canvas.Clear([Drawing.Color]::White)
            $canvas.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $canvas.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
            if ($kind -eq 'Dialog') {
                $canvas.FillRectangle($tint, 0, 0, 164, $height)
                $canvas.DrawImage($logo, 34, 74, 96, 96)
                $canvas.DrawString('FluentSidebar', $font, $ink, 29, 191)
            } else { $canvas.DrawImage($logo, 448, 12, 34, 34) }
            $bitmap.Save((Join-Path $OutputDirectory ($kind+'.bmp')), [Drawing.Imaging.ImageFormat]::Bmp)
        } finally { $font.Dispose(); $ink.Dispose(); $tint.Dispose(); $canvas.Dispose(); $bitmap.Dispose() }
    }
} finally { $logo.Dispose(); $icon.Dispose() }
