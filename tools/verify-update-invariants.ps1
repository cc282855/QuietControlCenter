$ErrorActionPreference = 'Stop'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Parse-Version([string]$Value) {
    $match = [regex]::Match($Value, '\d+(?:\.\d+){1,3}')
    if (-not $match.Success) { return $null }
    return [version]$match.Value
}

$root = Split-Path $PSScriptRoot -Parent
$checkView = Get-Content -Raw (Join-Path $root 'v2rayN/v2rayN/Views/CheckUpdateView.xaml.cs')
$mainView = Get-Content -Raw (Join-Path $root 'v2rayN/v2rayN/Views/MainWindow.xaml.cs')
$service = Get-Content -Raw (Join-Path $root 'v2rayN/v2rayN/Services/QuietUpdateService.cs')

Assert-True ($checkView -match 'CoreType == ECoreType\.v2rayN') 'GUI update item is not identified.'
Assert-True ($checkView -match 'item\.IsSelected = false') 'GUI update item is not forced deselected.'
Assert-True ($checkView -match 'CheckUpdateModels\.Remove\(item\)') 'GUI update item is not removed.'
Assert-True ($mainView -match 'RemoveOfficialGuiUpdate') 'Main update entry does not enforce isolation.'
Assert-True ($service -notmatch 'CheckUpdateGuiN|CheckUpdateN|AmazTool|CoreStop') 'Detection service references an install/replacement path.'
Assert-True ($service -match 'Timeout = TimeSpan\.FromSeconds\(5\)') 'Short network timeout is missing.'
Assert-True ($service -match 'LastCheckedUtc') 'Persistent 24-hour state is missing.'

$now = [DateTimeOffset]::UtcNow
Assert-True (($now - $now.AddHours(-23)) -lt [TimeSpan]::FromHours(24)) '24-hour negative boundary failed.'
Assert-True (($now - $now.AddHours(-24)) -ge [TimeSpan]::FromHours(24)) '24-hour positive boundary failed.'
Assert-True ((Parse-Version 'v7.24.4-rc1') -gt (Parse-Version 'v2rayN - V7.24.3 - X64')) 'Robust version comparison failed.'
Assert-True ((Parse-Version 'not-a-version') -eq $null) 'Invalid version was accepted.'

$manifest = Get-Content -Raw (Join-Path $root 'tools/quiet-update-manifest.example.json') | ConvertFrom-Json
Assert-True ($manifest.platform -eq 'win-x64') 'Manifest platform is not win-x64.'
Assert-True ([uri]$manifest.assetUrl).Scheme.Equals('https') 'Manifest asset URL is not HTTPS.'
Assert-True ($manifest.sha256 -match '^[a-fA-F0-9]{64}$') 'Manifest SHA256 is invalid.'
Assert-True ([uri]$manifest.provenanceUrl).Scheme.Equals('https') 'Manifest provenance is missing or insecure.'

'PASS: update isolation, 24-hour gate, version parsing, failure boundary, and manifest contract.'
