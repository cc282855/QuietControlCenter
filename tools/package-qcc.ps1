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

$runtimeNames = @('guiConfigs', 'guiLogs', 'logs', 'binConfigs', 'guiTemps')
$sensitiveExtensions = @(
    '.db', '.sqlite', '.sqlite3', '.log',
    '.wal', '.shm', '.journal',
    '.db-wal', '.db-shm', '.db-journal',
    '.sqlite-wal', '.sqlite-shm', '.sqlite-journal',
    '.key', '.pem', '.pfx', '.p12', '.jks', '.keystore', '.pk8', '.pkcs8', '.ppk', '.snk', '.secret'
)
$plausibleTextExtensions = @('.json', '.txt', '.yaml', '.yml', '.toml', '.csv', '.url', '.conf', '.config', '.ini', '.xml', '.md')
$subscriptionSchemePattern = '(?im)(?:vmess|vless|ss|ssr|trojan|hysteria|hysteria2(?:\+realm(?:\+http)?)?|hy2|tuic|socks|socks4|socks5|wireguard|anytls|naive|naive\+https|naive\+quic)://'
$privateKeyTextPattern = '(?im)(?:-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----|PuTTY-User-Key-File-)'
$sensitiveBaseNamePattern = '(?i)^(?:id_(?:rsa|dsa|ecdsa|ed25519)|private(?:[-_.]?key)?|credentials?|secrets?)(?:\..*)?$'

function Assert-NoSensitivePayload([string]$Root) {
    $sensitivePayloads = @(
        Get-ChildItem -LiteralPath $Root -Recurse -Force |
            Where-Object {
                ($_.PSIsContainer -and $runtimeNames -contains $_.Name) -or
                (-not $_.PSIsContainer -and (
                    $sensitiveExtensions -contains $_.Extension.ToLowerInvariant() -or
                    $_.Name -match '(?i)\.(?:db|sqlite|sqlite3)-(?:wal|shm|journal)$' -or
                    $_.Name -match $sensitiveBaseNamePattern
                ))
            }
    )
    if ($sensitivePayloads.Count -ne 0) {
        throw "Sensitive runtime state or key material is forbidden in the immutable package. Count=$($sensitivePayloads.Count)"
    }
}

function Assert-NoSubscriptionUris([string]$Root) {
    $plausibleTextPayloads = @(
        Get-ChildItem -LiteralPath $Root -Recurse -File -Force |
            Where-Object {
                $_.Name -eq 'qcc-package.json' -or
                $plausibleTextExtensions -contains $_.Extension.ToLowerInvariant() -or
                [string]::IsNullOrEmpty($_.Extension)
            }
    )
    foreach ($textPayload in $plausibleTextPayloads) {
        $bytes = [IO.File]::ReadAllBytes($textPayload.FullName)
        $byteCount = [Math]::Min($bytes.Length, 4096)
        $text = $null
        if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xff -and $bytes[1] -eq 0xfe) {
            $text = [Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2)
        } elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xfe -and $bytes[1] -eq 0xff) {
            $text = [Text.Encoding]::BigEndianUnicode.GetString($bytes, 2, $bytes.Length - 2)
        } elseif ($bytes.Length -ge 3 -and $bytes[0] -eq 0xef -and $bytes[1] -eq 0xbb -and $bytes[2] -eq 0xbf) {
            $text = [Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
        } elseif ($byteCount -ge 8) {
            $pairCount = [Math]::Floor($byteCount / 2)
            $evenNullCount = 0
            $oddNullCount = 0
            for ($index = 0; $index -lt ($pairCount * 2); $index += 2) {
                if ($bytes[$index] -eq 0) { $evenNullCount++ }
                if ($bytes[$index + 1] -eq 0) { $oddNullCount++ }
            }
            if ($oddNullCount * 4 -ge $pairCount * 3 -and $evenNullCount * 10 -le $pairCount) {
                $text = [Text.Encoding]::Unicode.GetString($bytes)
            } elseif ($evenNullCount * 4 -ge $pairCount * 3 -and $oddNullCount * 10 -le $pairCount) {
                $text = [Text.Encoding]::BigEndianUnicode.GetString($bytes)
            }
        }

        $controlByteCount = 0
        if ($null -eq $text) {
            for ($index = 0; $index -lt $byteCount; $index++) {
                $value = $bytes[$index]
                if ($value -eq 0) {
                    $controlByteCount = $byteCount
                    break
                }
                if ($value -lt 9 -or ($value -gt 13 -and $value -lt 32)) {
                    $controlByteCount++
                }
            }
            if ($byteCount -gt 0 -and $controlByteCount * 20 -ge $byteCount) {
                continue
            }
            $text = [Text.Encoding]::UTF8.GetString($bytes)
        }

        if ($text -match $privateKeyTextPattern) {
            throw 'Private key text is forbidden in the immutable package.'
        }
        if ($text -match $subscriptionSchemePattern) {
            throw 'Subscription URI is forbidden in the immutable package.'
        }
    }
}

function Assert-NoUnexpectedTextPayloads([string]$Root) {
    $unexpectedTextPayloads = @(
        Get-ChildItem -LiteralPath $Root -Recurse -File -Force |
            Where-Object {
                $_.Name -ne 'qcc-package.json' -and (
                    $plausibleTextExtensions -contains $_.Extension.ToLowerInvariant() -or
                    [string]::IsNullOrEmpty($_.Extension)
                )
            }
    )
    if ($unexpectedTextPayloads.Count -ne 0) {
        throw "Unexpected text payload is forbidden in the immutable package. Count=$($unexpectedTextPayloads.Count)"
    }
}

Assert-NoSensitivePayload $artifactRootResolved
Assert-NoSubscriptionUris $artifactRootResolved

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
Assert-NoUnexpectedTextPayloads $artifactRootResolved
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

Assert-NoSensitivePayload $artifactRootResolved
Assert-NoSubscriptionUris $artifactRootResolved
Assert-NoUnexpectedTextPayloads $artifactRootResolved
$markerText = Get-Content -Raw (Join-Path $Artifact 'qcc-package.json')
if ($markerText -match [regex]::Escape($repoRoot)) { throw 'Local paths leaked into qcc-package.json.' }
