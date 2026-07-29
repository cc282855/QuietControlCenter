param([Parameter(Mandatory)] [string]$Executable, [Parameter(Mandatory)] [string]$Output, [int]$Width=1487, [int]$Height=1058, [switch]$ReloadCore)
$ErrorActionPreference = 'Stop'
$baselineCorePids = @(Get-Process sing-box,mihomo -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
$arguments = "--qcc-qa-capture `"$([IO.Path]::GetFullPath($Output))`" $Width $Height"
if ($ReloadCore) { $arguments += ' --qcc-qa-reload' }
$process = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path $Executable) -ArgumentList $arguments -PassThru
try {
    if (-not $process.WaitForExit(30000)) { throw 'QA capture timed out.' }
    if (-not (Test-Path -LiteralPath $Output)) { throw 'QA capture was not produced.' }
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
    Start-Sleep -Milliseconds 500
    @(Get-Process sing-box,mihomo -ErrorAction SilentlyContinue | Where-Object { $baselineCorePids -notcontains $_.Id }) | ForEach-Object { Stop-Process -Id $_.Id -Force }
    "FIXTURE_PID=$($process.Id) EXIT=$($process.ExitCode)"
}
