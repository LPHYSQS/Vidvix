param(
    [string]$Source = "Assets/logo.png",
    [string]$OutputDirectory = "Assets"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

function Save-SquareAsset {
    param(
        [System.Drawing.Image]$SourceImage,
        [string]$DestinationPath,
        [int]$Size
    )

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.DrawImage($SourceImage, 0, 0, $Size, $Size)
        $bitmap.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Save-CenteredCanvasAsset {
    param(
        [System.Drawing.Image]$SourceImage,
        [string]$DestinationPath,
        [int]$Width,
        [int]$Height,
        [double]$IconScale = 0.72
    )

    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        $iconSize = [Math]::Min($Width, $Height) * $IconScale
        $iconSize = [Math]::Round($iconSize)
        $left = [Math]::Round(($Width - $iconSize) / 2.0)
        $top = [Math]::Round(($Height - $iconSize) / 2.0)

        $graphics.DrawImage($SourceImage, $left, $top, $iconSize, $iconSize)
        $bitmap.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $Source)) {
    throw "MSIX asset source image not found: $Source"
}

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$resolvedSource = (Resolve-Path -LiteralPath $Source).Path
$resolvedOutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

$sourceImage = [System.Drawing.Image]::FromFile($resolvedSource)
try {
    $squareAssets = @(
        @{ Name = 'LockScreenLogo.png'; Size = 24 },
        @{ Name = 'LockScreenLogo.scale-100.png'; Size = 24 },
        @{ Name = 'LockScreenLogo.scale-200.png'; Size = 48 },
        @{ Name = 'Square44x44Logo.png'; Size = 44 },
        @{ Name = 'Square44x44Logo.scale-100.png'; Size = 44 },
        @{ Name = 'Square44x44Logo.scale-200.png'; Size = 88 },
        @{ Name = 'Square44x44Logo.targetsize-16.png'; Size = 16 },
        @{ Name = 'Square44x44Logo.targetsize-24.png'; Size = 24 },
        @{ Name = 'Square44x44Logo.targetsize-24_altform-unplated.png'; Size = 24 },
        @{ Name = 'Square44x44Logo.targetsize-32.png'; Size = 32 },
        @{ Name = 'Square44x44Logo.targetsize-44.png'; Size = 44 },
        @{ Name = 'Square44x44Logo.targetsize-44_altform-unplated.png'; Size = 44 },
        @{ Name = 'Square44x44Logo.targetsize-48.png'; Size = 48 },
        @{ Name = 'Square44x44Logo.targetsize-64.png'; Size = 64 },
        @{ Name = 'Square44x44Logo.targetsize-256.png'; Size = 256 },
        @{ Name = 'Square150x150Logo.png'; Size = 150 },
        @{ Name = 'Square150x150Logo.scale-100.png'; Size = 150 },
        @{ Name = 'Square150x150Logo.scale-200.png'; Size = 300 },
        @{ Name = 'StoreLogo.png'; Size = 50 },
        @{ Name = 'StoreLogo.scale-100.png'; Size = 50 },
        @{ Name = 'StoreLogo.scale-140.png'; Size = 70 },
        @{ Name = 'StoreLogo.scale-180.png'; Size = 90 },
        @{ Name = 'StoreLogo.scale-200.png'; Size = 100 }
    )

    foreach ($asset in $squareAssets) {
        Save-SquareAsset -SourceImage $sourceImage -DestinationPath (Join-Path $resolvedOutputDirectory $asset.Name) -Size $asset.Size
    }

    $canvasAssets = @(
        @{ Name = 'Wide310x150Logo.png'; Width = 310; Height = 150; IconScale = 0.86 },
        @{ Name = 'Wide310x150Logo.scale-100.png'; Width = 310; Height = 150; IconScale = 0.86 },
        @{ Name = 'Wide310x150Logo.scale-200.png'; Width = 620; Height = 300; IconScale = 0.86 },
        @{ Name = 'SplashScreen.png'; Width = 620; Height = 300; IconScale = 0.76 },
        @{ Name = 'SplashScreen.scale-100.png'; Width = 620; Height = 300; IconScale = 0.76 },
        @{ Name = 'SplashScreen.scale-200.png'; Width = 1240; Height = 600; IconScale = 0.76 }
    )

    foreach ($asset in $canvasAssets) {
        Save-CenteredCanvasAsset `
            -SourceImage $sourceImage `
            -DestinationPath (Join-Path $resolvedOutputDirectory $asset.Name) `
            -Width $asset.Width `
            -Height $asset.Height `
            -IconScale $asset.IconScale
    }
}
finally {
    $sourceImage.Dispose()
}

Write-Host "MSIX assets generated from $resolvedSource into $resolvedOutputDirectory"
