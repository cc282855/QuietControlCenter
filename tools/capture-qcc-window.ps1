param([Parameter(Mandatory)] [string]$Executable, [Parameter(Mandatory)] [string]$Output, [int]$Width=1487, [int]$Height=1058, [switch]$ReloadCore)
$ErrorActionPreference = 'Stop'

if ($ReloadCore) {
    throw 'ReloadCore is forbidden during isolated UI capture.'
}

$coreProcessNames = @('sing-box', 'mihomo', 'xray')
function Get-CoreProcessSnapshot {
    @(
        Get-Process -Name $coreProcessNames -ErrorAction SilentlyContinue |
            Sort-Object ProcessName, Id |
            ForEach-Object { "$($_.ProcessName):$($_.Id)" }
    )
}

$outputPath = [IO.Path]::GetFullPath($Output)
$errorPath = $outputPath + '.error.txt'
if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Force
}
if (Test-Path -LiteralPath $errorPath) {
    Remove-Item -LiteralPath $errorPath -Force
}

$captureStartUtc = [DateTime]::UtcNow
$captureFreshnessFloorUtc = $captureStartUtc.AddSeconds(-2)
$baselineCoreProcesses = @(Get-CoreProcessSnapshot)
$arguments = "--qcc-qa-capture `"$outputPath`" $Width $Height"
$process = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path $Executable) -ArgumentList $arguments -PassThru
$timedOut = $false
try {
    $timedOut = -not $process.WaitForExit(30000)
    if ($timedOut) { throw 'QA capture timed out.' }
    # App.OnExit terminates the WPF process with Process.Kill(), which reports -1
    # after a successful capture. Freshness/error-file checks remain authoritative.
    if ($process.ExitCode -notin @(0, -1)) { throw "QA capture fixture exited with code $($process.ExitCode)." }
    if (Test-Path -LiteralPath $errorPath -PathType Leaf) { throw 'QA capture reported an error.' }
    if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) { throw 'QA capture was not produced.' }

    $outputFile = Get-Item -LiteralPath $outputPath
    if ($outputFile.Length -le 0) { throw 'QA capture output is empty.' }
    if ($outputFile.LastWriteTimeUtc -lt $captureFreshnessFloorUtc) { throw 'QA capture output is stale.' }
} finally {
    if ($timedOut -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }

    Start-Sleep -Milliseconds 500
    $finalCoreProcesses = @(Get-CoreProcessSnapshot)
    $coreProcessDrift = @(Compare-Object -ReferenceObject $baselineCoreProcesses -DifferenceObject $finalCoreProcesses)
    if ($coreProcessDrift.Count -ne 0) {
        throw "Core process PID drift detected; no core processes were stopped: $($coreProcessDrift -join ', ')"
    }

    "FIXTURE_PID=$($process.Id) EXIT=$($process.ExitCode)"
}
