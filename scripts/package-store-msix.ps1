param(
    [string]$PublishDir = "",
    [string]$SubmissionDir = "",
    [string]$PublishProfile = "Store-win-x64",
    [string]$PackageName = "D787ABC4.Vidvix",
    [string]$Publisher = "CN=FA0F6293-29B7-43FB-AB9B-49D0FB5F198C",
    [string]$PublisherDisplayName = "",
    [string]$DisplayName = "Vidvix",
    [string]$Description = "Local-first Windows video editing, merging, and offline AI workspace",
    [string]$Version = "1.2604.3.0",
    [string]$ApplicationId = "Vidvix",
    [string[]]$Languages = @("zh-CN", "en-US"),
    [string]$MinVersion = "10.0.17763.0",
    [string]$MaxVersionTested = "10.0.26100.0",
    [string]$OutputFileName = "Vidvix-v1.2604.3.0-store-x64.msix",
    [string]$WindowsAppRuntimePackageName = "",
    [string]$WindowsAppRuntimePublisher = "",
    [string]$WindowsAppRuntimeMinVersion = "",
    [string]$VclibsPackageName = "",
    [string]$VclibsPublisher = "",
    [string]$VclibsMinVersion = "",
    [switch]$SkipPublishBuild,
    [switch]$SignPackage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
    $PublisherDisplayName = [string]([char]0x5DF2) + [char]0x901D + [char]0x60C5 + [char]0x6B87
}

$excludedGeneratedNames = @(
    "AppxManifest.xml",
    "AppxBlockMap.xml",
    "AppxSignature.p7x",
    "[Content_Types].xml",
    "resources.pri"
)

$excludedGeneratedExtensions = @(
    ".msix",
    ".appx",
    ".msixupload",
    ".appxupload",
    ".emsix",
    ".eappx"
)

function Get-ResolvedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Resolve-Path -LiteralPath $Path).Path
}

function Get-FullPath {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Escape-Xml {
    param([AllowNull()][string]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return [System.Security.SecurityElement]::Escape($Value)
}

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

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [int[]]$AllowedExitCodes = @(0)
    )

    & $FilePath @Arguments
    if ($AllowedExitCodes -notcontains $LASTEXITCODE) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Resolve-StorePublishDirectory {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) {
        return (Get-FullPath -BasePath $RepositoryRoot -Path $PublishDir)
    }

    return (Join-Path ([System.IO.Path]::GetTempPath()) "Vidvix-store-publish\win-x64")
}

function Resolve-StoreSubmissionDirectory {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    if (-not [string]::IsNullOrWhiteSpace($SubmissionDir)) {
        return (Get-FullPath -BasePath $RepositoryRoot -Path $SubmissionDir)
    }

    return (Join-Path $RepositoryRoot "artifacts\store-submission")
}

function Invoke-StorePublishBuild {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$ResolvedPublishDir
    )

    $projectPath = Join-Path $RepositoryRoot "Vidvix.csproj"
    $relativePublishProfile = "Properties\PublishProfiles\$PublishProfile.pubxml"
    $publishProfilePath = Join-Path $RepositoryRoot $relativePublishProfile
    if (-not (Test-Path -LiteralPath $publishProfilePath)) {
        throw "Unable to locate publish profile: $publishProfilePath"
    }

    Invoke-Native -FilePath "dotnet" -Arguments @(
        "publish",
        $projectPath,
        "-p:PublishProfile=$relativePublishProfile",
        "-p:PublishDir=$ResolvedPublishDir\"
    )
}

function Test-StoreReadyPublishLayout {
    param(
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$ApplicationName
    )

    $resolvedRoot = Get-ResolvedPath -Path $RootPath
    $depsPath = Join-Path $resolvedRoot "$ApplicationName.deps.json"
    if (-not (Test-Path -LiteralPath $depsPath)) {
        throw "Store packaging requires a non-single-file publish layout. Missing dependency manifest: $depsPath"
    }

    $rootDlls = Get-ChildItem -LiteralPath $resolvedRoot -File -Filter *.dll -ErrorAction Stop
    if ($rootDlls.Count -eq 0) {
        throw "Store packaging requires an app-local runtime layout. '$resolvedRoot' looks like a single-file offline publish, which is not supported for this Store MSIX flow."
    }
}

function Resolve-WindowsAppRuntimeDependency {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$ApplicationName
    )

    if (-not [string]::IsNullOrWhiteSpace($WindowsAppRuntimePackageName) -and
        -not [string]::IsNullOrWhiteSpace($WindowsAppRuntimePublisher) -and
        -not [string]::IsNullOrWhiteSpace($WindowsAppRuntimeMinVersion)) {
        return [pscustomobject]@{
            Name = $WindowsAppRuntimePackageName
            Publisher = $WindowsAppRuntimePublisher
            MinVersion = $WindowsAppRuntimeMinVersion
            Source = "script parameters"
        }
    }

    $candidateManifests = Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot "obj") -Recurse -Filter "AppxManifest.xml" -File -ErrorAction Stop |
        Where-Object { $_.FullName -match "\\MsixContent\\" } |
        Sort-Object FullName -Descending

    foreach ($candidateManifest in $candidateManifests) {
        try {
            [xml]$manifestXml = Get-Content -LiteralPath $candidateManifest.FullName -Raw
            $identity = $manifestXml.Package.Identity
            if ($null -eq $identity) {
                continue
            }

            $identityName = [string]$identity.Name
            if ([string]::IsNullOrWhiteSpace($identityName) -or $identityName -eq $ApplicationName) {
                continue
            }

            if ($identityName -notlike "Microsoft.WindowsAppRuntime.*") {
                continue
            }

            return [pscustomobject]@{
                Name = $identityName
                Publisher = [string]$identity.Publisher
                MinVersion = [string]$identity.Version
                Source = $candidateManifest.FullName
            }
        }
        catch {
        }
    }

    throw "Unable to resolve the Windows App SDK framework dependency from the generated build artifacts. Build the Store publish profile first or supply -WindowsAppRuntimePackageName, -WindowsAppRuntimePublisher, and -WindowsAppRuntimeMinVersion explicitly."
}

function Resolve-VclibsDesktopDependency {
    if (-not [string]::IsNullOrWhiteSpace($VclibsPackageName) -and
        -not [string]::IsNullOrWhiteSpace($VclibsPublisher) -and
        -not [string]::IsNullOrWhiteSpace($VclibsMinVersion)) {
        return [pscustomobject]@{
            Name = $VclibsPackageName
            Publisher = $VclibsPublisher
            MinVersion = $VclibsMinVersion
            Source = "script parameters"
        }
    }

    $sdkManifestPath = "C:\Program Files (x86)\Microsoft SDKs\Windows Kits\10\ExtensionSDKs\Microsoft.VCLibs.Desktop\14.0\SDKManifest.xml"
    if (-not (Test-Path -LiteralPath $sdkManifestPath)) {
        throw "Unable to locate the VCLibs Desktop SDK manifest at $sdkManifestPath."
    }

    [xml]$rawManifest = Get-Content -LiteralPath $sdkManifestPath -Raw
    $fileList = $rawManifest.FileList
    if ($null -eq $fileList) {
        throw "The VCLibs Desktop SDK manifest is missing the FileList root element: $sdkManifestPath"
    }

    $frameworkIdentity = $fileList.GetAttribute("FrameworkIdentity-Retail")
    if ([string]::IsNullOrWhiteSpace($frameworkIdentity)) {
        throw "The VCLibs Desktop SDK manifest does not declare FrameworkIdentity-Retail: $sdkManifestPath"
    }

    $nameMatch = [regex]::Match($frameworkIdentity, "Name\s*=\s*(?<value>[^,]+)")
    $versionMatch = [regex]::Match($frameworkIdentity, "MinVersion\s*=\s*(?<value>[^,]+)")
    $publisherMatch = [regex]::Match($frameworkIdentity, "Publisher\s*=\s*'(?<value>[^']+)'")
    if (-not $nameMatch.Success -or -not $versionMatch.Success -or -not $publisherMatch.Success) {
        throw "Unable to parse the VCLibs Desktop framework identity: $frameworkIdentity"
    }

    return [pscustomobject]@{
        Name = $nameMatch.Groups["value"].Value.Trim()
        Publisher = $publisherMatch.Groups["value"].Value.Trim()
        MinVersion = $versionMatch.Groups["value"].Value.Trim()
        Source = $sdkManifestPath
    }
}

function Get-RelativeFileSet {
    param([Parameter(Mandatory = $true)][string]$RootPath)

    $resolvedRoot = Get-ResolvedPath -Path $RootPath
    $prefix = $resolvedRoot.TrimEnd("\") + "\"
    $set = New-Object "System.Collections.Generic.HashSet[string]" ([System.StringComparer]::OrdinalIgnoreCase)

    Get-ChildItem -LiteralPath $resolvedRoot -File -Recurse | ForEach-Object {
        $extension = $_.Extension.ToLowerInvariant()
        if ($excludedGeneratedNames -contains $_.Name -or $excludedGeneratedExtensions -contains $extension) {
            return
        }

        $relativePath = $_.FullName.Substring($prefix.Length).Replace("/", "\")
        $set.Add($relativePath) | Out-Null
    }

    return $set
}

function Invoke-Robocopy {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [string[]]$ExtraArgs = @()
    )

    $null = New-Item -ItemType Directory -Path $Destination -Force
    & robocopy.exe $Source $Destination @ExtraArgs
    if ($LASTEXITCODE -gt 7) {
        throw "Robocopy failed with exit code $LASTEXITCODE while copying '$Source' to '$Destination'."
    }
}

$repoRoot = Get-ResolvedPath -Path (Join-Path $PSScriptRoot "..")
$publishDirPath = Resolve-StorePublishDirectory -RepositoryRoot $repoRoot
$submissionDirPath = Resolve-StoreSubmissionDirectory -RepositoryRoot $repoRoot
$assetsSourcePath = Get-ResolvedPath -Path (Join-Path $repoRoot "Assets")
$makeAppxPath = Get-SdkToolPath -ToolName "makeappx.exe"
$makePriPath = Get-SdkToolPath -ToolName "makepri.exe"
$signToolPath = Get-SdkToolPath -ToolName "signtool.exe"

if (-not $SkipPublishBuild) {
    Invoke-StorePublishBuild -RepositoryRoot $repoRoot -ResolvedPublishDir $publishDirPath
}

$publishDirPath = Get-ResolvedPath -Path $publishDirPath
$null = New-Item -ItemType Directory -Path $submissionDirPath -Force
$submissionDirPath = Get-ResolvedPath -Path $submissionDirPath
Test-StoreReadyPublishLayout -RootPath $publishDirPath -ApplicationName $ApplicationId
$windowsAppRuntimeDependency = Resolve-WindowsAppRuntimeDependency -RepositoryRoot $repoRoot -ApplicationName $ApplicationId
$vclibsDependency = Resolve-VclibsDesktopDependency

$workingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "Vidvix-msix-store-package"
$stageDir = Join-Path $workingRoot "stage"
$inspectionDir = Join-Path $workingRoot "inspect"
$outputPackagePath = Join-Path $submissionDirPath $OutputFileName
$tempPackagePath = Join-Path $workingRoot $OutputFileName
$manifestPath = Join-Path $stageDir "AppxManifest.xml"
$resourcePriPath = Join-Path $stageDir "resources.pri"
$resourcePriBuildPath = Join-Path $workingRoot "resources.pri"
$priConfigPath = Join-Path $workingRoot "priconfig.xml"
$tempPfxPath = Join-Path $workingRoot "store-signing-temp.pfx"
$tempCerPath = Join-Path $workingRoot "store-signing-temp.cer"
$stagedAppPriPath = Join-Path $stageDir "Vidvix.pri"
$heldAppPriPath = Join-Path $workingRoot "Vidvix.pri"

$requiredPublishFiles = @(
    "Vidvix.exe",
    "Vidvix.deps.json",
    "Vidvix.pri",
    "Resources\Localization\zh-CN\common.json",
    "Resources\Localization\en-US\common.json"
)

foreach ($requiredFile in $requiredPublishFiles) {
    $candidatePath = Join-Path $publishDirPath $requiredFile
    if (-not (Test-Path -LiteralPath $candidatePath)) {
        throw "Required publish file is missing: $candidatePath"
    }
}

if (Test-Path -LiteralPath $workingRoot) {
    Remove-Item -LiteralPath $workingRoot -Recurse -Force
}

$null = New-Item -ItemType Directory -Path $workingRoot -Force

Invoke-Robocopy -Source $publishDirPath -Destination $stageDir -ExtraArgs @(
    "/E",
    "/R:2",
    "/W:2",
    "/NFL",
    "/NDL",
    "/NJH",
    "/NJS",
    "/XF",
    "*.msix",
    "*.appx",
    "*.msixupload",
    "*.appxupload",
    "*.emsix",
    "*.eappx",
    "AppxManifest.xml",
    "resources.pri",
    "AppxBlockMap.xml",
    "AppxSignature.p7x",
    "[Content_Types].xml"
)

Invoke-Robocopy -Source $assetsSourcePath -Destination (Join-Path $stageDir "Assets") -ExtraArgs @(
    "/E",
    "/R:2",
    "/W:2",
    "/NFL",
    "/NDL",
    "/NJH",
    "/NJS"
)

$sourceFiles = Get-RelativeFileSet -RootPath $publishDirPath
$stageFiles = Get-RelativeFileSet -RootPath $stageDir
$missingFiles = New-Object "System.Collections.Generic.List[string]"

foreach ($relativePath in $sourceFiles) {
    if (-not $stageFiles.Contains($relativePath)) {
        $missingFiles.Add($relativePath) | Out-Null
    }
}

if ($missingFiles.Count -gt 0) {
    throw "The MSIX staging folder is missing source files:`n$($missingFiles -join [Environment]::NewLine)"
}

$resourceXml = ($Languages | ForEach-Object {
        "    <Resource Language=""$(Escape-Xml $_)"" />"
    }) -join [Environment]::NewLine

$runtimeDependencyXml = @(
    "    <PackageDependency Name=""$(Escape-Xml $windowsAppRuntimeDependency.Name)"" Publisher=""$(Escape-Xml $windowsAppRuntimeDependency.Publisher)"" MinVersion=""$(Escape-Xml $windowsAppRuntimeDependency.MinVersion)"" />",
    "    <PackageDependency Name=""$(Escape-Xml $vclibsDependency.Name)"" Publisher=""$(Escape-Xml $vclibsDependency.Publisher)"" MinVersion=""$(Escape-Xml $vclibsDependency.MinVersion)"" />"
) -join [Environment]::NewLine

$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap desktop rescap">
  <Identity
    Name="$(Escape-Xml $PackageName)"
    Publisher="$(Escape-Xml $Publisher)"
    Version="$(Escape-Xml $Version)"
    ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>$(Escape-Xml $DisplayName)</DisplayName>
    <PublisherDisplayName>$(Escape-Xml $PublisherDisplayName)</PublisherDisplayName>
    <Description>$(Escape-Xml $Description)</Description>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Resources>
$resourceXml
  </Resources>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="$(Escape-Xml $MinVersion)" MaxVersionTested="$(Escape-Xml $MaxVersionTested)" />
$runtimeDependencyXml
  </Dependencies>
  <Applications>
    <Application Id="$(Escape-Xml $ApplicationId)" Executable="Vidvix.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="$(Escape-Xml $DisplayName)"
        Description="$(Escape-Xml $Description)"
        BackgroundColor="transparent"
        Square150x150Logo="Assets\Square150x150Logo.scale-200.png"
        Square44x44Logo="Assets\Square44x44Logo.scale-200.png"
        AppListEntry="default">
        <uap:DefaultTile
          Wide310x150Logo="Assets\Wide310x150Logo.scale-200.png"
          Square71x71Logo="Assets\Square44x44Logo.scale-200.png"
          ShortName="$(Escape-Xml $DisplayName)" />
        <uap:SplashScreen Image="Assets\SplashScreen.scale-200.png" />
      </uap:VisualElements>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@

Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding utf8

Invoke-Native -FilePath $makePriPath -Arguments @(
    "createconfig",
    "/cf", $priConfigPath,
    "/dq", $Languages[0],
    "/pv", "10.0.0",
    "/o"
)

Move-Item -LiteralPath $stagedAppPriPath -Destination $heldAppPriPath -Force

try {
    Invoke-Native -FilePath $makePriPath -Arguments @(
        "new",
        "/pr", $stageDir,
        "/cf", $priConfigPath,
        "/mn", $manifestPath,
        "/of", $resourcePriBuildPath,
        "/o"
    )
}
finally {
    if ((Test-Path -LiteralPath $heldAppPriPath) -and -not (Test-Path -LiteralPath $stagedAppPriPath)) {
        Move-Item -LiteralPath $heldAppPriPath -Destination $stagedAppPriPath -Force
    }
}

$generatedResourcePriFiles = Get-ChildItem -LiteralPath $workingRoot -Filter "resources*.pri" -File
if ($generatedResourcePriFiles.Count -eq 0) {
    throw "No store resource PRI files were generated."
}

foreach ($generatedResourcePriFile in $generatedResourcePriFiles) {
    Copy-Item -LiteralPath $generatedResourcePriFile.FullName -Destination (Join-Path $stageDir $generatedResourcePriFile.Name) -Force
}

if (-not (Test-Path -LiteralPath $resourcePriPath)) {
    throw "resources.pri was not generated."
}

if (Test-Path -LiteralPath $tempPackagePath) {
    Remove-Item -LiteralPath $tempPackagePath -Force
}

Invoke-Native -FilePath $makeAppxPath -Arguments @(
    "pack",
    "/v",
    "/o",
    "/h", "SHA256",
    "/d", $stageDir,
    "/p", $tempPackagePath,
    "/pri", $makePriPath
)

if ($SignPackage) {
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
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") `
        -FriendlyName "Vidvix Store Packaging Temporary Certificate"

    $trustedPeopleCertificate = $null
    $trustedRootCertificate = $null

    try {
        Export-PfxCertificate -Cert $certificate -FilePath $tempPfxPath -Password $securePassword | Out-Null
        Export-Certificate -Cert $certificate -FilePath $tempCerPath | Out-Null
        $trustedPeopleCertificate = Import-Certificate -FilePath $tempCerPath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople"
        $trustedRootCertificate = Import-Certificate -FilePath $tempCerPath -CertStoreLocation "Cert:\CurrentUser\Root"

        Invoke-Native -FilePath $signToolPath -Arguments @(
            "sign",
            "/fd", "SHA256",
            "/f", $tempPfxPath,
            "/p", [System.Net.NetworkCredential]::new("", $securePassword).Password,
            $tempPackagePath
        )

        Invoke-Native -FilePath $signToolPath -Arguments @(
            "verify",
            "/pa",
            "/v",
            $tempPackagePath
        )
    }
    finally {
        if ($null -ne $trustedPeopleCertificate) {
            Remove-Item -LiteralPath ("Cert:\CurrentUser\TrustedPeople\" + $trustedPeopleCertificate.Thumbprint) -Force -ErrorAction SilentlyContinue
        }

        if ($null -ne $trustedRootCertificate) {
            Remove-Item -LiteralPath ("Cert:\CurrentUser\Root\" + $trustedRootCertificate.Thumbprint) -Force -ErrorAction SilentlyContinue
        }

        if ($null -ne $certificate) {
            Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $certificate.Thumbprint) -DeleteKey -Force -ErrorAction SilentlyContinue
        }
    }
}

if (Test-Path -LiteralPath $inspectionDir) {
    Remove-Item -LiteralPath $inspectionDir -Recurse -Force
}

Invoke-Native -FilePath $makeAppxPath -Arguments @(
    "unpack",
    "/o",
    "/p", $tempPackagePath,
    "/d", $inspectionDir
)

$unpackedManifest = Join-Path $inspectionDir "AppxManifest.xml"
if (-not (Test-Path -LiteralPath $unpackedManifest)) {
    throw "Unable to inspect the generated MSIX because the unpacked manifest is missing."
}

$manifestContent = Get-Content -LiteralPath $unpackedManifest -Raw
foreach ($language in $Languages) {
    if ($manifestContent -notmatch [regex]::Escape("Language=""$language""")) {
        throw "Generated package manifest does not declare the expected language '$language'."
    }
}

Copy-Item -LiteralPath $tempPackagePath -Destination $outputPackagePath -Force

$packageItem = Get-Item -LiteralPath $outputPackagePath
$sourceFileCount = $sourceFiles.Count
$packagedFileCount = (Get-ChildItem -LiteralPath $inspectionDir -File -Recurse).Count

Write-Host ""
Write-Host "MSIX packaging completed successfully."
Write-Host "Package: $($packageItem.FullName)"
Write-Host "SizeBytes: $($packageItem.Length)"
Write-Host "SourceFileCountVerified: $sourceFileCount"
Write-Host "UnpackedFileCount: $packagedFileCount"
Write-Host "DeclaredLanguages: $($Languages -join ', ')"
Write-Host "WindowsAppRuntimeDependency: $($windowsAppRuntimeDependency.Name) >= $($windowsAppRuntimeDependency.MinVersion)"
Write-Host "WindowsAppRuntimeDependencySource: $($windowsAppRuntimeDependency.Source)"
Write-Host "VclibsDependency: $($vclibsDependency.Name) >= $($vclibsDependency.MinVersion)"
Write-Host "VclibsDependencySource: $($vclibsDependency.Source)"
