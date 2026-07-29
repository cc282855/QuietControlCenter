$ErrorActionPreference = 'Stop'
function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }

$root = Split-Path $PSScriptRoot -Parent
$checkView = Get-Content -Raw (Join-Path $root 'v2rayN/v2rayN/Views/CheckUpdateView.xaml.cs')
$mainView = Get-Content -Raw (Join-Path $root 'v2rayN/v2rayN/Views/MainWindow.xaml.cs')
$service = Get-Content -Raw (Join-Path $root 'v2rayN/v2rayN/Services/QuietUpdateService.cs')
$helper = Get-Content -Raw (Join-Path $root 'v2rayN/AmazTool/UpgradeApp.cs')
$program = Get-Content -Raw (Join-Path $root 'v2rayN/AmazTool/Program.cs')
$workflow = Get-Content -Raw (Join-Path $root '.github/workflows/upstream-draft.yml')

Assert-True ($checkView -match 'CoreType == ECoreType\.v2rayN') 'Official GUI item is not identified.'
Assert-True ($checkView -match 'CheckUpdateModels\.Remove\(item\)') 'Official GUI item is not removed.'
Assert-True ($mainView -match 'RemoveOfficialGuiUpdate') 'Update panel isolation is missing.'
Assert-True ($mainView -match 'QuietUpdateScheduler') 'Lifecycle scheduler is not wired.'
Assert-True ($service -match 'TimeSpan\.FromHours\(24\)') '24-hour gate is missing.'
Assert-True ($service -match 'PublicKeyPem' -and $service -match 'VerifyData') 'Pinned signature verification is missing.'
Assert-True ($service -match 'SHA256\.HashData' -and $service -match 'qcc-package\.json') 'Actual package/payload hashes are not checked.'
Assert-True ($service -match 'string\.IsNullOrEmpty\(uri\.UserInfo\)') 'Credential-bearing URLs are not rejected.'
Assert-True ($program -match 'Legacy ZIP arguments are intentionally rejected') 'Legacy helper mode is not fail-closed.'
Assert-True ($helper -match 'Path traversal/collision' -and $helper -match 'IsDeviceName' -and $helper -match 'MaxExpandedBytes') 'Bounded path-safe extraction is incomplete.'
Assert-True ($helper -match 'Directory\.Move\(install, backup\)' -and $helper -match 'rollback completed') 'Transactional rollback is missing.'
Assert-True ($helper -notmatch 'new\[\] \{ "guiConfigs", "logs", "bin" \}') 'Immutable bin payload is incorrectly preserved old-wins.'
Assert-True ($helper -match 'instruction\.sha256' -and $helper -match 'OriginExecutableSha256' -and $helper -match 'Guid\.TryParseExact') 'Instruction/work-root/origin binding is incomplete.'
Assert-True ($helper -match 'deferred backup cleanup' -and $helper -match 'Reparse point in mutable data') 'Commit cleanup or reparse rejection is missing.'
Assert-True ($workflow -match 'QCC_SIGNING_PRIVATE_KEY_PEM' -and $workflow -match '\-\-draft' -and $workflow -match '\-\-clobber') 'Draft/signing/rerun workflow contract is missing.'
Assert-True ($workflow -match 'actions/checkout@[0-9a-f]{40}' -and $workflow -match 'actions/setup-dotnet@[0-9a-f]{40}') 'Third-party actions are not pinned to full SHAs.'

$manifest = Get-Content -Raw (Join-Path $root 'tools/quiet-update-manifest.example.json') | ConvertFrom-Json
Assert-True ($manifest.schema -eq 1 -and $manifest.product -eq 'QuietControlCenter') 'Manifest identity is invalid.'
Assert-True ($manifest.sha256 -match '^[a-fA-F0-9]{64}$') 'Manifest SHA-256 is invalid.'
Assert-True (([uri]$manifest.assetUrl).Scheme -eq 'https' -and ([uri]$manifest.assetUrl).UserInfo -eq '') 'Asset URL is unsafe.'

'PASS: official detection-only, daily lifecycle, dormant signed channel, streamed hashes, path-safe transaction, rollback, and draft-only workflow.'
