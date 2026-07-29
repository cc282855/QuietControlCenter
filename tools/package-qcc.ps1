param(
    [string]$Artifact = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/qcc-win-x64'),
    [string]$Helper = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/qcc-helper/AmazTool.exe'),
    [string]$Version = '7.24.3'
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path.TrimEnd('\')
$artifactRootResolved = (Resolve-Path $Artifact).Path.TrimEnd('\')
$expectedArtifact = (Join-Path $repoRoot 'artifacts\qcc-win-x64').TrimEnd('\')
if (-not $artifactRootResolved.Equals($expectedArtifact, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to clean an unexpected artifact path.' }

# Publish output is immutable. Runtime state must never enter an update package.
$allowedDirectories = @('bin')
Get-ChildItem -LiteralPath $artifactRootResolved -Directory | ForEach-Object {
    if ($allowedDirectories -notcontains $_.Name) { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
}
Get-ChildItem -LiteralPath $artifactRootResolved -File | ForEach-Object {
    if ($_.Name -ne 'qcc-package.json' -and $_.Extension -notin @('.exe', '.dll')) { Remove-Item -LiteralPath $_.FullName -Force }
}
Copy-Item -LiteralPath $Helper -Destination (Join-Path $Artifact 'AmazTool.exe') -Force
$requiredRuntimeFiles = @('bin/xray/xray.exe', 'bin/sing_box/sing-box.exe', 'bin/mihomo/mihomo.exe')
foreach ($relative in $requiredRuntimeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $Artifact $relative))) { throw "Required runtime payload is missing: $relative" }
}
$files = [ordered]@{}
$artifactRoot = (Resolve-Path $Artifact).Path.TrimEnd('\') + '\'
Get-ChildItem -LiteralPath $Artifact -File -Recurse |
    Where-Object Name -ne 'qcc-package.json' |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($artifactRoot.Length).Replace('\','/')
        $files[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
[ordered]@{ product='QuietControlCenter'; platform='win-x64'; version=$Version; files=$files } |
    ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $Artifact 'qcc-package.json')

$runtimeNames = @('guiConfigs','guiLogs','logs','binConfigs','guiTemps')
if (Get-ChildItem -LiteralPath $Artifact -Recurse -Force | Where-Object { $runtimeNames -contains $_.Name -or $_.Extension -in @('.db','.log') }) {
    throw 'Runtime state leaked into the immutable package.'
}
$markerText = Get-Content -Raw (Join-Path $Artifact 'qcc-package.json')
if ($markerText -match [regex]::Escape($repoRoot)) { throw 'Local paths leaked into qcc-package.json.' }
