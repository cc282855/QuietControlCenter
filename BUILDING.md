# Quiet Control Center 7.24.3

The customized UI is a maintained patch layer over upstream v2rayN. The official GUI is detection-only forever: the built-in update panel contains only core and Geo updates. A complete Quiet Control Center package is the only allowed GUI update.

## Daily lifecycle

The single scheduler starts when the main window loads, checks immediately if the persisted 24-hour gate is due, and continues every 24 hours while the app is tray-resident. State is stored atomically at `%LOCALAPPDATA%\QuietControlCenter\update-state.json`. Corrupt state and implausible future timestamps recover as due. Network, JSON, permission, and timeout failures are silent and never block proxy startup. Concurrent checks coalesce.

The official API `https://api.github.com/repos/2dust/v2rayN/releases/latest` only produces a notice. Official archives are never downloaded or passed to the updater.

## Production signed channel

The shipped client is pinned to the production channel below, so no manual setup is required:

```json
{
  "manifestUrl": "https://github.com/cc282855/v2rayN/releases/latest/download/quiet-update-manifest.json",
  "expectedOwner": "cc282855",
  "expectedRepository": "v2rayN"
}
```

The matching P-256 public key is embedded in the client. `%LOCALAPPDATA%\QuietControlCenter\update-channel.json` remains an optional explicit override; a complete override can redirect or disable the channel for testing.

The private key exists only as the GitHub Actions secret `QCC_SIGNING_PRIVATE_KEY_PEM` in `cc282855/v2rayN`. Never place it in the client or repository. The scheduled workflow checks upstream daily, selects the highest semantic non-draft release (including prereleases, so it never downgrades from `7.24.3` to the older stable `7.23.4`), and skips only a complete published Quiet release. For a new or incomplete release it reapplies the UI layer, runs both test suites, verifies and imports only the upstream Windows `bin/` runtime payload, publishes both custom binaries, signs every immutable file in the complete package marker, creates or repairs a draft Release, verifies both uploaded asset digests, and only then publishes it as latest. A missing signing secret, upstream asset mismatch, patch conflict, test failure, build failure, marker failure, upload failure, or packaging failure leaves no newly published update, so existing clients stay on the last verified version and the next run can repair the draft.

The signed canonical bytes are UTF-8 lines in this exact order, ending with a newline: `schema`, `product`, `appVersion`, `platform`, `assetUrl`, lowercase `sha256`, `provenanceUrl`. URLs must be HTTPS, contain no credentials, and belong to the pinned owner/repository. The client streams the archive with a 512 MiB cap, checks the actual SHA-256, validates `qcc-package.json`, and rehashes every marked payload file before invoking the external helper.

The helper accepts only `AmazTool qcc-upgrade <absolute-instruction.json> <inherited-ready-pipe>`. Legacy arbitrary ZIP invocation exits nonzero. The inherited anonymous pipe is a one-shot ready handshake bound to a 192-bit random token, helper PID, and the exact originating PID, start time, executable path, and executable hash. The instruction, hash sidecar, package, and acknowledgement must use exact filenames inside `%TEMP%\QuietControlCenter\<random-guid>`. The helper acquires and validates the originating process before package work, performs bounded path-safe extraction on the installation volume, preserves only explicit mutable directories (`guiConfigs`, `guiLogs`, `logs`, and `binConfigs`), and rehashes the fully staged immutable tree before signaling ready. Only after the client validates ready and exits may the helper replace directories. Signed `bin` executables are always new-package-wins. Replacement is transactional and rolls back unless the new client emits the one-time startup acknowledgement. That acknowledgement commits the transaction; a locked backup is then left for deferred cleanup and can never roll back a running acknowledged version. Any pre-handoff failure keeps the current GUI running. The service waits for termination of the exact helper, derives the same direct-child `.qcc-stage-<full-token>` path from its own install directory and nonce, deletes that tree without following reparse points, and then removes the generated work root. It never accepts a cleanup path from the helper or package.

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
