[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$OutputDirectory = "artifacts/ai-smoke-samples",
    [int]$Width = 48,
    [int]$Height = 32,
    [int]$FrameRate = 6,
    [int]$DurationSeconds = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "[new-ai-smoke-samples] $Message"
}

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$BasePath
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Invoke-ExternalCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    Write-Step ("Running: {0} {1}" -f $FilePath, ($ArgumentList -join " "))

    Push-Location $WorkingDirectory
    try {
        & $FilePath @ArgumentList
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw ("Command failed with exit code {0}: {1} {2}" -f $exitCode, $FilePath, ($ArgumentList -join " "))
    }
}

$scriptRoot = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    $PSScriptRoot
}
else {
    Split-Path -Path $PSCommandPath -Parent
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Join-Path $scriptRoot ".."
}

$resolvedRepoRoot = Resolve-FullPath -Path $RepoRoot -BasePath (Get-Location).Path
$resolvedOutputDirectory = Resolve-FullPath -Path $OutputDirectory -BasePath $resolvedRepoRoot
$ffmpegPath = Resolve-FullPath -Path "Tools/ffmpeg/ffmpeg.exe" -BasePath $resolvedRepoRoot

if (-not (Test-Path -LiteralPath $ffmpegPath)) {
    throw ("FFmpeg executable not found: {0}" -f $ffmpegPath)
}

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

$interpolationSamplePath = Join-Path $resolvedOutputDirectory "interpolation-smoke-source.mp4"
$enhancementSamplePath = Join-Path $resolvedOutputDirectory "enhancement-smoke-source.mp4"
$videoSizeArgument = "{0}x{1}" -f $Width, $Height

Write-Step ("Repo root: {0}" -f $resolvedRepoRoot)
Write-Step ("Output directory: {0}" -f $resolvedOutputDirectory)

Invoke-ExternalCommand -FilePath $ffmpegPath -ArgumentList @(
    "-loglevel",
    "error",
    "-hide_banner",
    "-y",
    "-f",
    "lavfi",
    "-i",
    ("testsrc2=size={0}:rate={1}:duration={2}" -f $videoSizeArgument, $FrameRate, $DurationSeconds),
    "-f",
    "lavfi",
    "-i",
    ("sine=frequency=440:sample_rate=48000:duration={0}" -f $DurationSeconds),
    "-shortest",
    "-c:v",
    "libx264",
    "-pix_fmt",
    "yuv420p",
    "-c:a",
    "aac",
    "-movflags",
    "+faststart",
    $interpolationSamplePath
) -WorkingDirectory $resolvedRepoRoot

Invoke-ExternalCommand -FilePath $ffmpegPath -ArgumentList @(
    "-loglevel",
    "error",
    "-hide_banner",
    "-y",
    "-f",
    "lavfi",
    "-i",
    ("testsrc=size={0}:rate={1}:duration={2}" -f $videoSizeArgument, $FrameRate, $DurationSeconds),
    "-f",
    "lavfi",
    "-i",
    ("sine=frequency=523.25:sample_rate=48000:duration={0}" -f $DurationSeconds),
    "-shortest",
    "-c:v",
    "libx264",
    "-pix_fmt",
    "yuv420p",
    "-c:a",
    "aac",
    "-movflags",
    "+faststart",
    $enhancementSamplePath
) -WorkingDirectory $resolvedRepoRoot

Write-Output ("INTERPOLATION_SAMPLE={0}" -f $interpolationSamplePath)
Write-Output ("ENHANCEMENT_SAMPLE={0}" -f $enhancementSamplePath)
Write-Output ("SAMPLE_WIDTH={0}" -f $Width)
Write-Output ("SAMPLE_HEIGHT={0}" -f $Height)
Write-Output ("SAMPLE_FRAME_RATE={0}" -f $FrameRate)
Write-Output ("SAMPLE_DURATION_SECONDS={0}" -f $DurationSeconds)
