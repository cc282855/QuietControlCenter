# Quiet Control Center 7.24.3

This repository pins the upstream v2rayN source at 7.24.3 and the required
`v2rayN/GlobalHotKeys` submodule at commit
`162d401dfe0140b41d1fa349b9aadb4060e739b1` from
<https://github.com/2dust/GlobalHotKeys>.

Clone reproducibly with:

```powershell
git clone --recurse-submodules <quiet-control-center-repository>
git -C v2rayN/GlobalHotKeys checkout 162d401dfe0140b41d1fa349b9aadb4060e739b1
```

Build and verify with .NET SDK 10:

```powershell
dotnet restore v2rayN/v2rayN.slnx
dotnet build v2rayN/v2rayN/v2rayN.csproj -c Release --no-restore
dotnet test v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj -c Release --no-restore
dotnet publish v2rayN/v2rayN/v2rayN.csproj -c Release -r win-x64 --self-contained true -o artifacts/qcc-win-x64
```

The in-app update panel intentionally updates only proxy cores and Geo data.
It removes the upstream `ECoreType.v2rayN` GUI replacement item before the
panel is shown. Update the customized client only by installing a complete
Quiet Control Center package produced from this repository; never mix its UI
files with an official v2rayN binary update.

## Daily upstream detection and custom channel

On startup the client schedules an anonymous, non-blocking query to the
official GitHub Releases API. The check uses a five-second timeout and records
`lastCheckedUtc` plus `latestSeenTag` in
`%LOCALAPPDATA%\QuietControlCenter\update-state.json`; it runs at most once per
24 hours. Offline, malformed, rate-limited, and unwritable-state failures are
silent and cannot block proxy startup. An official release is detection-only:
the application displays that a compatible Quiet Control Center package is
required and never downloads or replaces the official GUI.

An optional custom channel can be enabled by creating
`%LOCALAPPDATA%\QuietControlCenter\update-channel.json`:

```json
{ "manifestUrl": "https://github.com/OWNER/REPOSITORY/releases/latest/download/quiet-update-manifest.json" }
```

The manifest contract is shown in `tools/quiet-update-manifest.example.json`.
It requires an app version, `win-x64`, an HTTPS full-package URL, a 64-character
SHA-256, and an HTTPS provenance URL. Signature metadata is reserved by the
contract. The current client remains notification-only even for a valid custom
manifest; it never auto-installs unsigned or unverified bytes. With no channel
file, official detection plus a clear manual-package notice is the intended
behavior.

## Fork automation (not active until configured)

`.github/workflows/upstream-draft.yml` checks upstream daily or accepts a
manual tag. In a user-owned fork it derives the maintained Quiet UI patch layer
from `QUIET_UI_BASE_TAG` (default `7.24.3`), checks out the exact new upstream
tag, runs `git apply --check` and fails closed on conflicts, then restores,
builds, tests, publishes, hashes, and creates a **draft** custom release.
Publishing still requires human visual/provenance review. This repository has
no supplied fork or credentials, so the workflow is an automation artifact,
not a claim that a release channel is currently live.

Run the deterministic update-policy checks locally with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-update-invariants.ps1
```
