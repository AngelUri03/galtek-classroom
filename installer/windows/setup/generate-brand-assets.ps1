#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $SourceLogo,
    [Parameter(Mandatory = $true)][string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedSourceSha256 = 'fd004063ddfa305c3703190cef786d1b5b9e61dbdcd7b29419db1cb10775869e'
$iconSizes = @(16, 24, 32, 48, 64, 128, 256)

function New-TransparentBitmap {
    param([int] $Width, [int] $Height)
    return [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
}

function Copy-ScaledImage {
    param(
        [System.Drawing.Image] $Source,
        [System.Drawing.Rectangle] $SourceRectangle,
        [int] $CanvasWidth,
        [int] $CanvasHeight,
        [int] $Padding
    )

    $bitmap = New-TransparentBitmap -Width $CanvasWidth -Height $CanvasHeight
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        $availableWidth = $CanvasWidth - (2 * $Padding)
        $availableHeight = $CanvasHeight - (2 * $Padding)
        $scale = [Math]::Min($availableWidth / $SourceRectangle.Width, $availableHeight / $SourceRectangle.Height)
        $width = [Math]::Max(1, [int][Math]::Round($SourceRectangle.Width * $scale))
        $height = [Math]::Max(1, [int][Math]::Round($SourceRectangle.Height * $scale))
        $x = [int][Math]::Floor(($CanvasWidth - $width) / 2)
        $y = [int][Math]::Floor(($CanvasHeight - $height) / 2)
        $destination = [System.Drawing.Rectangle]::new($x, $y, $width, $height)
        $graphics.DrawImage($Source, $destination, $SourceRectangle, [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $graphics.Dispose()
    }
    return $bitmap
}

function Save-Png {
    param([System.Drawing.Bitmap] $Bitmap, [string] $Path)
    try {
        $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $Bitmap.Dispose()
    }
}

function Get-AlphaBounds {
    param([System.Drawing.Bitmap] $Bitmap, [int] $FirstRow, [int] $LastRow)

    $minX = $Bitmap.Width
    $minY = $Bitmap.Height
    $maxX = -1
    $maxY = -1
    for ($y = $FirstRow; $y -le $LastRow; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -gt 0) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { throw 'The source logo has no visible pixels.' }
    return [System.Drawing.Rectangle]::FromLTRB($minX, $minY, $maxX + 1, $maxY + 1)
}

function Get-SymbolLastRow {
    param([System.Drawing.Bitmap] $Bitmap)

    $searchStart = [int][Math]::Floor($Bitmap.Height * 0.45)
    $searchEnd = [int][Math]::Floor($Bitmap.Height * 0.75)
    $runStart = -1
    for ($y = $searchStart; $y -le $searchEnd; $y++) {
        $hasAlpha = $false
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -gt 0) { $hasAlpha = $true; break }
        }
        if (-not $hasAlpha -and $runStart -lt 0) { $runStart = $y }
        if ($hasAlpha -and $runStart -ge 0) {
            if (($y - $runStart) -ge 8) { return $runStart - 1 }
            $runStart = -1
        }
    }
    throw 'Unable to find the transparent separator between the graphic symbol and wordmark.'
}

function Write-MultiResolutionIcon {
    param([System.Drawing.Bitmap] $Source, [System.Drawing.Rectangle] $SourceRectangle, [string] $Path)

    $frames = @()
    foreach ($size in $iconSizes) {
        $frame = Copy-ScaledImage -Source $Source -SourceRectangle $SourceRectangle -CanvasWidth $size -CanvasHeight $size -Padding ([Math]::Max(1, [int][Math]::Round($size * 0.06)))
        $stream = [System.IO.MemoryStream]::new()
        try {
            $frame.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames += ,$stream.ToArray()
        }
        finally {
            $stream.Dispose()
            $frame.Dispose()
        }
    }

    $file = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)
        $offset = 6 + (16 * $frames.Count)
        for ($index = 0; $index -lt $frames.Count; $index++) {
            $size = $iconSizes[$index]
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($frameBytes in $frames) { $writer.Write($frameBytes) }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}

$sourcePath = [System.IO.Path]::GetFullPath($SourceLogo)
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Official logo is missing: $sourcePath" }
$sourceHashBefore = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sourceHashBefore -ne $expectedSourceSha256) {
    throw "Official logo hash mismatch. Expected $expectedSourceSha256, actual $sourceHashBefore."
}

Add-Type -AssemblyName System.Drawing
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$source = [System.Drawing.Bitmap]::new($sourcePath)
try {
    if ($source.Width -ne 865 -or $source.Height -ne 1080) { throw 'Official logo dimensions must remain 865x1080.' }
    if (($source.PixelFormat -band [System.Drawing.Imaging.PixelFormat]::Alpha) -eq 0) { throw 'Official logo must retain its alpha channel.' }

    $symbolLastRow = Get-SymbolLastRow -Bitmap $source
    $symbolVisible = Get-AlphaBounds -Bitmap $source -FirstRow 0 -LastRow $symbolLastRow
    $symbolCrop = [System.Drawing.Rectangle]::FromLTRB(
        [Math]::Max(0, $symbolVisible.Left - 12),
        [Math]::Max(0, $symbolVisible.Top - 12),
        [Math]::Min($source.Width, $symbolVisible.Right + 12),
        [Math]::Min($source.Height, $symbolVisible.Bottom + 12))
    Save-Png -Bitmap (Copy-ScaledImage -Source $source -SourceRectangle $symbolCrop -CanvasWidth 256 -CanvasHeight 256 -Padding 12) -Path (Join-Path $outputRoot 'galtek-classroom-icon.png')
    Write-MultiResolutionIcon -Source $source -SourceRectangle $symbolCrop -Path (Join-Path $outputRoot 'galtek-classroom.ico')
}
finally {
    $source.Dispose()
}

$sourceHashAfter = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sourceHashAfter -ne $sourceHashBefore) { throw 'Official logo was modified while deriving installer assets.' }
Write-Output "GALTEK_BRAND_ASSETS_READY sourceSha256=$sourceHashAfter output=$outputRoot"
