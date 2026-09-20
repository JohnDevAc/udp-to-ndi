Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size,$size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.ScaleTransform($size/256.0,$size/256.0)
    $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(25,38,53))
    $mint = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(100,230,183))
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235,245,250))
    $g.FillRectangle($background,0,0,256,256)
    $g.FillRectangle($white,42,88,28,80)
    $g.FillRectangle($mint,186,88,28,80)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(100,230,183),16)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawLine($pen,88,128,161,128)
    $g.DrawLines($pen,[System.Drawing.PointF[]]@([System.Drawing.PointF]::new(137,101),[System.Drawing.PointF]::new(164,128),[System.Drawing.PointF]::new(137,155)))
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$stream.ToArray()
    if ($size -eq 256) { $bitmap.Save((Join-Path $PSScriptRoot 'app.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $pen.Dispose(); $background.Dispose(); $mint.Dispose(); $white.Dispose(); $g.Dispose(); $bitmap.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $PSScriptRoot 'app.ico'))
$writer = New-Object System.IO.BinaryWriter($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]7)
$offset = 6 + 16 * 7
$sizes = @(16,24,32,48,64,128,0)
for ($i=0; $i -lt 7; $i++) {
    $writer.Write([byte]$sizes[$i]); $writer.Write([byte]$sizes[$i]); $writer.Write([uint16]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
$writer.Dispose()
