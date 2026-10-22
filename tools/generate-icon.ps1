param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\CatSoftwareManager\Assets\cat.ico')
)

Add-Type -AssemblyName System.Drawing

function New-CatPngBytes {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = $Size / 64.0
    $orange = [System.Drawing.Color]::FromArgb(255, 239, 146, 93)
    $dark = [System.Drawing.Color]::FromArgb(255, 59, 48, 41)
    $cream = [System.Drawing.Color]::FromArgb(255, 255, 241, 218)
    $pink = [System.Drawing.Color]::FromArgb(255, 222, 117, 117)

    $face = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $face.AddEllipse(8 * $scale, 15 * $scale, 48 * $scale, 43 * $scale)
    $leftEar = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(11 * $scale, 22 * $scale),
        [System.Drawing.PointF]::new(15 * $scale, 4 * $scale),
        [System.Drawing.PointF]::new(29 * $scale, 15 * $scale)
    )
    $rightEar = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(35 * $scale, 15 * $scale),
        [System.Drawing.PointF]::new(49 * $scale, 4 * $scale),
        [System.Drawing.PointF]::new(53 * $scale, 22 * $scale)
    )
    $face.AddPolygon($leftEar)
    $face.AddPolygon($rightEar)
    $face.CloseFigure()

    $faceBrush = [System.Drawing.SolidBrush]::new($orange)
    $outlinePen = [System.Drawing.Pen]::new($dark, [Math]::Max(1.4, 2.4 * $scale))
    $graphics.FillPath($faceBrush, $face)
    $graphics.DrawPath($outlinePen, $face)

    $innerLeft = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(14 * $scale, 19 * $scale),
        [System.Drawing.PointF]::new(16.5 * $scale, 9 * $scale),
        [System.Drawing.PointF]::new(23 * $scale, 16 * $scale)
    )
    $innerRight = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(41 * $scale, 16 * $scale),
        [System.Drawing.PointF]::new(47.5 * $scale, 9 * $scale),
        [System.Drawing.PointF]::new(50 * $scale, 19 * $scale)
    )
    $innerBrush = [System.Drawing.SolidBrush]::new($pink)
    $graphics.FillPolygon($innerBrush, $innerLeft)
    $graphics.FillPolygon($innerBrush, $innerRight)

    $creamBrush = [System.Drawing.SolidBrush]::new($cream)
    $graphics.FillEllipse($creamBrush, 23 * $scale, 31 * $scale, 18 * $scale, 15 * $scale)
    $darkBrush = [System.Drawing.SolidBrush]::new($dark)
    $eyeRadius = [Math]::Max(1.3, 2.25 * $scale)
    $graphics.FillEllipse($darkBrush, 21 * $scale, 27 * $scale, $eyeRadius * 2, $eyeRadius * 2.5)
    $graphics.FillEllipse($darkBrush, (43 * $scale) - ($eyeRadius * 2), 27 * $scale, $eyeRadius * 2, $eyeRadius * 2.5)
    $graphics.FillEllipse($darkBrush, 30.8 * $scale, 35 * $scale, 2.4 * $scale, 1.8 * $scale)

    if ($Size -ge 32) {
        $whiskerPen = [System.Drawing.Pen]::new($dark, [Math]::Max(0.8, 1.1 * $scale))
        $graphics.DrawLine($whiskerPen, 8 * $scale, 37 * $scale, 21 * $scale, 38 * $scale)
        $graphics.DrawLine($whiskerPen, 8 * $scale, 42 * $scale, 21 * $scale, 41 * $scale)
        $graphics.DrawLine($whiskerPen, 43 * $scale, 38 * $scale, 56 * $scale, 37 * $scale)
        $graphics.DrawLine($whiskerPen, 43 * $scale, 41 * $scale, 56 * $scale, 42 * $scale)
        $whiskerPen.Dispose()
    }

    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    $faceBrush.Dispose()
    $innerBrush.Dispose()
    $creamBrush.Dispose()
    $darkBrush.Dispose()
    $outlinePen.Dispose()
    $face.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
    return ,$bytes
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $images += [pscustomobject]@{
        Size = $size
        Bytes = (New-CatPngBytes -Size $size)
    }
}

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $directory -Force | Out-Null

$stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($image in $images) {
    $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$image.Bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $image.Bytes.Length
}

foreach ($image in $images) {
    $writer.Write($image.Bytes)
}

$writer.Dispose()
$stream.Dispose()
Write-Output "Generated $OutputPath"
