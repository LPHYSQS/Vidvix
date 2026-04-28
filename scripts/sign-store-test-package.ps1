param(
    [string]$TestPackageDirectory = "",
    [string]$Publisher = "CN=FA0F6293-29B7-43FB-AB9B-49D0FB5F198C",
    [switch]$TrustCertificate,
    [switch]$InstallPackage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-SdkToolPath {
    param([Parameter(Mandatory = $true)][string]$ToolName)

    $kitsRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    $match = Get-ChildItem -LiteralPath $kitsRoot -Recurse -Filter $ToolName -File -ErrorAction Stop |
        Where-Object { $_.FullName -match "\\x64\\" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($null -eq $match) {
        throw "Unable to locate $ToolName under $kitsRoot."
    }

    return $match.FullName
}

function Get-LatestStoreTestDirectory {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $submissionRoot = Join-Path $RepositoryRoot "artifacts\store-submission"
    $candidate = Get-ChildItem -LiteralPath $submissionRoot -Directory -ErrorAction Stop |
        Where-Object { $_.Name -like "*_Test" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $candidate) {
        throw "Unable to locate a Store test package directory under $submissionRoot."
    }

    return $candidate.FullName
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-TopLevelMsixPath {
    param([Parameter(Mandatory = $true)][string]$DirectoryPath)

    $msix = Get-ChildItem -LiteralPath $DirectoryPath -File -Filter *.msix -ErrorAction Stop |
        Select-Object -First 1

    if ($null -eq $msix) {
        throw "Unable to locate a top-level .msix package under $DirectoryPath."
    }

    return $msix.FullName
}

function Get-DependencyPackagePaths {
    param([Parameter(Mandatory = $true)][string]$DirectoryPath)

    $dependencyRoot = Join-Path $DirectoryPath "Dependencies"
    if (-not (Test-Path -LiteralPath $dependencyRoot)) {
        return @()
    }

    $dependencyPaths = New-Object System.Collections.Generic.List[string]

    Get-ChildItem -LiteralPath $dependencyRoot -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in ".appx", ".msix" } |
        ForEach-Object { $dependencyPaths.Add($_.FullName) | Out-Null }

    $architectureDirectory = switch ($env:PROCESSOR_ARCHITECTURE.ToUpperInvariant()) {
        "AMD64" { "x64" }
        "ARM64" { "arm64" }
        "X86" { "x86" }
        default { "" }
    }

    if (-not [string]::IsNullOrWhiteSpace($architectureDirectory)) {
        $architecturePath = Join-Path $dependencyRoot $architectureDirectory
        if (Test-Path -LiteralPath $architecturePath) {
            Get-ChildItem -LiteralPath $architecturePath -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Extension -in ".appx", ".msix" } |
                ForEach-Object { $dependencyPaths.Add($_.FullName) | Out-Null }
        }
    }

    return $dependencyPaths.ToArray()
}

function Invoke-SignToolWithRetry {
    param(
        [Parameter(Mandatory = $true)][string]$SignToolPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [int]$MaxAttempts = 5,
        [int]$DelaySeconds = 3
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        & $SignToolPath @Arguments
        if ($LASTEXITCODE -eq 0) {
            return
        }

        if ($attempt -eq $MaxAttempts) {
            throw "signtool.exe failed with exit code $LASTEXITCODE after $MaxAttempts attempts."
        }

        Start-Sleep -Seconds $DelaySeconds
    }
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$resolvedTestDirectory = if ([string]::IsNullOrWhiteSpace($TestPackageDirectory)) {
    Get-LatestStoreTestDirectory -RepositoryRoot $repositoryRoot
}
else {
    (Resolve-Path -LiteralPath $TestPackageDirectory).Path
}

$msixPath = Get-TopLevelMsixPath -DirectoryPath $resolvedTestDirectory
$packageFileName = [System.IO.Path]::GetFileNameWithoutExtension($msixPath)
$certificatePath = Join-Path $resolvedTestDirectory "$packageFileName.cer"
$pfxPath = Join-Path ([System.IO.Path]::GetTempPath()) "$packageFileName.pfx"
$signToolPath = Get-SdkToolPath -ToolName "signtool.exe"
$shouldTrustCertificate = $TrustCertificate.IsPresent -or $InstallPackage.IsPresent

if ($shouldTrustCertificate -and -not (Test-IsAdministrator)) {
    throw "Trusting the Store test certificate requires an elevated PowerShell session. Re-run this script as Administrator with -TrustCertificate or -InstallPackage."
}

$securePassword = ConvertTo-SecureString -String ([Guid]::NewGuid().Guid) -AsPlainText -Force
$certificate = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $Publisher `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature `
    -NotAfter (Get-Date).AddYears(2) `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3,1.3.6.1.4.1.311.84.3.2") `
    -FriendlyName "Vidvix Store Test Certificate"

try {
    Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $certificate -FilePath $certificatePath -Force | Out-Null

    Invoke-SignToolWithRetry -SignToolPath $signToolPath -Arguments @(
        "sign",
        "/fd",
        "SHA256",
        "/f",
        $pfxPath,
        "/p",
        [System.Net.NetworkCredential]::new("", $securePassword).Password,
        $msixPath
    )

    if ($shouldTrustCertificate) {
        Import-Certificate -FilePath $certificatePath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
        Import-Certificate -FilePath $certificatePath -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null

        & $signToolPath verify /pa /v $msixPath
        if ($LASTEXITCODE -ne 0) {
            throw "signtool.exe failed with exit code $LASTEXITCODE while verifying $msixPath."
        }
    }

    if ($InstallPackage) {
        $dependencyPaths = Get-DependencyPackagePaths -DirectoryPath $resolvedTestDirectory
        if ($dependencyPaths.Count -gt 0) {
            Add-AppxPackage -Path $msixPath -DependencyPath $dependencyPaths -ForceApplicationShutdown
        }
        else {
            Add-AppxPackage -Path $msixPath -ForceApplicationShutdown
        }
    }
}
finally {
    if (Test-Path -LiteralPath $pfxPath) {
        Remove-Item -LiteralPath $pfxPath -Force
    }

    if ($null -ne $certificate) {
        Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $certificate.Thumbprint) -DeleteKey -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "Store test package signed successfully."
Write-Host "TestPackageDirectory: $resolvedTestDirectory"
Write-Host "SignedPackage: $msixPath"
Write-Host "Certificate: $certificatePath"
if ($InstallPackage) {
    Write-Host "PackageInstall: completed"
}
