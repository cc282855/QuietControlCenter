[CmdletBinding()]
param(
    [string]$PackageRoot,
    [string]$ShortcutPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$expectedSource = '12E88F8F7AE6D4643AB85CC3548D9416BBA84A3A2E301998F813C35CC5D5DF81'
$expectedMaster = '1B9883F083E01AA6E3EF1FB1A021C14144E20E6F60A60E72CA62308E083AC122'
$expectedIcon = '655063EAEC64812156887850999CEC30D0961B0D8749DA64513180FE28EFE0C0'
$expectedSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$mainExecutableName = ([char]0x7384).ToString() + [char]0x540C + '.exe'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Hash([string]$Path, [string]$Expected) {
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "Brand asset is missing: $Path"
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    Assert-True ($actual -eq $Expected) "Brand asset hash mismatch: $Path expected=$Expected actual=$actual"
}

& python (Join-Path $repoRoot 'tools\build-brand-assets.py') --repo-root $repoRoot --check
Assert-True ($LASTEXITCODE -eq 0) 'Deterministic brand asset check failed.'

$source = Join-Path $repoRoot 'branding\source\xuanwu-porcelain-red-source.png'
$master = Join-Path $repoRoot 'branding\master\xuanwu-porcelain-red-transparent-1024.png'
$icon = Join-Path $repoRoot 'v2rayN\v2rayN\Resources\v2rayN.ico'
Assert-Hash $source $expectedSource
Assert-Hash $master $expectedMaster
Assert-Hash $icon $expectedIcon

Add-Type -AssemblyName System.Drawing
$masterImage = [System.Drawing.Bitmap]::FromFile($master)
try {
    Assert-True ($masterImage.Width -eq 1024 -and $masterImage.Height -eq 1024) 'Transparent master must be exactly 1024x1024.'
    foreach ($point in @(@(0,0), @(1023,0), @(0,1023), @(1023,1023))) {
        Assert-True ($masterImage.GetPixel($point[0], $point[1]).A -eq 0) 'Transparent master corners must have zero alpha.'
    }
} finally {
    $masterImage.Dispose()
}

$iconBytes = [IO.File]::ReadAllBytes($icon)
Assert-True ([BitConverter]::ToUInt16($iconBytes, 0) -eq 0) 'ICO reserved header is invalid.'
Assert-True ([BitConverter]::ToUInt16($iconBytes, 2) -eq 1) 'ICO type header is invalid.'
Assert-True ([BitConverter]::ToUInt16($iconBytes, 4) -eq $expectedSizes.Count) 'ICO must contain exactly nine frames.'
for ($index = 0; $index -lt $expectedSizes.Count; $index++) {
    $entry = 6 + 16 * $index
    $width = $iconBytes[$entry]; if ($width -eq 0) { $width = 256 }
    $height = $iconBytes[$entry + 1]; if ($height -eq 0) { $height = 256 }
    $bits = [BitConverter]::ToUInt16($iconBytes, $entry + 6)
    $offset = [BitConverter]::ToUInt32($iconBytes, $entry + 12)
    Assert-True ($width -eq $expectedSizes[$index] -and $height -eq $expectedSizes[$index]) "Unexpected ICO frame at index $index."
    Assert-True ($bits -eq 32) "ICO frame $width must be 32-bit."
    Assert-True ($iconBytes[$offset] -eq 0x89 -and $iconBytes[$offset + 1] -eq 0x50) "ICO frame $width must use PNG encoding."
}

$sameIconPaths = @(
    'v2rayN\v2rayN\Resources\NotifyIcon1.ico',
    'v2rayN\v2rayN\Resources\NotifyIcon2.ico',
    'v2rayN\v2rayN\Resources\NotifyIcon3.ico',
    'v2rayN\v2rayN\Resources\NotifyIcon4.ico',
    'v2rayN\v2rayN.Desktop\Assets\v2rayN.ico',
    'v2rayN\v2rayN.Desktop\Assets\NotifyIcon1.ico',
    'v2rayN\v2rayN.Desktop\Assets\NotifyIcon2.ico',
    'v2rayN\v2rayN.Desktop\Assets\NotifyIcon3.ico',
    'v2rayN\v2rayN.Desktop\Assets\NotifyIcon4.ico',
    'v2rayN\AmazTool\Resources\v2rayN.ico'
)
foreach ($relative in $sameIconPaths) { Assert-Hash (Join-Path $repoRoot $relative) $expectedIcon }

$amazProject = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'v2rayN\AmazTool\AmazTool.csproj')
Assert-True ($amazProject.Contains('<ApplicationIcon>Resources\v2rayN.ico</ApplicationIcon>')) 'AmazTool does not embed the brand application icon.'

if ($PackageRoot) {
    $packageResolved = (Resolve-Path -LiteralPath $PackageRoot).Path
    $mainExe = Join-Path $packageResolved $mainExecutableName
    $helperExe = Join-Path $packageResolved 'AmazTool.exe'
    Assert-True (Test-Path -LiteralPath $mainExe -PathType Leaf) "Packaged main executable is missing: $mainExe"
    Assert-True (Test-Path -LiteralPath $helperExe -PathType Leaf) "Packaged updater executable is missing: $helperExe"
    foreach ($executable in @($mainExe, $helperExe)) {
        $associated = [System.Drawing.Icon]::ExtractAssociatedIcon($executable)
        try {
            Assert-True ($null -ne $associated -and $associated.Width -ge 16 -and $associated.Height -ge 16) "PE associated icon is missing: $executable"
        } finally {
            if ($null -ne $associated) { $associated.Dispose() }
        }
    }
}

if ($ShortcutPath) {
    $shortcutResolved = (Resolve-Path -LiteralPath $ShortcutPath).Path
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutResolved)
    try {
        Assert-True ($shortcut.TargetPath.EndsWith($mainExecutableName, [StringComparison]::OrdinalIgnoreCase)) 'Evidence shortcut target must use the branded executable.'
        Assert-True ($shortcut.IconLocation.EndsWith("$mainExecutableName,0", [StringComparison]::OrdinalIgnoreCase)) 'Evidence shortcut must use executable icon index zero.'
    } finally {
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
    }
}

"brand-assets=PASS; icoSha256=$expectedIcon; frames=$($expectedSizes -join ',')"
