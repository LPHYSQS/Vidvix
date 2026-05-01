[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$ProjectPath = "tests/SplitAudioOfflineSmoke/SplitAudioOfflineSmoke.csproj",
    [string]$Configuration = "Debug",
    [switch]$SkipBuild,
    [switch]$KeepTemp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "[test-split-audio-offline] $Message"
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

function Remove-PathIfExists {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
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

function Invoke-SmokeHarness {
    param(
        [Parameter(Mandatory = $true)][string]$HarnessDllPath,
        [Parameter(Mandatory = $true)][string]$InputPath,
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][string]$OutputExtension,
        [Parameter(Mandatory = $true)][string]$AccelerationMode,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    Write-Step ("Smoke run for {0} -> {1} ({2})" -f (Split-Path -Path $InputPath -Leaf), $OutputExtension, $AccelerationMode)

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

    Push-Location $WorkingDirectory
    try {
        $outputLines = & dotnet $HarnessDllPath $InputPath $OutputDirectory $OutputExtension $AccelerationMode 2>&1 | Tee-Object -Variable capturedOutput
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw ("Smoke harness failed with exit code {0}.{1}{1}{2}" -f $exitCode, [Environment]::NewLine, ($capturedOutput -join [Environment]::NewLine))
    }

    return @($capturedOutput | ForEach-Object { $_.ToString() })
}

function Assert-StemOutputs {
    param(
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][string]$InputBaseName,
        [Parameter(Mandatory = $true)][string]$OutputExtension
    )

    foreach ($stemName in @("vocals", "drums", "bass", "other")) {
        $expectedPath = Join-Path $OutputDirectory ("{0}_{1}{2}" -f $InputBaseName, $stemName, $OutputExtension)
        if (-not (Test-Path -LiteralPath $expectedPath)) {
            throw ("Missing expected stem output: {0}" -f $expectedPath)
        }
    }
}

function Assert-ExecutionPlanOutput {
    param(
        [Parameter(Mandatory = $true)][string[]]$CapturedOutput,
        [Parameter(Mandatory = $true)][string]$ExpectedAccelerationMode
    )

    $planLine = $CapturedOutput | Where-Object { $_ -like "EXECUTION_PLAN=*" } | Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($planLine)) {
        throw "Smoke harness did not emit EXECUTION_PLAN."
    }

    $deviceKindLine = $CapturedOutput | Where-Object { $_ -like "EXECUTION_DEVICE_KIND=*" } | Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($deviceKindLine)) {
        throw "Smoke harness did not emit EXECUTION_DEVICE_KIND."
    }

    $runtimeVariantLine = $CapturedOutput | Where-Object { $_ -like "EXECUTION_RUNTIME_VARIANT=*" } | Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($runtimeVariantLine)) {
        throw "Smoke harness did not emit EXECUTION_RUNTIME_VARIANT."
    }

    if ($ExpectedAccelerationMode -eq "Cpu" -and $deviceKindLine -ne "EXECUTION_DEVICE_KIND=Cpu") {
        throw ("Expected CPU execution, but saw: {0}" -f $deviceKindLine)
    }

    if ($ExpectedAccelerationMode -eq "Cpu" -and $runtimeVariantLine -ne "EXECUTION_RUNTIME_VARIANT=Cpu") {
        throw ("Expected CPU runtime variant, but saw: {0}" -f $runtimeVariantLine)
    }

    if ($ExpectedAccelerationMode -eq "GpuPreferred" -and
        $deviceKindLine -notmatch "^EXECUTION_DEVICE_KIND=(DiscreteGpu|IntegratedGpu|UnknownGpu|Cpu)$") {
        throw ("Unexpected GPU-preferred execution result: {0}" -f $deviceKindLine)
    }

    if ($ExpectedAccelerationMode -eq "GpuPreferred" -and
        $runtimeVariantLine -notmatch "^EXECUTION_RUNTIME_VARIANT=(Cuda|DirectMl|Cpu)$") {
        throw ("Unexpected GPU-preferred runtime variant: {0}" -f $runtimeVariantLine)
    }
}

function Test-AnyPathExists {
    param(
        [Parameter(Mandatory = $true)][string[]]$CandidatePaths,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    foreach ($candidatePath in $CandidatePaths) {
        if (Test-Path -LiteralPath (Join-Path $candidatePath $RelativePath)) {
            return $true
        }
    }

    return $false
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
$resolvedProjectPath = Resolve-FullPath -Path $ProjectPath -BasePath $resolvedRepoRoot
$projectDirectory = Split-Path -Path $resolvedProjectPath -Parent
$framework = "net8.0-windows10.0.19041.0"
$harnessOutputDirectory = Join-Path $resolvedRepoRoot ("artifacts\split-audio-offline-smoke\{0}\{1}" -f $Configuration, $framework)
$buildOutputDirectoryArgument = if ($harnessOutputDirectory.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
    $harnessOutputDirectory
}
else {
    "{0}{1}" -f $harnessOutputDirectory, [System.IO.Path]::DirectorySeparatorChar
}
$harnessDllPath = Join-Path $harnessOutputDirectory "SplitAudioOfflineSmoke.dll"
$vidvixAssemblyPath = Join-Path $harnessOutputDirectory "Vidvix.dll"
$repoFfmpegPath = Resolve-FullPath -Path "Tools/ffmpeg/ffmpeg.exe" -BasePath $resolvedRepoRoot

if (-not (Test-Path -LiteralPath $repoFfmpegPath)) {
    throw ("FFmpeg executable not found: {0}" -f $repoFfmpegPath)
}

if (-not $SkipBuild) {
    Remove-PathIfExists -Path $harnessOutputDirectory
    Invoke-ExternalCommand -FilePath "dotnet" -ArgumentList @(
        "build",
        $resolvedProjectPath,
        "-c",
        $Configuration,
        "-p:UseAppHost=false",
        "-p:OutDir=$buildOutputDirectoryArgument"
    ) -WorkingDirectory $resolvedRepoRoot
}

if (-not (Test-Path -LiteralPath $harnessDllPath)) {
    throw ("Smoke harness DLL not found: {0}" -f $harnessDllPath)
}

if (-not (Test-Path -LiteralPath $vidvixAssemblyPath)) {
    throw ("Vidvix assembly not found: {0}" -f $vidvixAssemblyPath)
}

$vidvixVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($vidvixAssemblyPath).FileVersion
if ([string]::IsNullOrWhiteSpace($vidvixVersion)) {
    throw ("Unable to resolve Vidvix assembly version from: {0}" -f $vidvixAssemblyPath)
}

$demucsStorageRootCandidates = @(
    (Join-Path (Join-Path $harnessOutputDirectory "Tools\Demucs") ("Version-{0}" -f $vidvixVersion)),
    (Join-Path (
        Join-Path (
            Join-Path ([Environment]::GetFolderPath([System.Environment+SpecialFolder]::LocalApplicationData)) "Vidvix"
        ) "Tools\Demucs"
    ) ("Version-{0}" -f $vidvixVersion))
) | Select-Object -Unique
$cpuRuntimeExtractionPathCandidates = $demucsStorageRootCandidates | ForEach-Object { Join-Path $_ "Current" }
$directMlRuntimeExtractionPathCandidates = $demucsStorageRootCandidates | ForEach-Object { Join-Path $_ "CurrentGpu" }
$cudaRuntimeExtractionPathCandidates = $demucsStorageRootCandidates | ForEach-Object { Join-Path $_ "CurrentGpuCuda" }

$tempRootPath = Join-Path ([System.IO.Path]::GetTempPath()) ("vidvix-split-audio-offline-smoke-{0}" -f [Guid]::NewGuid().ToString("N"))
$audioInputPath = Join-Path $tempRootPath "sample-audio.wav"
$audioOutputPath = Join-Path $tempRootPath "sample-audio-output"
$videoInputPath = Join-Path $tempRootPath "sample-video.mp4"
$videoOutputPath = Join-Path $tempRootPath "sample-video-output"

Write-Step ("Repo root: {0}" -f $resolvedRepoRoot)
Write-Step ("Harness project: {0}" -f $resolvedProjectPath)
Write-Step ("Demucs storage roots: {0}" -f ($demucsStorageRootCandidates -join ", "))
Write-Step ("Temporary workspace: {0}" -f $tempRootPath)

try {
    New-Item -ItemType Directory -Path $tempRootPath -Force | Out-Null

    Write-Step "Clearing extracted runtime to force first-run unzip validation"
    foreach ($path in @($cpuRuntimeExtractionPathCandidates + $directMlRuntimeExtractionPathCandidates + $cudaRuntimeExtractionPathCandidates)) {
        Remove-PathIfExists -Path $path
    }

    Write-Step "Generating sample audio input"
    Invoke-ExternalCommand -FilePath $repoFfmpegPath -ArgumentList @(
        "-hide_banner",
        "-y",
        "-f",
        "lavfi",
        "-i",
        "sine=frequency=440:duration=4:sample_rate=44100",
        "-c:a",
        "pcm_s16le",
        $audioInputPath
    ) -WorkingDirectory $resolvedRepoRoot

    $audioRunOutput = Invoke-SmokeHarness -HarnessDllPath $harnessDllPath -InputPath $audioInputPath -OutputDirectory $audioOutputPath -OutputExtension ".mp3" -AccelerationMode "Cpu" -WorkingDirectory $resolvedRepoRoot
    Assert-StemOutputs -OutputDirectory $audioOutputPath -InputBaseName "sample-audio" -OutputExtension ".mp3"
    Assert-ExecutionPlanOutput -CapturedOutput $audioRunOutput -ExpectedAccelerationMode "Cpu"

    if (-not (Test-AnyPathExists -CandidatePaths $cpuRuntimeExtractionPathCandidates -RelativePath "python.exe")) {
        throw ("Expected extracted CPU runtime was not found after the first smoke run: {0}" -f ($cpuRuntimeExtractionPathCandidates -join " / "))
    }

    Write-Step "Generating sample video input"
    Invoke-ExternalCommand -FilePath $repoFfmpegPath -ArgumentList @(
        "-hide_banner",
        "-y",
        "-f",
        "lavfi",
        "-i",
        "color=c=black:s=1280x720:d=4",
        "-f",
        "lavfi",
        "-i",
        "sine=frequency=523:duration=4:sample_rate=44100",
        "-shortest",
        "-c:v",
        "libx264",
        "-pix_fmt",
        "yuv420p",
        "-c:a",
        "aac",
        $videoInputPath
    ) -WorkingDirectory $resolvedRepoRoot

    $videoRunOutput = Invoke-SmokeHarness -HarnessDllPath $harnessDllPath -InputPath $videoInputPath -OutputDirectory $videoOutputPath -OutputExtension ".flac" -AccelerationMode "GpuPreferred" -WorkingDirectory $resolvedRepoRoot
    Assert-StemOutputs -OutputDirectory $videoOutputPath -InputBaseName "sample-video" -OutputExtension ".flac"
    Assert-ExecutionPlanOutput -CapturedOutput $videoRunOutput -ExpectedAccelerationMode "GpuPreferred"

    if (-not (Test-AnyPathExists -CandidatePaths $directMlRuntimeExtractionPathCandidates -RelativePath "python.exe")) {
        if (-not (Test-AnyPathExists -CandidatePaths $cudaRuntimeExtractionPathCandidates -RelativePath "python.exe")) {
            throw ("Expected extracted GPU runtime was not found after the GPU-preferred smoke run: {0} / {1}" -f ($directMlRuntimeExtractionPathCandidates -join " / "), ($cudaRuntimeExtractionPathCandidates -join " / "))
        }
    }

    Write-Step "Offline smoke regression passed."
    Write-Host ("AUDIO_OUTPUT_DIR={0}" -f $audioOutputPath)
    Write-Host ("VIDEO_OUTPUT_DIR={0}" -f $videoOutputPath)
    Write-Host ("FIRST_RUN_LOG_LINES={0}" -f $audioRunOutput.Count)
    Write-Host ("SECOND_RUN_LOG_LINES={0}" -f $videoRunOutput.Count)
}
finally {
    if (-not $KeepTemp) {
        Remove-PathIfExists -Path $tempRootPath
    }
    else {
        Write-Step ("Temporary workspace preserved at: {0}" -f $tempRootPath)
    }
}
