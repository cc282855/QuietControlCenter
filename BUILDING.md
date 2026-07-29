# Quiet Control Center 7.24.3

The customized UI is a maintained patch layer over upstream v2rayN. The official GUI is detection-only forever: the built-in update panel contains only core and Geo updates. A complete Quiet Control Center package is the only allowed GUI update.

## Daily lifecycle

The single scheduler starts when the main window loads, checks immediately if the persisted 24-hour gate is due, and continues every 24 hours while the app is tray-resident. State is stored atomically at `%LOCALAPPDATA%\QuietControlCenter\update-state.json`. Corrupt state and implausible future timestamps recover as due. Network, JSON, permission, and timeout failures are silent and never block proxy startup. Concurrent checks coalesce.

The official API `https://api.github.com/repos/2dust/v2rayN/releases/latest` only produces a notice. Official archives are never downloaded or passed to the updater.

## Enable the custom signed channel

The channel is dormant unless every field below exists in `%LOCALAPPDATA%\QuietControlCenter\update-channel.json`:

```json
{
  "manifestUrl": "https://github.com/OWNER/REPOSITORY/releases/latest/download/quiet-update-manifest.json",
  "publicKeyPem": "-----BEGIN PUBLIC KEY-----\n...P-256 PUBLIC KEY ONLY...\n-----END PUBLIC KEY-----",
  "expectedOwner": "OWNER",
  "expectedRepository": "REPOSITORY"
}
```

Never place a private key in the client or repository. Configure the matching P-256 private PEM only as the GitHub Actions secret `QCC_SIGNING_PRIVATE_KEY_PEM`. The workflow remains draft-only. Without that secret it emits an unsigned draft manifest, which every client rejects. A human must review the UI screenshots, provenance URL, hashes, and draft before publishing.

The signed canonical bytes are UTF-8 lines in this exact order, ending with a newline: `schema`, `product`, `appVersion`, `platform`, `assetUrl`, lowercase `sha256`, `provenanceUrl`. URLs must be HTTPS, contain no credentials, and belong to the pinned owner/repository. The client streams the archive with a 512 MiB cap, checks the actual SHA-256, validates `qcc-package.json`, and rehashes every marked payload file before invoking the external helper.

The helper accepts only `AmazTool qcc-upgrade <absolute-instruction.json> <inherited-ready-pipe>`. Legacy arbitrary ZIP invocation exits nonzero. The inherited anonymous pipe is a one-shot ready handshake bound to a random token, helper PID, and the exact originating PID, start time, executable path, and executable hash. The instruction, hash sidecar, package, and acknowledgement must use exact filenames inside `%TEMP%\QuietControlCenter\<random-guid>`. The helper acquires and validates the originating process before package work, performs bounded path-safe extraction on the installation volume, preserves only explicit mutable directories (`guiConfigs`, `guiLogs`, `logs`, and `binConfigs`), and rehashes the fully staged immutable tree before signaling ready. Only after the client validates ready and exits may the helper replace directories. Signed `bin` executables are always new-package-wins. Replacement is transactional and rolls back unless the new client emits the one-time startup acknowledgement. That acknowledgement commits the transaction; a locked backup is then left for deferred cleanup and can never roll back a running acknowledged version. Any pre-handoff failure keeps the current GUI running, terminates the exact helper process, and removes the generated work root; helper failures also clean staging.

`tools/package-qcc.ps1` removes runtime databases, logs, temporary directories, and generated configuration before producing `qcc-package.json`. The marker contains only relative immutable payload paths and hashes.

## Reproducible build

The required `v2rayN/GlobalHotKeys` submodule is pinned at `162d401dfe0140b41d1fa349b9aadb4060e739b1`.

```powershell
dotnet restore v2rayN/v2rayN.slnx
dotnet build v2rayN/v2rayN/v2rayN.csproj -c Release --no-restore
dotnet test v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj -c Release --no-restore
dotnet test v2rayN/v2rayN.Tests/v2rayN.Tests.csproj -c Release --no-restore
dotnet publish v2rayN/v2rayN/v2rayN.csproj -c Release -r win-x64 --self-contained true -o artifacts/qcc-win-x64
dotnet publish v2rayN/AmazTool/AmazTool.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/qcc-helper
```

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-update-invariants.ps1` before packaging.
