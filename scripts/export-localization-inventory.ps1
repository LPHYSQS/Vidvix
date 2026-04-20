[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs\localization-string-inventory.csv')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$script:CjkPattern = '[\u3400-\u4dbf\u4e00-\u9fff]'
$script:LabelLikeXamlProperties = @(
    'Label',
    'Header',
    'Title',
    'PlaceholderText',
    'ToolTipService.ToolTip',
    'AutomationProperties.Name')
$script:HotspotPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    'ViewModels/MergeViewModel.cs',
    'ViewModels/MergeViewModel.AudioVideoCompose.cs',
    'Core/Models/ApplicationConfiguration.cs',
    'Views/MergePage.xaml',
    'Views/MainWindow.xaml',
    'Services/AudioSeparationWorkflowService.cs',
    'ViewModels/SplitAudioWorkspaceViewModel.cs',
    'ViewModels/MainViewModel.Execution.cs',
    'Views/Controls/ApplicationSettingsPane.xaml'
) | ForEach-Object { [void]$script:HotspotPaths.Add($_) }

function Test-ContainsCjk {
    param([string]$Text)

    return -not [string]::IsNullOrWhiteSpace($Text) -and $Text -match $script:CjkPattern
}

function Convert-ToRelativePath {
    param(
        [string]$RootPath,
        [string]$FullPath
    )

    $rootPrefix = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\') + '\'
    $full = [System.IO.Path]::GetFullPath($FullPath)

    if ($full.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($rootPrefix.Length).Replace('\', '/')
    }

    return $full.Replace('\', '/')
}

function Convert-ToKeySegment {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return 'item'
    }

    $parts = @([regex]::Matches($Text, '[A-Za-z0-9]+') | ForEach-Object { $_.Value })
    if (-not $parts -or $parts.Count -eq 0) {
        return 'item'
    }

    $first = $parts[0].Substring(0, 1).ToLowerInvariant() + $parts[0].Substring(1)
    $rest = foreach ($part in $parts | Select-Object -Skip 1) {
        if ($part.Length -eq 1) {
            $part.ToUpperInvariant()
        }
        else {
            $part.Substring(0, 1).ToUpperInvariant() + $part.Substring(1)
        }
    }

    return ($first + ($rest -join '')) -replace '[^a-zA-Z0-9]', ''
}

function Get-ModuleName {
    param([string]$RelativePath)

    switch -Regex ($RelativePath) {
        '^Views/Controls/ApplicationSettingsPane' { return 'settings' }
        '^Views/MainWindow|^ViewModels/MainViewModel|^Views/MainWindow\.' { return 'main-window' }
        'SplitAudio|AudioSeparation' { return 'split-audio' }
        'Terminal' { return 'terminal' }
        'MediaDetail|MediaInfo' { return 'media-details' }
        'Trim' { return 'trim' }
        'Merge|AudioVideoCompose|VideoJoin|AudioJoin' { return 'merge' }
        'ApplicationConfiguration|ProcessingModeOption|ProcessingWorkspaceProfile|OutputFormatOption|ThemePreferenceOption|TranscodingModeOption|DemucsAccelerationModeOption' { return 'common' }
        default { return 'common' }
    }
}

function Get-ModuleKeySegment {
    param([string]$ModuleName)

    switch ($ModuleName) {
        'main-window' { return 'mainWindow' }
        'split-audio' { return 'splitAudio' }
        'media-details' { return 'mediaDetails' }
        default { return (Convert-ToKeySegment $ModuleName) }
    }
}

function Get-FileAreaSegment {
    param([string]$RelativePath)

    $fileName = [System.IO.Path]::GetFileName($RelativePath)
    $stem = $fileName -replace '\.xaml\.cs$', '' -replace '\.xaml$', '' -replace '\.cs$', ''

    switch ($stem) {
        'ApplicationSettingsPane' { return 'applicationSettings' }
        'MainWindow' { return 'shell' }
        default { return (Convert-ToKeySegment $stem) }
    }
}

function Get-XamlCategory {
    param([string]$PropertyName)

    if ($script:LabelLikeXamlProperties -contains $PropertyName) {
        return 'XamlLabel'
    }

    return 'XamlText'
}

function Get-CodeCategory {
    param(
        [string]$RelativePath,
        [string]$LineText
    )

    if ($RelativePath -eq 'Core/Models/ApplicationConfiguration.cs' -or $LineText -match '(DisplayName|Description|displayName|description|headerTitle|headerDescription|Label|Title)') {
        return 'ConfigurationDisplayText'
    }

    if ($RelativePath.StartsWith('ViewModels/', [System.StringComparison]::OrdinalIgnoreCase)) {
        return 'ViewModelMessage'
    }

    if ($RelativePath.StartsWith('Views/', [System.StringComparison]::OrdinalIgnoreCase)) {
        return 'ViewModelMessage'
    }

    if ($RelativePath.StartsWith('Services/', [System.StringComparison]::OrdinalIgnoreCase)) {
        return 'ServiceUserMessage'
    }

    if ($RelativePath.StartsWith('Utils/', [System.StringComparison]::OrdinalIgnoreCase)) {
        if ($LineText -match '\bthrow new\b') {
            return 'ServiceUserMessage'
        }

        return 'NonUiIgnore'
    }

    if ($RelativePath.StartsWith('Core/', [System.StringComparison]::OrdinalIgnoreCase)) {
        return 'ConfigurationDisplayText'
    }

    return 'NonUiIgnore'
}

function Get-Priority {
    param(
        [string]$RelativePath,
        [string]$ModuleName,
        [string]$Category
    )

    if ($Category -eq 'NonUiIgnore') {
        return 'P2'
    }

    if ($script:HotspotPaths.Contains($RelativePath)) {
        return 'P0'
    }

    switch ($ModuleName) {
        'settings' { return 'P0' }
        'main-window' { return 'P0' }
        'split-audio' { return 'P0' }
        'merge' { return 'P0' }
        'trim' { return 'P1' }
        'terminal' { return 'P1' }
        'media-details' { return 'P1' }
        default { return 'P1' }
    }
}

function Get-Status {
    param([string]$Category)

    if ($Category -eq 'NonUiIgnore') {
        return 'Ignored'
    }

    return 'Pending'
}

function Get-ElementSegment {
    param(
        [string]$LineText,
        [string]$PropertyName,
        [int]$LineNumber
    )

    $line = $LineText

    if ($line -match '(?<command>[A-Za-z_][A-Za-z0-9_]*)Command') {
        return Convert-ToKeySegment ($matches.command -replace 'Command$', '')
    }

    if ($line -match 'x:Name\s*=\s*"(?<name>[A-Za-z_][A-Za-z0-9_]*)"') {
        return Convert-ToKeySegment $matches.name
    }

    if ($line -match '\b(StatusMessage|_statusMessage)\b') { return 'statusMessage' }
    if ($line -match '\bOutputDirectoryHintText\b') { return 'outputDirectoryHint' }
    if ($line -match '\bDragDropCaptionText\b') { return 'dragDropCaption' }
    if ($line -match '\bSupportedInputSummary\b') { return 'supportedInputSummary' }
    if ($line -match '\bheaderTitle\b') { return 'headerTitle' }
    if ($line -match '\bheaderDescription\b') { return 'headerDescription' }
    if ($line -match '\bDisplayName\b|\bdisplayName\b') { return 'displayName' }
    if ($line -match '\bDescription\b|\bdescription\b') { return 'description' }
    if ($line -match '\bthrow new\b') { return 'error' }
    if ($line -match '\bAddUiLog\b|_logger\.Log') { return 'log' }
    if ($line -match '\bReportProgress\b') { return 'progress' }
    if ($line -match '\bHintText\b') { return 'hint' }

    if (-not [string]::IsNullOrWhiteSpace($PropertyName)) {
        return Convert-ToKeySegment ($PropertyName -replace '^ToolTipService\.', '' -replace '^AutomationProperties\.', '')
    }

    return "line$LineNumber"
}

function Get-StateSegment {
    param(
        [string]$Category,
        [string]$LineText,
        [string]$PropertyName
    )

    switch ($Category) {
        'XamlLabel' { return 'label' }
        'XamlText' {
            if ($PropertyName -eq 'ToolTipService.ToolTip') { return 'tooltip' }
            if ($PropertyName -eq 'PlaceholderText') { return 'placeholder' }
            return 'text'
        }
        'ViewModelMessage' {
            if ($LineText -match '\bStatusMessage\b|_statusMessage') { return 'status' }
            if ($LineText -match '\bHintText\b') { return 'hint' }
            if ($LineText -match '\bCaption\b') { return 'caption' }
            return 'message'
        }
        'ServiceUserMessage' {
            if ($LineText -match '\bthrow new\b') { return 'error' }
            if ($LineText -match '\bReportProgress\b') { return 'progress' }
            if ($LineText -match '_logger\.Log') { return 'log' }
            return 'message'
        }
        'ConfigurationDisplayText' {
            if ($LineText -match '\bheaderTitle\b') { return 'title' }
            if ($LineText -match '\bheaderDescription\b') { return 'description' }
            if ($LineText -match '\bDisplayName\b|\bdisplayName\b') { return 'label' }
            return 'display'
        }
        default { return 'ignore' }
    }
}

function Get-SuggestedKey {
    param(
        [string]$ModuleName,
        [string]$RelativePath,
        [string]$LineText,
        [string]$PropertyName,
        [string]$Category,
        [int]$LineNumber
    )

    $moduleSegment = Get-ModuleKeySegment $ModuleName
    $areaSegment = Get-FileAreaSegment $RelativePath
    $elementSegment = Get-ElementSegment -LineText $LineText -PropertyName $PropertyName -LineNumber $LineNumber
    $stateSegment = Get-StateSegment -Category $Category -LineText $LineText -PropertyName $PropertyName

    return '{0}.{1}.{2}.{3}' -f $moduleSegment, $areaSegment, $elementSegment, $stateSegment
}

function New-InventoryEntry {
    param(
        [string]$RelativePath,
        [int]$LineNumber,
        [string]$SourceText,
        [string]$Category,
        [string]$PropertyName,
        [string]$LineText
    )

    $normalizedSource = $SourceText.Trim()
    $moduleName = Get-ModuleName $RelativePath

    [pscustomobject]@{
        Path         = $RelativePath
        LineNumber   = $LineNumber
        SourceText   = $normalizedSource
        Category     = $Category
        Priority     = Get-Priority -RelativePath $RelativePath -ModuleName $moduleName -Category $Category
        SuggestedKey = Get-SuggestedKey -ModuleName $moduleName -RelativePath $RelativePath -LineText $LineText -PropertyName $PropertyName -Category $Category -LineNumber $LineNumber
        Module       = $moduleName
        Status       = Get-Status $Category
    }
}

function Get-XamlEntries {
    param(
        [string]$RelativePath,
        [string]$FullPath
    )

    $entries = New-Object System.Collections.Generic.List[object]
    $lines = @(Get-Content -Path $FullPath -Encoding UTF8)
    $attributePattern = [regex]'(?<name>(?:[\w.]+:)?[\w.]+)\s*=\s*"(?<value>[^"]*[\u3400-\u4dbf\u4e00-\u9fff][^"]*)"'
    $textPattern = [regex]'>\s*(?<value>[^<]*[\u3400-\u4dbf\u4e00-\u9fff][^<]*)\s*<'
    $inComment = $false

    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        $trimmed = $line.Trim()

        if ($inComment) {
            if ($trimmed -match '-->') {
                $inComment = $false
            }

            continue
        }

        if ($trimmed.StartsWith('<!--', [System.StringComparison]::Ordinal)) {
            if ($trimmed -notmatch '-->') {
                $inComment = $true
            }

            continue
        }

        if (-not (Test-ContainsCjk $line)) {
            continue
        }

        foreach ($match in $attributePattern.Matches($line)) {
            $value = [string]$match.Groups['value'].Value
            if (-not (Test-ContainsCjk $value)) {
                continue
            }

            $propertyName = [string]$match.Groups['name'].Value
            $category = Get-XamlCategory $propertyName
            [void]$entries.Add((New-InventoryEntry -RelativePath $RelativePath -LineNumber ($index + 1) -SourceText $value -Category $category -PropertyName $propertyName -LineText $line))
        }

        foreach ($match in $textPattern.Matches($line)) {
            $value = [string]$match.Groups['value'].Value
            if (-not (Test-ContainsCjk $value)) {
                continue
            }

            if ($line -match '=\s*"[^"]*' + [regex]::Escape($value.Trim()) + '[^"]*"') {
                continue
            }

            [void]$entries.Add((New-InventoryEntry -RelativePath $RelativePath -LineNumber ($index + 1) -SourceText $value -Category 'XamlText' -PropertyName '' -LineText $line))
        }
    }

    return $entries
}

function Convert-StringLiteralValue {
    param([string]$Literal)

    $value = $Literal
    $isVerbatim = $value.StartsWith('@') -or $value.StartsWith('$@') -or $value.StartsWith('@$')

    if ($isVerbatim) {
        $value = $value -replace '^\$@"', '' -replace '^@\$"', '' -replace '^@"', ''
        $value = $value.Substring(0, $value.Length - 1)
        return $value.Replace('""', '"')
    }

    $value = $value -replace '^\$"', '' -replace '^"', ''
    $value = $value.Substring(0, $value.Length - 1)
    $value = $value -replace '\\\"', '"'
    $value = $value -replace '\\\\', '\'

    return $value
}

function Get-CodeEntries {
    param(
        [string]$RelativePath,
        [string]$FullPath
    )

    $entries = New-Object System.Collections.Generic.List[object]
    $lines = @(Get-Content -Path $FullPath -Encoding UTF8)
    $literalPattern = [regex]'(?<literal>\$@"(?:(?:"")|[^"])*"|@\$"(?:(?:"")|[^"])*"|@"(?:(?:"")|[^"])*"|\$"(?:\\.|[^"\\])*"|"(?:\\.|[^"\\])*")'

    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        $trimmed = $line.Trim()

        if ($trimmed.StartsWith('//', [System.StringComparison]::Ordinal) -or $trimmed.StartsWith('/*', [System.StringComparison]::Ordinal) -or $trimmed.StartsWith('*', [System.StringComparison]::Ordinal)) {
            continue
        }

        if (-not (Test-ContainsCjk $line)) {
            continue
        }

        foreach ($match in $literalPattern.Matches($line)) {
            $literal = [string]$match.Groups['literal'].Value
            $value = Convert-StringLiteralValue $literal

            if (-not (Test-ContainsCjk $value)) {
                continue
            }

            $category = Get-CodeCategory -RelativePath $RelativePath -LineText $line
            [void]$entries.Add((New-InventoryEntry -RelativePath $RelativePath -LineNumber ($index + 1) -SourceText $value -Category $category -PropertyName '' -LineText $line))
        }
    }

    return $entries
}

function Test-ShouldSkipFile {
    param(
        [string]$RepoRootPath,
        [System.IO.FileInfo]$FileInfo
    )

    $relativePath = Convert-ToRelativePath -RootPath $RepoRootPath -FullPath $FileInfo.FullName

    if ($relativePath -match '(^|/)(bin|obj|artifacts)(/|$)') {
        return $true
    }

    if ($relativePath -match '\.(g|g\.i|designer|Designer)\.cs$' -or $relativePath -match 'AssemblyInfo\.cs$' -or $relativePath -match 'GlobalUsings\.g\.cs$') {
        return $true
    }

    return $false
}

$targetDirectories = @('Views', 'ViewModels', 'Services', 'Core', 'Utils')
$files = foreach ($directory in $targetDirectories) {
    $path = Join-Path $RepoRoot $directory
    if (Test-Path $path) {
        Get-ChildItem -Path $path -Recurse -File -Include *.cs,*.xaml
    }
}

$inventory = New-Object System.Collections.Generic.List[object]

foreach ($file in $files | Sort-Object FullName -Unique) {
    if (Test-ShouldSkipFile -RepoRootPath $RepoRoot -FileInfo $file) {
        continue
    }

    $relativePath = Convert-ToRelativePath -RootPath $RepoRoot -FullPath $file.FullName
    $entries =
        if ($file.Extension -eq '.xaml') {
            Get-XamlEntries -RelativePath $relativePath -FullPath $file.FullName
        }
        else {
            Get-CodeEntries -RelativePath $relativePath -FullPath $file.FullName
        }

    foreach ($entry in $entries) {
        [void]$inventory.Add($entry)
    }
}

$priorityRank = @{
    P0 = 0
    P1 = 1
    P2 = 2
}

$sortedInventory = $inventory |
    Sort-Object `
        @{ Expression = { $priorityRank[$_.Priority] } }, `
        @{ Expression = { $_.Module } }, `
        @{ Expression = { $_.Path } }, `
        @{ Expression = { [int]$_.LineNumber } }, `
        @{ Expression = { $_.SuggestedKey } }

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path $outputDirectory)) {
    New-Item -Path $outputDirectory -ItemType Directory -Force | Out-Null
}

$sortedInventory |
    Select-Object Path, LineNumber, SourceText, Category, Priority, SuggestedKey, Module, Status |
    Export-Csv -Path $OutputPath -NoTypeInformation -Encoding UTF8

$summary = $sortedInventory |
    Group-Object Module, Priority |
    Sort-Object Count -Descending |
    Select-Object @{ Name = 'Module'; Expression = { $_.Group[0].Module } },
                  @{ Name = 'Priority'; Expression = { $_.Group[0].Priority } },
                  Count

Write-Host "Inventory exported to $OutputPath"
Write-Host ("Entries: {0}" -f $sortedInventory.Count)
$summary | Format-Table -AutoSize
