# Render the same vector geometry as Assets/WireLink.svg into a multi-resolution ICO.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../WireLink.App/Assets/WireLink.ico'))
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new(1024, 1024)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.ScaleTransform(4, 4)
    $outline = [Drawing.Drawing2D.GraphicsPath]::new()
    $outline.AddArc(8, 8, 106, 106, 180, 90)
    $outline.AddArc(142, 8, 106, 106, 270, 90)
    $outline.AddArc(142, 142, 106, 106, 0, 90)
    $outline.AddArc(8, 142, 106, 106, 90, 90)
    $outline.CloseFigure()
    $backgroundBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#17243b'))
    $foregroundBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#6ee7c4'))
    $graphics.FillPath($backgroundBrush, $outline)
    $coordinates = @(48,68,80,68,90,151,122,82,151,82,159,151,186,68,218,68,171,191,139,191,132,127,103,191,72,191)
    $points = for ($i = 0; $i -lt $coordinates.Count; $i += 2) {
        [Drawing.PointF]::new($coordinates[$i], $coordinates[$i + 1])
    }
    $graphics.FillPolygon($foregroundBrush, [Drawing.PointF[]]$points)
    $small = [Drawing.Bitmap]::new($size, $size)
    $scaled = [Drawing.Graphics]::FromImage($small)
    $scaled.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $scaled.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $scaled.DrawImage($bitmap, 0, 0, $size, $size)
    $buffer = [IO.MemoryStream]::new()
    $small.Save($buffer, [Drawing.Imaging.ImageFormat]::Png)
    $images += ,$buffer.ToArray()
    $buffer.Dispose(); $scaled.Dispose(); $small.Dispose()
    $graphics.Dispose(); $bitmap.Dispose(); $outline.Dispose()
    $backgroundBrush.Dispose(); $foregroundBrush.Dispose()
}
$output = [IO.File]::Create($iconPath)
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
}
finally { $writer.Dispose(); $output.Dispose() }
Write-Output "Generated $iconPath ($($sizes -join ', ') px)"
