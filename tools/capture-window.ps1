param(
    [Parameter(Mandatory)] [string]$ExePath,
    [Parameter(Mandatory)] [string]$OutputPath,
    [int]$LogicalWidth = 1487,
    [int]$LogicalHeight = 1058,
    [double]$DpiScale = 1.5,
    [int]$StartupSeconds = 8,
    [switch]$SendF5
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class QccCaptureNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int command);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@

[QccCaptureNative]::SetProcessDPIAware() | Out-Null
$resolvedExe = (Resolve-Path $ExePath).Path
$workingDirectory = Split-Path $resolvedExe -Parent
$process = [System.Diagnostics.Process]::Start([System.Diagnostics.ProcessStartInfo]@{
    FileName = $resolvedExe
    WorkingDirectory = $workingDirectory
    UseShellExecute = $true
})

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        $handle = $process.MainWindowHandle
    } while ($handle -eq [IntPtr]::Zero -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline)
    if ($handle -eq [IntPtr]::Zero) { throw 'Fixture window did not become ready.' }

    $screenPhysicalWidth = [Math]::Round([System.Windows.SystemParameters]::PrimaryScreenWidth * $DpiScale)
    $physicalWidth = [Math]::Min([Math]::Round($LogicalWidth * $DpiScale), $screenPhysicalWidth)
    $physicalHeight = [Math]::Round($LogicalHeight * $DpiScale)
    [QccCaptureNative]::ShowWindow($handle, 9) | Out-Null
    [QccCaptureNative]::SetWindowPos($handle, [IntPtr]::Zero, 0, 0, $physicalWidth, $physicalHeight, 0x0040) | Out-Null
    if ($SendF5) {
        Start-Sleep -Seconds 2
        [QccCaptureNative]::SetForegroundWindow($handle) | Out-Null
        [System.Windows.Forms.SendKeys]::SendWait('{F5}')
    }
    Start-Sleep -Seconds $StartupSeconds

    $rect = New-Object QccCaptureNative+RECT
    [QccCaptureNative]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $actualWidth = $rect.Right - $rect.Left
    $actualHeight = $rect.Bottom - $rect.Top
    $source = New-Object System.Drawing.Bitmap $actualWidth, $actualHeight
    $graphics = [System.Drawing.Graphics]::FromImage($source)
    $dc = $graphics.GetHdc()
    try { [QccCaptureNative]::PrintWindow($handle, $dc, 2) | Out-Null }
    finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }

    $target = New-Object System.Drawing.Bitmap $LogicalWidth, $LogicalHeight
    $targetGraphics = [System.Drawing.Graphics]::FromImage($target)
    $targetGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $targetGraphics.DrawImage($source, 0, 0, $LogicalWidth, $LogicalHeight)
    $targetGraphics.Dispose()
    $source.Dispose()
    $outputDirectory = Split-Path $OutputPath -Parent
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    $target.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $target.Dispose()
    "CAPTURED pid=$($process.Id) actual=${actualWidth}x${actualHeight} normalized=${LogicalWidth}x${LogicalHeight} dpiScale=$DpiScale"
}
finally {
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessId -ne $process.Id -and $_.ExecutablePath -and $_.ExecutablePath.StartsWith($workingDirectory, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
}
