[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$ManifestPath,
    [string]$WorkingDirectory,
    [switch]$KeepWorkingDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "[sync-ai-runtime-assets] $Message"
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

function Ensure-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Remove-PathIfExists {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$CandidatePath
    )

    $resolvedRootPath = [System.IO.Path]::GetFullPath($RootPath)
    $resolvedCandidatePath = [System.IO.Path]::GetFullPath($CandidatePath)
    $comparison = [System.StringComparison]::OrdinalIgnoreCase
    $normalizedRootPath = if ($resolvedRootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $resolvedRootPath
    }
    else {
        "{0}{1}" -f $resolvedRootPath, [System.IO.Path]::DirectorySeparatorChar
    }

    if ($resolvedCandidatePath.Equals($resolvedRootPath, $comparison) -or
        $resolvedCandidatePath.StartsWith($normalizedRootPath, $comparison)) {
        return $resolvedCandidatePath
    }

    throw ("Refusing to operate outside root '{0}': {1}" -f $resolvedRootPath, $resolvedCandidatePath)
}

function Invoke-CurlDownload {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $curlCommand = Get-Command -Name "curl.exe" -ErrorAction SilentlyContinue
    if ($null -eq $curlCommand) {
        throw "curl.exe is required for resilient GitHub asset downloads but was not found."
    }

    $arguments = @(
        "--http1.1",
        "-L",
        "--fail",
        "--silent",
        "--show-error",
        "--retry",
        "20",
        "--retry-delay",
        "5",
        "--retry-all-errors",
        "-C",
        "-",
        "-o",
        $DestinationPath,
        $Url
    )

    & $curlCommand.Source @arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw ("curl.exe failed with exit code {0} while downloading {1}" -f $exitCode, $Url)
    }
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
            Invoke-CurlDownload -Url $candidateUrl -DestinationPath $DestinationPath
            return
        }
        catch {
            $lastError = $_
            Write-Step ("Download failed, trying next candidate if available: {0}" -f $candidateUrl)
        }
    }

    throw $lastError
}

function Download-TextFile {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    Write-Step ("Downloading text asset {0}" -f $Url)
    Ensure-Directory -Path (Split-Path -Path $DestinationPath -Parent)
    Invoke-WebRequest -Headers @{ "User-Agent" = "Codex" } -Uri $Url -OutFile $DestinationPath
}

function Test-ZipArchive {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    try {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
        $archive.Dispose()
        return $true
    }
    catch {
        return $false
    }
}

function Get-Array {
    param(
        [Parameter(Mandatory = $true)]$Value
    )

    if ($null -eq $Value) {
        return @()
    }

    return @([object[]]$Value)
}

function Copy-ZipEntryToPath {
    param(
        [Parameter(Mandatory = $true)]$Archive,
        [Parameter(Mandatory = $true)][string]$EntryPath,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $entry = $Archive.GetEntry($EntryPath)
    if ($null -eq $entry) {
        throw ("Archive entry not found: {0}" -f $EntryPath)
    }

    Ensure-Directory -Path (Split-Path -Path $DestinationPath -Parent)

    $entryStream = $null
    $fileStream = $null

    try {
        $entryStream = $entry.Open()
        $fileStream = [System.IO.File]::Create($DestinationPath)
        $entryStream.CopyTo($fileStream)
    }
    finally {
        if ($null -ne $fileStream) {
            $fileStream.Dispose()
        }

        if ($null -ne $entryStream) {
            $entryStream.Dispose()
        }
    }
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$InputObject
    )

    Ensure-Directory -Path (Split-Path -Path $Path -Parent)
    $json = $InputObject | ConvertTo-Json -Depth 10
    Set-Content -LiteralPath $Path -Value $json -Encoding utf8
}

function Get-OptionalStringProperty {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    if ($InputObject.PSObject.Properties.Name -contains $PropertyName) {
        return [string]$InputObject.$PropertyName
    }

    return ""
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

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $scriptRoot "ai-runtime-lock.json"
}

$resolvedRepoRoot = Resolve-FullPath -Path $RepoRoot -BasePath (Get-Location).Path
$resolvedManifestPath = Resolve-FullPath -Path $ManifestPath -BasePath $resolvedRepoRoot
$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$targetRoot = Resolve-FullPath -Path ([string]$manifest.targetRoot) -BasePath $resolvedRepoRoot
$shouldCleanupWorkingDirectory = [string]::IsNullOrWhiteSpace($WorkingDirectory) -and -not $KeepWorkingDirectory
$workingRootPath = if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    Join-Path ([System.IO.Path]::GetTempPath()) ("vidvix-ai-runtime-sync-{0}" -f [Guid]::NewGuid().ToString("N"))
}
else {
    Resolve-FullPath -Path $WorkingDirectory -BasePath $resolvedRepoRoot
}
$downloadsPath = Join-Path $workingRootPath "downloads"

Add-Type -AssemblyName System.IO.Compression.FileSystem

Write-Step ("Repo root: {0}" -f $resolvedRepoRoot)
Write-Step ("Manifest: {0}" -f $resolvedManifestPath)
Write-Step ("Working directory: {0}" -f $workingRootPath)
Write-Step ("Target root: {0}" -f $targetRoot)

Ensure-Directory -Path $downloadsPath
Ensure-Directory -Path $targetRoot

foreach ($managedDirectory in (Get-Array -Value $manifest.managedDirectories)) {
    $managedPath = Assert-ChildPath -RootPath $targetRoot -CandidatePath (Join-Path $targetRoot ([string]$managedDirectory))
    Remove-PathIfExists -Path $managedPath
}

Ensure-Directory -Path (Assert-ChildPath -RootPath $targetRoot -CandidatePath (Join-Path $targetRoot "Licenses"))
Ensure-Directory -Path (Assert-ChildPath -RootPath $targetRoot -CandidatePath (Join-Path $targetRoot "Manifests"))

try {
    foreach ($package in (Get-Array -Value $manifest.packages)) {
        $archivePath = Join-Path $downloadsPath ([string]$package.archiveFileName)
        if (Test-ZipArchive -Path $archivePath) {
            Write-Step ("Reusing cached archive {0}" -f $archivePath)
        }
        else {
            Download-FileWithFallback -CandidateUrls (Get-Array -Value $package.downloadUrls) -DestinationPath $archivePath
        }

        Write-Step ("Curating package {0}" -f [string]$package.displayName)
        $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            foreach ($file in (Get-Array -Value $package.files)) {
                $destinationPath = Assert-ChildPath -RootPath $targetRoot -CandidatePath (Join-Path $targetRoot ([string]$file.target))
                Copy-ZipEntryToPath -Archive $archive -EntryPath ([string]$file.entryPath) -DestinationPath $destinationPath
            }
        }
        finally {
            $archive.Dispose()
        }

        foreach ($licenseDownload in (Get-Array -Value $package.licenseDownloads)) {
            $destinationPath = Assert-ChildPath -RootPath $targetRoot -CandidatePath (Join-Path $targetRoot ([string]$licenseDownload.target))
            Download-TextFile -Url ([string]$licenseDownload.url) -DestinationPath $destinationPath
        }

        $packageManifest = [ordered]@{
            id = [string]$package.id
            displayName = [string]$package.displayName
            sourceRepository = [string]$package.sourceRepository
            runtimeRepository = Get-OptionalStringProperty -InputObject $package -PropertyName "runtimeRepository"
            releaseTag = [string]$package.releaseTag
            releasePublishedAt = [string]$package.releasePublishedAt
            generatedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssK")
            archiveFileName = [string]$package.archiveFileName
            downloadUrls = @(Get-Array -Value $package.downloadUrls | ForEach-Object { [string]$_ })
            retainedFiles = @(Get-Array -Value $package.files | ForEach-Object {
                [ordered]@{
                    source = [string]$_.entryPath
                    target = [string]$_.target
                    purpose = [string]$_.purpose
                }
            })
            licenseFiles = @(Get-Array -Value $package.licenseDownloads | ForEach-Object {
                [ordered]@{
                    url = [string]$_.url
                    target = [string]$_.target
                    purpose = [string]$_.purpose
                }
            })
            removedCategories = @(Get-Array -Value $package.removedCategories | ForEach-Object { [string]$_ })
            notes = @(Get-Array -Value $package.notes | ForEach-Object { [string]$_ })
        }

        $packageManifestPath = Assert-ChildPath -RootPath $targetRoot -CandidatePath (Join-Path $targetRoot ("Manifests/{0}.json" -f [string]$package.id))
        Write-JsonFile -Path $packageManifestPath -InputObject $packageManifest
    }
}
finally {
    if ($shouldCleanupWorkingDirectory) {
        Remove-PathIfExists -Path $workingRootPath
    }
}

Write-Step "AI runtime assets synced successfully."
