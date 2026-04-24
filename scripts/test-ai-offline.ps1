[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$ProjectPath = "tests/AiOfflineSmoke/AiOfflineSmoke.csproj",
    [string]$Configuration = "Debug",
    [switch]$SkipBuild,
    [switch]$KeepTemp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "[test-ai-offline] $Message"
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

function Invoke-CapturedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$FailureLabel
    )

    Write-Step ("Running: {0} {1}" -f $FilePath, ($ArgumentList -join " "))

    Push-Location $WorkingDirectory
    try {
        $capturedOutput = & $FilePath @ArgumentList 2>&1 | Tee-Object -Variable rawOutput
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw ("{0} failed with exit code {1}.{2}{2}{3}" -f $FailureLabel, $exitCode, [Environment]::NewLine, ($rawOutput -join [Environment]::NewLine))
    }

    return @($capturedOutput | ForEach-Object { $_.ToString() })
}

function Parse-KeyValueLines {
    param([Parameter(Mandatory = $true)][string[]]$Lines)

    $map = @{}
    foreach ($line in $Lines) {
        if ($line -match '^(?<key>[A-Z0-9_]+)=(?<value>.*)$') {
            $map[$matches["key"]] = $matches["value"]
        }
    }

    return $map
}

function Assert-Value {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Values,
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$Expected
    )

    if (-not $Values.ContainsKey($Key)) {
        throw ("Missing expected key: {0}" -f $Key)
    }

    if ($Values[$Key] -ne $Expected) {
        throw ("Expected {0}={1}, but saw {2}" -f $Key, $Expected, $Values[$Key])
    }
}

function Assert-PathExists {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw ("Expected path does not exist: {0}" -f $Path)
    }
}

function Convert-RationalToDouble {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return 0d
    }

    if ($Value -match '^(?<numerator>-?\d+(?:\.\d+)?)\/(?<denominator>-?\d+(?:\.\d+)?)$') {
        $denominator = [double]::Parse($matches["denominator"], [System.Globalization.CultureInfo]::InvariantCulture)
        if ($denominator -eq 0d) {
            return 0d
        }

        $numerator = [double]::Parse($matches["numerator"], [System.Globalization.CultureInfo]::InvariantCulture)
        return $numerator / $denominator
    }

    return [double]::Parse($Value, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Assert-ApproxEqual {
    param(
        [double]$Actual,
        [double]$Expected,
        [double]$Tolerance,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ([Math]::Abs($Actual - $Expected) -gt $Tolerance) {
        throw ("{0} expected {1} (+/- {2}), but saw {3}" -f $Label, $Expected, $Tolerance, $Actual)
    }
}

function Get-VideoProbeMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$FfprobePath,
        [Parameter(Mandatory = $true)][string]$InputPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $probeOutput = Invoke-CapturedCommand -FilePath $FfprobePath -ArgumentList @(
        "-v",
        "error",
        "-show_entries",
        "stream=codec_type,width,height,avg_frame_rate,r_frame_rate",
        "-of",
        "json",
        $InputPath
    ) -WorkingDirectory $WorkingDirectory -FailureLabel "ffprobe"

    $json = $probeOutput -join [Environment]::NewLine | ConvertFrom-Json
    $videoStream = $json.streams | Where-Object { $_.codec_type -eq "video" } | Select-Object -First 1
    $audioStream = $json.streams | Where-Object { $_.codec_type -eq "audio" } | Select-Object -First 1

    if ($null -eq $videoStream) {
        throw ("ffprobe did not return a video stream for: {0}" -f $InputPath)
    }

    return [pscustomobject]@{
        Width = [int]$videoStream.width
        Height = [int]$videoStream.height
        AvgFrameRate = [string]$videoStream.avg_frame_rate
        RealFrameRate = [string]$videoStream.r_frame_rate
        HasAudio = $null -ne $audioStream
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
$resolvedProjectPath = Resolve-FullPath -Path $ProjectPath -BasePath $resolvedRepoRoot
$framework = "net8.0-windows10.0.19041.0"
$harnessOutputDirectory = Join-Path $resolvedRepoRoot ("artifacts\ai-offline-smoke\{0}\{1}" -f $Configuration, $framework)
$buildOutputDirectoryArgument = if ($harnessOutputDirectory.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
    $harnessOutputDirectory
}
else {
    "{0}{1}" -f $harnessOutputDirectory, [System.IO.Path]::DirectorySeparatorChar
}
$harnessDllPath = Join-Path $harnessOutputDirectory "AiOfflineSmoke.dll"
$ffmpegPath = Resolve-FullPath -Path "Tools/ffmpeg/ffmpeg.exe" -BasePath $resolvedRepoRoot
$ffprobePath = Resolve-FullPath -Path "Tools/ffmpeg/ffprobe.exe" -BasePath $resolvedRepoRoot
$sampleScriptPath = Resolve-FullPath -Path "scripts/new-ai-smoke-samples.ps1" -BasePath $resolvedRepoRoot

Assert-PathExists -Path $ffmpegPath
Assert-PathExists -Path $ffprobePath
Assert-PathExists -Path $sampleScriptPath

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

Assert-PathExists -Path $harnessDllPath

$tempRootPath = Join-Path ([System.IO.Path]::GetTempPath()) ("vidvix-ai-offline-smoke-{0}" -f [Guid]::NewGuid().ToString("N"))
$sampleOutputDirectory = Join-Path $tempRootPath "samples"
$runOutputDirectory = Join-Path $tempRootPath "outputs"

Write-Step ("Repo root: {0}" -f $resolvedRepoRoot)
Write-Step ("Harness project: {0}" -f $resolvedProjectPath)
Write-Step ("Temporary workspace: {0}" -f $tempRootPath)

try {
    New-Item -ItemType Directory -Path $tempRootPath -Force | Out-Null
    New-Item -ItemType Directory -Path $runOutputDirectory -Force | Out-Null

    $sampleLines = Invoke-CapturedCommand -FilePath "powershell" -ArgumentList @(
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $sampleScriptPath,
        "-RepoRoot",
        $resolvedRepoRoot,
        "-OutputDirectory",
        $sampleOutputDirectory
    ) -WorkingDirectory $resolvedRepoRoot -FailureLabel "AI sample generation"

    $sampleValues = Parse-KeyValueLines -Lines $sampleLines
    $interpolationInputPath = $sampleValues["INTERPOLATION_SAMPLE"]
    $enhancementInputPath = $sampleValues["ENHANCEMENT_SAMPLE"]
    Assert-PathExists -Path $interpolationInputPath
    Assert-PathExists -Path $enhancementInputPath

    $catalogLines = Invoke-CapturedCommand -FilePath "dotnet" -ArgumentList @(
        $harnessDllPath,
        "catalog"
    ) -WorkingDirectory $resolvedRepoRoot -FailureLabel "AI runtime catalog smoke"
    $catalogValues = Parse-KeyValueLines -Lines $catalogLines

    Assert-Value -Values $catalogValues -Key "RIFE_CPU_STATE" -Expected "Available"
    Assert-Value -Values $catalogValues -Key "REALESRGAN_GPU_STATE" -Expected "Available"
    if ($catalogValues.ContainsKey("REALESRGAN_CPU_STATE")) {
        Write-Step ("Real-ESRGAN CPU state: {0}" -f $catalogValues["REALESRGAN_CPU_STATE"])
    }

    $interpolationOutputDirectory = Join-Path $runOutputDirectory "interpolation"
    $interpolationLines = Invoke-CapturedCommand -FilePath "dotnet" -ArgumentList @(
        $harnessDllPath,
        "interpolate",
        $interpolationInputPath,
        $interpolationOutputDirectory,
        "x2",
        "cpu",
        ".mp4",
        "interpolation-smoke",
        "false"
    ) -WorkingDirectory $resolvedRepoRoot -FailureLabel "AI interpolation smoke"
    $interpolationValues = Parse-KeyValueLines -Lines $interpolationLines
    Assert-Value -Values $interpolationValues -Key "RESULT_KIND" -Expected "Interpolation"
    Assert-Value -Values $interpolationValues -Key "OUTPUT_EXISTS" -Expected "True"
    Assert-Value -Values $interpolationValues -Key "SCALE_FACTOR" -Expected "2"
    Assert-Value -Values $interpolationValues -Key "INTERPOLATION_PASS_COUNT" -Expected "1"
    Assert-Value -Values $interpolationValues -Key "EXECUTION_DEVICE_KIND" -Expected "Cpu"
    Assert-Value -Values $interpolationValues -Key "PRESERVED_AUDIO" -Expected "True"
    $interpolationOutputPath = $interpolationValues["OUTPUT_PATH"]
    Assert-PathExists -Path $interpolationOutputPath
    $interpolationProbe = Get-VideoProbeMetadata -FfprobePath $ffprobePath -InputPath $interpolationOutputPath -WorkingDirectory $resolvedRepoRoot
    if ($interpolationProbe.Width -ne 48 -or $interpolationProbe.Height -ne 32) {
        throw ("Interpolation output expected 48x32, but saw {0}x{1}" -f $interpolationProbe.Width, $interpolationProbe.Height)
    }

    Assert-ApproxEqual -Actual (Convert-RationalToDouble -Value $interpolationProbe.AvgFrameRate) -Expected 12 -Tolerance 0.25 -Label "Interpolation avg frame rate"
    if (-not $interpolationProbe.HasAudio) {
        throw "Interpolation output is missing the original audio stream."
    }

    $enhancementExactOutputDirectory = Join-Path $runOutputDirectory "enhancement-exact"
    $enhancementExactLines = Invoke-CapturedCommand -FilePath "dotnet" -ArgumentList @(
        $harnessDllPath,
        "enhance",
        $enhancementInputPath,
        $enhancementExactOutputDirectory,
        "anime",
        "2",
        ".mp4",
        "enhancement-anime-x2"
    ) -WorkingDirectory $resolvedRepoRoot -FailureLabel "AI enhancement exact-scale smoke"
    $enhancementExactValues = Parse-KeyValueLines -Lines $enhancementExactLines
    Assert-Value -Values $enhancementExactValues -Key "RESULT_KIND" -Expected "Enhancement"
    Assert-Value -Values $enhancementExactValues -Key "OUTPUT_EXISTS" -Expected "True"
    Assert-Value -Values $enhancementExactValues -Key "MODEL_TIER" -Expected "Anime"
    Assert-Value -Values $enhancementExactValues -Key "REQUESTED_SCALE" -Expected "2"
    Assert-Value -Values $enhancementExactValues -Key "ACHIEVED_SCALE" -Expected "2"
    Assert-Value -Values $enhancementExactValues -Key "PASS_SCALES" -Expected "2"
    Assert-Value -Values $enhancementExactValues -Key "REQUIRES_DOWNSCALE" -Expected "False"
    Assert-Value -Values $enhancementExactValues -Key "EXECUTION_DEVICE_KIND" -Expected "Gpu"
    $enhancementExactOutputPath = $enhancementExactValues["OUTPUT_PATH"]
    Assert-PathExists -Path $enhancementExactOutputPath
    $enhancementExactProbe = Get-VideoProbeMetadata -FfprobePath $ffprobePath -InputPath $enhancementExactOutputPath -WorkingDirectory $resolvedRepoRoot
    if ($enhancementExactProbe.Width -ne 96 -or $enhancementExactProbe.Height -ne 64) {
        throw ("Exact enhancement output expected 96x64, but saw {0}x{1}" -f $enhancementExactProbe.Width, $enhancementExactProbe.Height)
    }

    Assert-ApproxEqual -Actual (Convert-RationalToDouble -Value $enhancementExactProbe.AvgFrameRate) -Expected 6 -Tolerance 0.25 -Label "Exact enhancement avg frame rate"
    if (-not $enhancementExactProbe.HasAudio) {
        throw "Exact enhancement output is missing the original audio stream."
    }

    $enhancementOverscaleOutputDirectory = Join-Path $runOutputDirectory "enhancement-overscale"
    $enhancementOverscaleLines = Invoke-CapturedCommand -FilePath "dotnet" -ArgumentList @(
        $harnessDllPath,
        "enhance",
        $enhancementInputPath,
        $enhancementOverscaleOutputDirectory,
        "standard",
        "3",
        ".mp4",
        "enhancement-standard-x3"
    ) -WorkingDirectory $resolvedRepoRoot -FailureLabel "AI enhancement overscale smoke"
    $enhancementOverscaleValues = Parse-KeyValueLines -Lines $enhancementOverscaleLines
    Assert-Value -Values $enhancementOverscaleValues -Key "RESULT_KIND" -Expected "Enhancement"
    Assert-Value -Values $enhancementOverscaleValues -Key "OUTPUT_EXISTS" -Expected "True"
    Assert-Value -Values $enhancementOverscaleValues -Key "MODEL_TIER" -Expected "Standard"
    Assert-Value -Values $enhancementOverscaleValues -Key "REQUESTED_SCALE" -Expected "3"
    Assert-Value -Values $enhancementOverscaleValues -Key "ACHIEVED_SCALE" -Expected "4"
    Assert-Value -Values $enhancementOverscaleValues -Key "PASS_SCALES" -Expected "4"
    Assert-Value -Values $enhancementOverscaleValues -Key "REQUIRES_DOWNSCALE" -Expected "True"
    Assert-Value -Values $enhancementOverscaleValues -Key "EXECUTION_DEVICE_KIND" -Expected "Gpu"
    $enhancementOverscaleOutputPath = $enhancementOverscaleValues["OUTPUT_PATH"]
    Assert-PathExists -Path $enhancementOverscaleOutputPath
    $enhancementOverscaleProbe = Get-VideoProbeMetadata -FfprobePath $ffprobePath -InputPath $enhancementOverscaleOutputPath -WorkingDirectory $resolvedRepoRoot
    if ($enhancementOverscaleProbe.Width -ne 144 -or $enhancementOverscaleProbe.Height -ne 96) {
        throw ("Overscale enhancement output expected 144x96, but saw {0}x{1}" -f $enhancementOverscaleProbe.Width, $enhancementOverscaleProbe.Height)
    }

    Assert-ApproxEqual -Actual (Convert-RationalToDouble -Value $enhancementOverscaleProbe.AvgFrameRate) -Expected 6 -Tolerance 0.25 -Label "Overscale enhancement avg frame rate"
    if (-not $enhancementOverscaleProbe.HasAudio) {
        throw "Overscale enhancement output is missing the original audio stream."
    }

    Write-Step "AI offline smoke regression passed."
    Write-Host ("INTERPOLATION_OUTPUT={0}" -f $interpolationOutputPath)
    Write-Host ("ENHANCEMENT_EXACT_OUTPUT={0}" -f $enhancementExactOutputPath)
    Write-Host ("ENHANCEMENT_OVERSCALE_OUTPUT={0}" -f $enhancementOverscaleOutputPath)
}
finally {
    if (-not $KeepTemp) {
        Remove-PathIfExists -Path $tempRootPath
    }
    else {
        Write-Step ("Temporary workspace preserved at: {0}" -f $tempRootPath)
    }
}
