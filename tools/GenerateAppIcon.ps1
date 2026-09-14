[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\Captail\Assets\Captail.ico'),

    [ValidateSet('Active', 'Inactive')]
    [string]$Mode = 'Active',

    [string]$Color = ''
)

$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Drawing.Common
}
catch {
    Add-Type -AssemblyName System.Drawing
}

if ([string]::IsNullOrWhiteSpace($Color)) {
    $Color = if ($Mode -eq 'Inactive') { '#8492A7' } else { '#FF705A' }
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$pngImages = [System.Collections.Generic.List[byte[]]]::new()
$iconColor = [System.Drawing.ColorTranslator]::FromHtml($Color)

function New-RoundedRectanglePath {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.RectangleF]$Bounds,

        [Parameter(Mandatory)]
        [single]$Radius
    )

    $diameter = [single]($Radius * 2)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Bounds.Left, $Bounds.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.Left, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Set-HighQualityGraphics {
    param([Parameter(Mandatory)][System.Drawing.Graphics]$Graphics)

    $Graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    $Graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $Graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $Graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $Graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
}

function Set-ExactIconSymmetry {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.Bitmap]$Bitmap,

        [Parameter(Mandatory)]
        [System.Drawing.Color]$BaseColor
    )

    # Anti-aliasing and fractional scaling can otherwise leave opposite edges
    # a fraction of a pixel different. Mirror alpha explicitly so every size
    # is horizontally and vertically symmetric, then discard near-transparent
    # fringe pixels that make 16/20 px icons look fuzzy in Explorer/tray.
    $halfWidth = [int][Math]::Ceiling($Bitmap.Width / 2.0)
    $halfHeight = [int][Math]::Ceiling($Bitmap.Height / 2.0)
    for ($y = 0; $y -lt $halfHeight; $y++) {
        $mirrorY = $Bitmap.Height - 1 - $y
        for ($x = 0; $x -lt $halfWidth; $x++) {
            $mirrorX = $Bitmap.Width - 1 - $x
            $alphas = @(
                $Bitmap.GetPixel($x, $y).A,
                $Bitmap.GetPixel($mirrorX, $y).A,
                $Bitmap.GetPixel($x, $mirrorY).A,
                $Bitmap.GetPixel($mirrorX, $mirrorY).A
            )
            $alpha = [int][Math]::Round(($alphas | Measure-Object -Average).Average)
            if ($alpha -lt 24) {
                $alpha = 0
            }
            $pixel = [System.Drawing.Color]::FromArgb(
                $alpha,
                $BaseColor.R,
                $BaseColor.G,
                $BaseColor.B)

            $Bitmap.SetPixel($x, $y, $pixel)
            $Bitmap.SetPixel($mirrorX, $y, $pixel)
            $Bitmap.SetPixel($x, $mirrorY, $pixel)
            $Bitmap.SetPixel($mirrorX, $mirrorY, $pixel)
        }
    }
}

function New-CaptailIconPng {
    param(
        [Parameter(Mandatory)]
        [int]$Size,

        [Parameter(Mandatory)]
        [System.Drawing.Color]$MarkColor
    )

    # Render large first, then downsample. This keeps the same canonical
    # rounded-square + centered-dot geometry from 16 through 256 px.
    $supersample = 8
    $renderSize = $Size * $supersample
    $source = [System.Drawing.Bitmap]::new(
        $renderSize,
        $renderSize,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($source)
    try {
        Set-HighQualityGraphics $graphics
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $stroke = [single]($renderSize * 0.065)
        $inset = [single]($renderSize * 0.085 + $stroke / 2)
        $bounds = [System.Drawing.RectangleF]::new(
            $inset,
            $inset,
            [single]($renderSize - 2 * $inset),
            [single]($renderSize - 2 * $inset))
        $radius = [single]($renderSize * 0.19)
        $path = New-RoundedRectanglePath -Bounds $bounds -Radius $radius
        $pen = [System.Drawing.Pen]::new($MarkColor, $stroke)
        try {
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $graphics.DrawPath($pen, $path)
        }
        finally {
            $pen.Dispose()
            $path.Dispose()
        }

        $dotSize = [single]($renderSize * 0.15)
        $dotOffset = [single](($renderSize - $dotSize) / 2)
        $dotBrush = [System.Drawing.SolidBrush]::new($MarkColor)
        try {
            $graphics.FillEllipse($dotBrush, $dotOffset, $dotOffset, $dotSize, $dotSize)
        }
        finally {
            $dotBrush.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $downsample = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        Set-HighQualityGraphics $downsample
        $downsample.Clear([System.Drawing.Color]::Transparent)
        $downsample.DrawImage(
            $source,
            [System.Drawing.Rectangle]::new(0, 0, $Size, $Size),
            0,
            0,
            $renderSize,
            $renderSize,
            [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $downsample.Dispose()
        $source.Dispose()
    }

    Set-ExactIconSymmetry -Bitmap $bitmap -BaseColor $MarkColor

    $stream = [System.IO.MemoryStream]::new()
    try {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

foreach ($size in $sizes) {
    $pngImages.Add((New-CaptailIconPng -Size $size -MarkColor $iconColor))
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$file = [System.IO.File]::Create($resolvedOutput)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $image = $pngImages[$index]
        $iconDimension = if ($size -eq 256) { 0 } else { $size }
        $writer.Write([byte]$iconDimension)
        $writer.Write([byte]$iconDimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Length
    }

    foreach ($image in $pngImages) {
        $writer.Write($image)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Output $resolvedOutput
