[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$ManifestPath,
    [string]$OutputZipPath,
    [string]$WorkingDirectory,
    [switch]$KeepWorkingDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "[build-demucs-runtime] $Message"
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

function Reset-NetworkEnvironment {
    $env:ALL_PROXY = ""
    $env:HTTP_PROXY = ""
    $env:HTTPS_PROXY = ""
    $env:NO_PROXY = "*"
    $env:PIP_NO_CACHE_DIR = "1"
    $env:PIP_DISABLE_PIP_VERSION_CHECK = "1"
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

function Remove-GlobMatches {
    param([Parameter(Mandatory = $true)][string]$Pattern)

    Get-ChildItem -Path $Pattern -Force -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
    }
}

function Remove-PythonCacheArtifacts {
    param([Parameter(Mandatory = $true)][string]$RootPath)

    Get-ChildItem -LiteralPath $RootPath -Recurse -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq "__pycache__" } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }

    Get-ChildItem -LiteralPath $RootPath -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @(".pyc", ".pyo") } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
}

function Download-FileWithFallback {
    param(
        [Parameter(Mandatory = $true)][string[]]$CandidateUrls,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $lastError = $null

    foreach ($candidateUrl in $CandidateUrls) {
        try {
            Write-Step ("Downloading {0}" -f $candidateUrl)
            Invoke-WebRequest -Uri $candidateUrl -OutFile $DestinationPath
            return
        }
        catch {
            $lastError = $_
            Write-Step ("Download failed: {0}" -f $candidateUrl)
        }
    }

    throw $lastError
}

function Get-ManifestStringArray {
    param(
        [Parameter(Mandatory = $true)]$ManifestObject,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    if (-not ($ManifestObject.PSObject.Properties.Name -contains $PropertyName)) {
        return @()
    }

    $propertyValue = $ManifestObject.$PropertyName
    if ($null -eq $propertyValue) {
        return @()
    }

    return @([string[]]$propertyValue)
}

function Get-UriLeafName {
    param([Parameter(Mandatory = $true)][string]$Uri)

    $parsedUri = [System.Uri]$Uri
    $leafName = [System.IO.Path]::GetFileName($parsedUri.AbsolutePath)
    if ([string]::IsNullOrWhiteSpace($leafName)) {
        throw ("Unable to determine a file name from URI: {0}" -f $Uri)
    }

    return $leafName
}

function Compress-DirectoryWithRetry {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectoryPath,
        [Parameter(Mandatory = $true)][string]$DestinationArchivePath,
        [int]$MaxAttempts = 6,
        [int]$DelaySeconds = 5
    )

    $lastError = $null

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            Remove-PathIfExists -Path $DestinationArchivePath
            Compress-Archive -Path (Join-Path $SourceDirectoryPath "*") -DestinationPath $DestinationArchivePath -CompressionLevel Optimal
            return
        }
        catch {
            $lastError = $_
            if ($attempt -lt $MaxAttempts) {
                Write-Step ("Compression attempt {0}/{1} failed, retrying in {2}s..." -f $attempt, $MaxAttempts, $DelaySeconds)
                Start-Sleep -Seconds $DelaySeconds
            }
        }
    }

    throw $lastError
}

Reset-NetworkEnvironment

$scriptRoot = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    $PSScriptRoot
}
else {
    Split-Path -Path $PSCommandPath -Parent
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Join-Path $scriptRoot ".."
}

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $scriptRoot "demucs-runtime-lock.json"
}

$resolvedRepoRoot = Resolve-FullPath -Path $RepoRoot -BasePath (Get-Location).Path
$resolvedManifestPath = Resolve-FullPath -Path $ManifestPath -BasePath $resolvedRepoRoot
$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json
$resolvedBaseRuntimeArchivePath = if (
    ($manifest.PSObject.Properties.Name -contains "baseRuntimeArchivePath") -and
    -not [string]::IsNullOrWhiteSpace([string]$manifest.baseRuntimeArchivePath)
) {
    Resolve-FullPath -Path ([string]$manifest.baseRuntimeArchivePath) -BasePath $resolvedRepoRoot
}
else {
    $null
}
$resolvedOutputZipPath = if ([string]::IsNullOrWhiteSpace($OutputZipPath)) {
    Resolve-FullPath -Path $manifest.outputZipPath -BasePath $resolvedRepoRoot
}
else {
    Resolve-FullPath -Path $OutputZipPath -BasePath $resolvedRepoRoot
}

$workingRootPath = if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    Join-Path ([System.IO.Path]::GetTempPath()) ("vidvix-demucs-runtime-build-{0}" -f [Guid]::NewGuid().ToString("N"))
}
else {
    Resolve-FullPath -Path $WorkingDirectory -BasePath $resolvedRepoRoot
}

$downloadsPath = Join-Path $workingRootPath "downloads"
$runtimeRootPath = Join-Path $workingRootPath "runtime"
$pythonEmbedArchivePath = Join-Path $downloadsPath "python-embed.zip"
$getPipScriptPath = Join-Path $downloadsPath "get-pip.py"
$prefetchedPackagesPath = Join-Path $downloadsPath "runtime-packages"
$importCheckScriptPath = Join-Path $workingRootPath "import-check.py"
$stagedArchivePath = Join-Path $workingRootPath ([System.IO.Path]::GetFileName($resolvedOutputZipPath))

Write-Step ("Repo root: {0}" -f $resolvedRepoRoot)
Write-Step ("Manifest: {0}" -f $resolvedManifestPath)
Write-Step ("Output zip: {0}" -f $resolvedOutputZipPath)
Write-Step ("Working directory: {0}" -f $workingRootPath)

try {
    Remove-PathIfExists -Path $workingRootPath
    New-Item -ItemType Directory -Path $downloadsPath -Force | Out-Null
    New-Item -ItemType Directory -Path $runtimeRootPath -Force | Out-Null
    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($resolvedOutputZipPath)) -Force | Out-Null

    Write-Step "Downloading get-pip bootstrap"
    Download-FileWithFallback -CandidateUrls @(
        [string]$manifest.python.getPipUrl,
        "https://bootstrap.pypa.io/get-pip.py"
    ) -DestinationPath $getPipScriptPath

    if ($null -ne $resolvedBaseRuntimeArchivePath) {
        if (-not (Test-Path -LiteralPath $resolvedBaseRuntimeArchivePath)) {
            throw ("The base runtime archive was not found: {0}" -f $resolvedBaseRuntimeArchivePath)
        }

        Write-Step ("Extracting base runtime archive: {0}" -f $resolvedBaseRuntimeArchivePath)
        Expand-Archive -LiteralPath $resolvedBaseRuntimeArchivePath -DestinationPath $runtimeRootPath -Force
    }
    else {
        Write-Step ("Downloading Python embeddable runtime {0}" -f $manifest.python.version)
        Download-FileWithFallback -CandidateUrls @([string]$manifest.python.embedUrl) -DestinationPath $pythonEmbedArchivePath

        Write-Step "Extracting Python embeddable runtime"
        Expand-Archive -LiteralPath $pythonEmbedArchivePath -DestinationPath $runtimeRootPath -Force
    }

    $pythonExecutablePath = Join-Path $runtimeRootPath "python.exe"
    if (-not (Test-Path -LiteralPath $pythonExecutablePath)) {
        throw "python.exe was not found after extracting the embeddable runtime."
    }

    if ($null -eq $resolvedBaseRuntimeArchivePath) {
        $pythonPthFile = Get-ChildItem -LiteralPath $runtimeRootPath -Filter "python*._pth" -File | Select-Object -First 1
        if ($null -eq $pythonPthFile) {
            throw "The embeddable runtime did not contain a python*._pth file."
        }

        $pythonZipFile = Get-ChildItem -LiteralPath $runtimeRootPath -Filter "python*.zip" -File | Select-Object -First 1
        if ($null -eq $pythonZipFile) {
            throw "The embeddable runtime did not contain python*.zip."
        }

        Write-Step ("Configuring {0}" -f $pythonPthFile.Name)
        Set-Content -LiteralPath $pythonPthFile.FullName -Value @(
            $pythonZipFile.Name
            "."
            "Lib\site-packages"
            "import site"
        ) -Encoding Ascii
    }

    Write-Step "Bootstrapping pip into the embedded runtime"
    $bootstrapArgs = @($getPipScriptPath) + @(Get-ManifestStringArray -ManifestObject $manifest -PropertyName "bootstrapPackages")
    Invoke-ExternalCommand -FilePath $pythonExecutablePath -ArgumentList $bootstrapArgs -WorkingDirectory $workingRootPath

    $prefetchedRuntimePackagePaths = @()
    $prefetchPackageUrls = @(Get-ManifestStringArray -ManifestObject $manifest -PropertyName "prefetchPackageUrls")
    if ($prefetchPackageUrls.Count -gt 0) {
        New-Item -ItemType Directory -Path $prefetchedPackagesPath -Force | Out-Null

        foreach ($packageUrl in $prefetchPackageUrls) {
            $prefetchedPackagePath = Join-Path $prefetchedPackagesPath (Get-UriLeafName -Uri $packageUrl)
            Download-FileWithFallback -CandidateUrls @($packageUrl) -DestinationPath $prefetchedPackagePath
            $prefetchedRuntimePackagePaths += $prefetchedPackagePath
        }
    }

    $lockedRuntimePackages = @(Get-ManifestStringArray -ManifestObject $manifest -PropertyName "runtimePackages")
    if (($prefetchedRuntimePackagePaths.Count + $lockedRuntimePackages.Count) -eq 0) {
        throw "The manifest did not define any runtime packages to install."
    }

    Write-Step "Installing locked runtime packages"
    $pipInstallArgs = @(
        "-m",
        "pip",
        "install",
        "--upgrade",
        "--isolated",
        "--disable-pip-version-check",
        "--no-cache-dir",
        "--no-compile",
        "--trusted-host",
        "pypi.org",
        "--trusted-host",
        "files.pythonhosted.org",
        "--proxy="
    )

    foreach ($extraIndexUrl in (Get-ManifestStringArray -ManifestObject $manifest -PropertyName "extraIndexUrls")) {
        $pipInstallArgs += @("--extra-index-url", $extraIndexUrl)
    }

    $pipInstallArgs += @(Get-ManifestStringArray -ManifestObject $manifest -PropertyName "pipInstallArguments")
    $pipInstallArgs += $prefetchedRuntimePackagePaths + $lockedRuntimePackages
    Invoke-ExternalCommand -FilePath $pythonExecutablePath -ArgumentList $pipInstallArgs -WorkingDirectory $workingRootPath

    Write-Step "Removing build-only tools and Python cache files"
    Remove-PathIfExists -Path (Join-Path $runtimeRootPath "Scripts")
    Remove-GlobMatches -Pattern (Join-Path $runtimeRootPath "Lib\site-packages\pip*")
    Remove-GlobMatches -Pattern (Join-Path $runtimeRootPath "Lib\site-packages\wheel*")
    Remove-PythonCacheArtifacts -RootPath $runtimeRootPath

    $importCheckScriptLines = @(Get-ManifestStringArray -ManifestObject $manifest -PropertyName "importCheckScriptLines")
    if ($importCheckScriptLines.Count -eq 0) {
        $importCheckScriptLines = @(
            "import demucs",
            "import soundfile",
            "import torch",
            "import torchaudio",
            "",
            "backends = list(getattr(torchaudio, ""list_audio_backends"", lambda: [])())",
            "if ""soundfile"" not in backends:",
            "    raise RuntimeError(f""soundfile backend missing: {backends}"")",
            "",
            "print(f""demucs={demucs.__version__}"")",
            "print(f""torch={torch.__version__}"")",
            "print(f""torchaudio={torchaudio.__version__}"")",
            "print(f""soundfile={soundfile.__version__}"")"
        )
    }
    else {
        $importCheckScriptLines = @($importCheckScriptLines)
    }

    Set-Content -LiteralPath $importCheckScriptPath -Encoding Ascii -Value $importCheckScriptLines

    Write-Step "Running import verification against the cleaned runtime"
    Invoke-ExternalCommand -FilePath $pythonExecutablePath -ArgumentList @($importCheckScriptPath) -WorkingDirectory $workingRootPath

    Write-Step "Compressing runtime payload"
    Compress-DirectoryWithRetry -SourceDirectoryPath $runtimeRootPath -DestinationArchivePath $stagedArchivePath

    Remove-PathIfExists -Path $resolvedOutputZipPath
    Move-Item -LiteralPath $stagedArchivePath -Destination $resolvedOutputZipPath

    $runtimeFileCount = (Get-ChildItem -LiteralPath $runtimeRootPath -Recurse -File | Measure-Object).Count
    $archiveInfo = Get-Item -LiteralPath $resolvedOutputZipPath

    Write-Step ("Runtime package rebuilt successfully with {0} files." -f $runtimeFileCount)
    Write-Step ("Archive size: {0} bytes" -f $archiveInfo.Length)
}
finally {
    if (-not $KeepWorkingDirectory) {
        Remove-PathIfExists -Path $workingRootPath
    }
    else {
        Write-Step ("Working directory preserved at: {0}" -f $workingRootPath)
    }
}
