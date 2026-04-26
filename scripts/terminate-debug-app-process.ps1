param(
    [Parameter(Mandatory = $true)]
    [string]$ProcessName,

    [string]$ExecutablePath
)

$processImageName = [System.IO.Path]::GetFileNameWithoutExtension($ProcessName)
if ([string]::IsNullOrWhiteSpace($processImageName)) {
    throw "ProcessName must resolve to a valid executable name."
}

$normalizedExecutablePath = $null
if (-not [string]::IsNullOrWhiteSpace($ExecutablePath)) {
    try {
        $normalizedExecutablePath = [System.IO.Path]::GetFullPath($ExecutablePath)
    }
    catch {
        $normalizedExecutablePath = $null
    }
}

$runningProcesses = Get-Process -Name $processImageName -ErrorAction SilentlyContinue
if ($null -eq $runningProcesses) {
    exit 0
}

foreach ($process in @($runningProcesses)) {
    $shouldStop = $true

    if ($normalizedExecutablePath) {
        $candidatePath = $null

        try {
            $candidatePath = $process.MainModule.FileName
        }
        catch {
            $candidatePath = $null
        }

        if ([string]::IsNullOrWhiteSpace($candidatePath)) {
            continue
        }

        try {
            $normalizedCandidatePath = [System.IO.Path]::GetFullPath($candidatePath)
        }
        catch {
            continue
        }

        $shouldStop = [string]::Equals(
            $normalizedCandidatePath,
            $normalizedExecutablePath,
            [System.StringComparison]::OrdinalIgnoreCase)
    }

    if (-not $shouldStop) {
        continue
    }

    Stop-Process -Id $process.Id -Force -ErrorAction Stop
    Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue
    Write-Host "Stopped debug app process $($process.Id): $($process.ProcessName)"
}
