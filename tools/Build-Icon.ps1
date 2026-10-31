# Geometry from Microsoft's Fluent System Icons "Panel Right" (MIT).
Add-Type -AssemblyName System.Drawing
$assetRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Assets'))
$source = Get-Content -LiteralPath (Join-Path $assetRoot 'FluentPanelRight.svg') -Raw
$svg = $source.Replace('width="24" height="24"','width="256" height="256"').Replace('fill="#212121"','fill="url(#accent)"')
$svg = $svg.Replace('<path ', '<defs><linearGradient id="accent" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#4DBBFF"/><stop offset="1" stop-color="#0067C0"/></linearGradient></defs><path ')
Set-Content -LiteralPath (Join-Path $assetRoot 'Sidebar.svg') -Value $svg -Encoding utf8

function New-PanelPath {
 $p = [System.Drawing.Drawing2D.GraphicsPath]::new([System.Drawing.Drawing2D.FillMode]::Alternate)
 $p.StartFigure()
 $p.AddBezier(18.75,4,20.5449,4,22,5.45507,22,7.25)
 $p.AddLine(22,7.25,22,16.75)
 $p.AddBezier(22,16.75,22,18.5449,20.5449,20,18.75,20)
 $p.AddLine(18.75,20,5.25,20)
 $p.AddBezier(5.25,20,3.45507,20,2,18.5449,2,16.75)
 $p.AddLine(2,16.75,2,7.25)
 $p.AddBezier(2,7.25,2,5.45507,3.45507,4,5.25,4)
 $p.AddLine(5.25,4,18.75,4); $p.CloseFigure()
 $p.StartFigure()
 $p.AddBezier(5.25,5.5,4.2835,5.5,3.5,6.2835,3.5,7.25)
 $p.AddLine(3.5,7.25,3.5,16.75)
 $p.AddBezier(3.5,16.75,3.5,17.7165,4.2835,18.5,5.25,18.5)
 $p.AddLine(5.25,18.5,14.5,18.5); $p.AddLine(14.5,18.5,14.5,5.5)
 $p.AddLine(14.5,5.5,5.25,5.5); $p.CloseFigure()
 return ,$p
}

$frames = [System.Collections.Generic.List[byte[]]]::new()
$sizes = @(16,20,24,32,40,48,64,128,256)
foreach ($size in $sizes) {
 $bitmap = [System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
 $g = [System.Drawing.Graphics]::FromImage($bitmap)
 $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
 $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
 $g.Clear([System.Drawing.Color]::Transparent)
 $g.ScaleTransform($size/24.0,$size/24.0)
 $path = New-PanelPath
 $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.RectangleF]::new(2,4,20,16),[System.Drawing.Color]::FromArgb(77,187,255),[System.Drawing.Color]::FromArgb(0,103,192),45)
 $g.FillPath($brush,$path)
 $png = [System.IO.MemoryStream]::new()
 $bitmap.Save($png,[System.Drawing.Imaging.ImageFormat]::Png)
 $frames.Add($png.ToArray())
 if ($size -eq 256) { $bitmap.Save((Join-Path $assetRoot 'Sidebar.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
 $png.Dispose(); $brush.Dispose(); $path.Dispose(); $g.Dispose(); $bitmap.Dispose()
}
$output = [System.IO.File]::Create((Join-Path $assetRoot 'Sidebar.ico'))
$writer = [System.IO.BinaryWriter]::new($output)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16*$sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) {
 $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
 $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
 $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
 $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Dispose(); $output.Dispose()
