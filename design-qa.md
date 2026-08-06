# Quiet Control Center design QA

Final inspection: 2026-07-29 (Asia/Shanghai)

Reference: `C:\Users\ADMINI~1\AppData\Local\Temp\codex-clipboard-b51a56d2-1f5b-4dec-9020-8fa47bb6d75f.png`

All frames below were rendered at 96 DPI by the final qcc-09 published `v2rayN.exe`. They were not copied from an earlier client build.

- Connected safe-loopback, 1487x1058: `.codex/state/runs/qcc-20260729-09/implement/final-connected-1487x1058.png` — SHA-256 `0C009012D1A6400C892EFEE115940E0F11D62967CBA2CA129109D44BDB6A6FB8`.
- Empty state, 1487x1058: `.codex/state/runs/qcc-20260729-09/implement/final-empty-1487x1058.png` — SHA-256 `37B61261880136D56F48EAF2E8294AD8237BC26462AC627E532C6436053D6CB2`.
- Responsive state, 1120x720: `.codex/state/runs/qcc-20260729-09/implement/final-responsive-1120x720.png` — SHA-256 `62B59A99696E335E287B95F981F6C098E9038C947A9721CDD31F3C69076EB87D`.
- Reference/final side-by-side, 2538x900: `.codex/state/runs/qcc-20260729-09/implement/comparison-reference-vs-final.png` — SHA-256 `BB0A2A22FBA9662AE35156270BEF5A73A789585C61CEB7782B93F4F7E81BEB8D`.

Inspection found no clipping, unintended overlap, stale GUI-update wording, broken hierarchy, or missing primary controls. The 1120x720 frame preserves navigation, hero status, node/empty area, details actions, and bottom connection controls. The connected frame uses a local-only SOCKS fixture and visibly reports `已连接`; the empty frame contains no transient snackbar.

The empty and responsive frame hashes remain identical to the pre-remediation UI baseline. The connected frame differs only in the live loopback latency value (75 ms), not layout or styling. The updater cleanup remediation therefore did not alter the selected UI.

Fixture processes were terminated by exact spawned PID, the loopback listener on port 19080 was removed, and pre-existing PID 58048 remained present and untouched.

P0: none

P1: none

P2: none

Final result: passed

## Compact summary and complete inspector QA — 2026-07-31

### Source visual truth

- Top connection summary: `C:\Users\ADMINI~1\AppData\Local\Temp\codex-clipboard-04d39093-7f25-4da5-be5a-d0a080f34ab3.png`, 1620x201 pixels.
- Node inspector: `C:\Users\ADMINI~1\AppData\Local\Temp\codex-clipboard-0b4730ee-e02a-498c-9bb8-12de0b95abcc.png`, 383x576 pixels.

### Rendered implementation

- Minimum viewport: `.codex/state/runs/qcc-20260731-02/implement/compact-final-1120x720.png`, 1120x720 at 96 DPI, SHA-256 `DF8C0A27882D9B8D933F2A6D96172400E2AA06E425BD8C7129193E8B953A3E7E`.
- Normal viewport: `.codex/state/runs/qcc-20260731-02/implement/compact-final-1487x1058.png`, 1487x1058 at 96 DPI, SHA-256 `6306FB50D19C2EC21A7BCC6BD62ED60FDE3F6854E8BFCA338A39A1CC4DFD1F87`.
- State: connected, selected V5 Singapore node, populated protocol/address/port/subscription, untested delay and speed.

### Density normalization and focused comparisons

The user references are display-density crops rather than 96-DPI full-window captures. They were normalized by width while preserving aspect ratio; the implementation regions remained at their exact 96-DPI layout size.

- Top comparison: `.codex/state/runs/qcc-20260731-02/implement/comparison-top-source-vs-final.png`, 1314x295, SHA-256 `25F370B9443B66BA7361A3AC5E276639A169DEE680B121332B76A640C4FFD61A`. The 1620x201 source was normalized to 1314x163; the implementation crop is 1314x120. Top half is the source and bottom half is the implementation.
- Inspector comparison: `.codex/state/runs/qcc-20260731-02/implement/comparison-detail-source-vs-final.png`, 320x983, SHA-256 `CC37BF1BA73D6F97E6166F5926CBCBE277FF526DBCC184FF3403949308EB747D`. The 383x576 source was normalized to 320x481; the implementation crop is 320x410. Top half is the source and bottom half is the implementation.

### Full-view and focused findings

- Fonts and typography: the hero node title is 18px and the inspector title is 16px; secondary labels remain 11-12px. The provided node name is no longer ellipsized in either region. Weight, family, ClearType behavior and hierarchy remain consistent with Quiet Control Center.
- Spacing and layout rhythm: the summary row is reduced from 150 to 120 pixels. The inspector is widened from 264 to 320 pixels, uses 22-pixel data rows, and keeps all eight fields plus all six node actions visible at 1120x720.
- Responsiveness: no overlap, clipped controls or broken grids are visible at 1120x720 or 1487x1058. The inspector retains a scrollbar only as a fallback for unusually long content; the tested data is fully visible without scrolling.
- Colors and tokens: existing Quiet Control Center white surfaces, muted labels, blue primary actions, green connection badge and red untested state are unchanged.
- Image and icon fidelity: these regions contain no raster imagery. Existing Material Design icons are preserved, aligned and reduced only where their enclosing controls were compacted.
- Copy and content: connection labels, state values, eight inspector fields and six action labels are unchanged. The duplicate in-body node badge was removed because the panel header already communicates the same role and it consumed the node-name width.
- Interaction evidence: the UI capture mode verified stable rendering; `v2rayN.Tests` static command assertions continue to cover the latency and mixed-test bindings. No interaction behavior was changed in this iteration.
- Accessibility: the smallest visible type remains at least 11px, controls remain at least 32px high, color semantics are unchanged, and wrapping replaces truncation for the selected node name and address.

### Comparison history

- Pass 1: no actionable P0, P1 or P2 findings. The confirmed plan targets were met without a visual remediation loop.

P0: none

P1: none

P2: none

P3: the fallback inspector scrollbar remains available for unusually long multi-line values; this is intentional and does not hide the tested data.

final result: passed

## Profile traffic columns and active-node period totals — 2026-08-05

### Source visual truth

- User reference: `C:\Users\ADMINI~1\AppData\Local\Temp\codex-clipboard-3b0b4492-c3ca-45d0-8aa7-c5a3afed67f1.png`, 1290x809 pixels.
- The reference shows the node table ending at the port column, with the historical traffic columns unreachable.

### Implementation evidence

- The native DataGrid retains virtualization and uses its own automatic horizontal scrollbar plus two-axis touch panning.
- Four persisted traffic columns now receive explicit 104 DIP widths with 96 DIP minimums and are made visible whenever statistics are enabled, independently of legacy column-width metadata.
- Header and cell horizontal padding is reduced from 12 to 8 DIP; truncated cell content has a full-value tooltip.
- The current-node summary wraps the subscription name followed by actual today/month upload and download totals. Stored values are scoped to the active node and reset at local day/month boundaries.
- Static XAML parsing, all 239 ServiceLib tests, all 77 UI tests, and the WPF win-x64 Release build passed with 0 warnings and 0 errors.

### Blocking visual gate

- A same-state implementation screenshot could not be captured from this Codex session. The main PE intentionally requires administrator elevation, while the capture host cannot accept the Windows elevation boundary. Two isolated capture fixtures were terminated by their exact spawned PIDs after timeout.
- The pre-existing `玄同` PID 83292 and `sing-box` PID 40536 remained running and were not signaled, restarted, or replaced. No user configuration, database, or subscription URL was read.
- Because no rendered implementation frame exists in the same populated-table state as the reference, visible clipping, scrollbar reachability, and 1120x720/1487x1058 pixel fidelity cannot be truthfully marked passed here.

P0: none found by static or automated validation

P1: visual comparison unavailable

P2: none found by static or automated validation

final result: blocked

## Active-node traffic card — 2026-08-06

### Visual evidence

- Source visual truth: `C:\Users\ADMINI~1\AppData\Local\Temp\codex-clipboard-252f72b1-03b9-4a40-8572-e9b5a92d80ae.png`.
- Rendered implementation: `C:\Users\Administrator\Documents\Codex\2026-07-28\weekday-morning-brief-7-30-slack\outputs\Xuantong-traffic-card-ui\traffic-card-1120x720.png`.
- Focused implementation crop: `C:\Users\Administrator\Documents\Codex\2026-07-28\weekday-morning-brief-7-30-slack\outputs\Xuantong-traffic-card-ui\traffic-card-focus.png`.
- Source pixels: 558x157. Implementation pixels and CSS viewport: 1120x720 at 96 DPI and device scale 1. Focus crop: 306x122. No density normalization was required; the focused comparison intentionally preserves the narrower responsive desktop slot.
- State: deterministic connected-node QA sample with the same node, subscription, 46.0 KB upload and 111.0 KB download values used by the reference. The QA process used an isolated temporary configuration and did not read user subscriptions.

### Comparison

- Full view: the new traffic card stays entirely inside the current-node column at the minimum supported 1120x720 layout. The adjacent quota and connection-quality cards remain aligned, and no persistent controls are clipped or displaced.
- Focused view: the subscription is separated from traffic data. A bordered, rounded, raised-surface card presents `当日流量` on the first row and `本月流量` on the second row, with upload/download values fully visible. The card remains intentionally more compact than the unboxed reference because the user requested a small UI frame.
- Fonts and typography: existing responsive Qcc font tokens are retained; row labels use the smaller semibold hierarchy and values use readable medium-weight text. No truncation or half-line rendering is visible.
- Spacing and layout rhythm: 8x5 inner padding, 9 DIP radius, a 3 DIP row gap and a 58 DIP label column create consistent alignment without increasing the header height excessively.
- Colors and visual tokens: `QccSurfaceRaised`, `QccBorder` and `QccMuted` keep the component consistent across supported themes; the connected badge retains its semantic success color.
- Image quality and asset fidelity: the target contains no component imagery or custom icons. Existing application assets remain unchanged and render sharply.
- Copy and content: `当日流量` and `本月流量` match the requested hierarchy. Both rows display the active node's actual upload/download totals and update through the existing statistics event.
- Primary interaction: read-only status presentation; no new interaction was required. The isolated capture produced no error file, and the protected `玄同` and `sing-box` PIDs were unchanged before and after capture.

### Findings

P0: none

P1: none

P2: none

P3: none

### Comparison history

- First visual comparison found no actionable P0/P1/P2 mismatch. No post-comparison visual fix was required.

### Validation

- WPF Release build: 0 warnings, 0 errors.
- ServiceLib tests: 239/239 passed.
- UI tests: 77/77 passed.
- XAML XML parse and traffic-card static contract: passed.

final result: passed

## Brand icon generation and application-surface verification — 2026-08-05

Source and generation mode:

- Built-in `image_gen` was used exactly once in `logo-brand` mode; no CLI/API fallback and no external competitor asset were used.
- Built-in generated source: `E:\ZTY\CODEX存储目录\Codex双版本\Codex-26.707.3748.0-data\codex-home\generated_images\019fd0c8-d0c5-7d20-8466-e61f646507eb\exec-4425c10f-6695-41fc-a441-a5acd91ebd9c.png`.
- Immutable project chroma source: `branding/source/mika-wind-gate-chroma-source.png`, generated at 1254x1254 RGB and copied without resampling, SHA-256 `C4A7CBE53799F29077BEC13202C6D6C702327D9965F2F1D9B0A3378A2E02590B`.
- Final prompt: `Use case: logo-brand. Asset type: original 1024x1024 application logo and Windows shortcut icon master for a modern accelerator/proxy client. Create one original centered dark-indigo rounded-square tile containing a bold abstract wind-gate / connected-orbit-arrow symbol made from one broad cyan-to-violet ribbon and exactly one small warm-gold node. Use a perfectly flat solid #00FF00 removable chroma background; strong near-square silhouette, approximately 82% coverage and readable at 16px; vector-like flat geometry. No text, letters, numbers, infinity symbol, shield, animal, literal lightning bolt, watermark, mockup, floor, texture, background gradient, 3D extrusion or trademark resemblance.`

Transparency and density:

- The installed imagegen chroma helper sampled `#0BF804` and removed the background with soft matte and despill. The frozen alpha-extracted source is `branding/source/mika-wind-gate-alpha-extracted-source.png`, SHA-256 `D2B2A67174FA0496E07B9C0B237B55D25B3DB23EE39F00B5B29754325298F1F3`.
- `tools/build-brand-assets.py` normalizes the alpha source to the deterministic 1024x1024 RGBA master `branding/master/mika-wind-gate-transparent-1024.png`, SHA-256 `DF739064E84E9F038923268D997CD0FB1D6FBDDCA51F0B38CFE96E9BF512C9F4`.
- The final alpha bounding box is `(137,131)-(886,892)`, or 749x761 pixels; 557,839 pixels are visible (53.20% area density), 9,158 edge pixels are partially transparent, all four corners are fully transparent, and no visible chroma-green pixel remains.

Visual evidence:

- Light 1:1 contact sheet: `branding/evidence/mika-brand-contact-light-1024.png`, 1024x1024, SHA-256 `6B17C0756A0D508D58D1556325CAEC63806263B23749C02C1E8C7FAC892FFF42`.
- Dark 1:1 contact sheet: `branding/evidence/mika-brand-contact-dark-1024.png`, 1024x1024, SHA-256 `ECCF801D749E022FFFF75C6F61A87CB35EC35A7FA5A9747F8A70A1655CA1548E`.
- Both sheets show literal 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixel reductions. The dark-indigo tile, cyan/violet wind path and single gold node remain distinguishable at taskbar/tray sizes.
- The first generated contact-sheet layout allowed the 128px preview and bottom note to overlap. The deterministic layout was corrected before packaging: labels now occupy separate padding, 256px has its own card and neither light nor dark evidence has overlap or clipping.
- Packaged PE associated-icon evidence: `outputs/QuietControlCenter-v7.24.4.20-brand-icon/evidence/米卡-pe-associated-icon-32.png` and `AmazTool-pe-associated-icon-32.png`; each differs from the source ICO 32px rendering by only `0.1211` mean RGB levels on a 0-255 scale.
- Evidence-only shortcut: `outputs/QuietControlCenter-v7.24.4.20-brand-icon/evidence/米卡-快捷方式图标验收.lnk`; its target is the isolated clean-package `米卡.exe`, its icon location is `米卡.exe,0`, and it was not launched.

Application coverage and invariants:

- `MikaLogo.png` remains the 512x512 top-left in-app logo. WPF EXE/window/taskbar/tray resources, all four WPF notify icons, all five Avalonia ICO resources, Avalonia PNG/ICNS and the updater helper now derive from the same frozen master.
- The Windows ICO contains nine 32-bit PNG frames at 16/20/24/32/40/48/64/128/256 pixels; all WPF, Avalonia and AmazTool ICO copies have SHA-256 `D64BFDC8BF4FCA88F485A19BA65BF6F559AA68C33065FFBB79432FCEA9650B1D`.
- The application name and executable name remain `米卡`. The 56 semantic Material Design menu/action glyphs, routing custom icons and bundled third-party core executables were not changed.
- `tools/verify-brand-assets.ps1` checks immutable input hashes, exact generated bytes, alpha corners, ICO frames, cross-project ICO equality, updater embedding, final PE associated icons and shortcut icon location.

Build, package and privacy:

- Release solution build with version `7.24.4.20`: 0 errors and one pre-existing out-of-scope `GlobalHotKeys/HotKeyManager.cs` nullability warning. Separate Desktop build: 0 warnings and 0 errors. `ServiceLib.Tests`: 233/233. `v2rayN.Tests`: 75/75.
- Clean marker: `QuietControlCenter / win-x64 / 7.24.4.20`, 35 immutable files; all clean-directory and ZIP entry hashes matched. The ZIP contains 36 files including the marker.
- Public ZIP: `outputs/QuietControlCenter-v7.24.4.20-brand-icon/QuietControlCenter-7.24.4.20-win-x64.zip`, SHA-256 `144ECA24ED020E61080DFFBF1CD91AEF21588561139BFEF35E106712C4D76210`, size 191,162,654 bytes.
- The public clean package and ZIP contain no `guiConfigs`, runtime state, database, log, key, secret or shortcut. The separate `local-continuity/qcc-win-x64` copy received the previous 7.24.4.19 opaque `guiConfigs` directory by directory copy only; its two files were not opened or inspected.
- Extracted main PE manifest contains `requireAdministrator`; main FileVersion is `7.24.4.20`. Neither the newly built main executable, updater, nor evidence shortcut was launched.
- Process isolation: the protected `米卡` PID remained `85116`. The user-controlled application independently replaced its `sing-box` child PID `91028` with PID `69860` during final hash verification; no task command launched, stopped, restarted, signaled or overwrote either process.

P0: none

P1: none

P2: none after contact-sheet layout correction

final result: passed

## Core automatic-selection control visibility inspection — 2026-08-05

### Source visual truth

- User-reported clipped control: `C:\Users\ADMINI~1\AppData\Local\Temp\codex-clipboard-b0996567-a9ad-4d7e-9ec0-872e6595f6e2.png`, 1482x1041 pixels. The long label was rendered inside the compact toggle content slot and was unreadable.

### Rendered implementation

- Minimum QA viewport: `outputs/QuietControlCenter-v7.24.4.19-core-ui-fix/evidence/core-settings-900x600.png`, 900x600 at 96 DPI, SHA-256 `0E5D6CCB30739D519791E3F9E45E5B703425163F2B7193F8A0BA4DA3E376C29D`.
- Default window: `outputs/QuietControlCenter-v7.24.4.19-core-ui-fix/evidence/core-settings-1000x700.png`, 1000x700 at 96 DPI, SHA-256 `2866FD14CAF9A78AECD8094A3BFD069FBAD90F694BDCE93B80466E46FFCA144D`.
- Standard compact viewport: `outputs/QuietControlCenter-v7.24.4.19-core-ui-fix/evidence/core-settings-1120x720.png`, 1120x720 at 96 DPI, SHA-256 `24E94AC19DB8326CEE9E034012CC09D43C24D12C25A95D4B6B0D8979F9833C49`.
- Large viewport: `outputs/QuietControlCenter-v7.24.4.19-core-ui-fix/evidence/core-settings-1487x1058.png`, 1487x1058 at 96 DPI, SHA-256 `6CBE6C73835CBA9B69E4E9E59159CAD0CCC07592411B823486E9893F9444B70C`.

### Literal comparison artifacts

- Full source-versus-fixed canvas: `outputs/QuietControlCenter-v7.24.4.19-core-ui-fix/evidence/comparison-core-settings-full-source-vs-fixed.png`, 2974x1058 pixels, SHA-256 `7D59AB7005BFAEB843979FD515702095EF83F9A0ED71E0B96BA8401376467FF7`. The left 1487x1058 panel contains the original 1482x1041 user screenshot centered without resampling; the right panel contains the exact 1487x1058 fixed render. The left input includes the Windows title bar and the user's DPI/manual-mapping state, while the right render intentionally omits operating-system chrome and uses an isolated clean configuration. Those state differences are not treated as UI regressions.
- Focused bad-toggle-versus-fixed-control canvas: `outputs/QuietControlCenter-v7.24.4.19-core-ui-fix/evidence/comparison-core-settings-toggle-source-vs-fixed.png`, 2200x100 pixels, SHA-256 `9ACC7E1455E1E157C4021307C912F52192EB1F01929AF076C053660BD7726AFF`. The left half is the original unreadable toggle-content region; the right half is the fixed independent title, compact switch, explicit `已开启` state and complete explanation. Both halves are literal crops from the cited inputs.

### Findings

- Typography and content: `自动匹配合适的内核` is now a separate wrapping title. The switch contains no text; the adjacent `已开启` / `已关闭` label reflects its actual checked state. The full compatibility explanation remains visible.
- Layout and responsiveness: the existing 1000x700 default is preserved, a conservative 840x560 minimum is enforced, and the Core tab uses vertical scrolling with horizontal scrolling disabled. At 900x600 all eight manual mappings and both footer buttons remain visible without clipping or overlap.
- Interaction and accessibility: the switch remains bound to `EnableAutoCoreSelection`, is two-state, focusable and keyboard reachable, and exposes a Chinese automation name, help text, tooltip and polite live-state announcement. Manual mapping combo boxes remain enabled and editable.
- Visual consistency: existing font, spacing, color, switch, combo-box, tab and button resources are reused. No global style or unrelated layout was changed.
- Runtime isolation: final captures ran from a new isolated 7.24.4.19 managed QA directory with no subscriptions. The QA target now bypasses creation of the one-second metrics timer, speed bindings, connection-state polling and subscription-quota scheduling before its `OnLoaded` early return; it also skips update polling and creation of the tray view, never invokes Save, and terminates only its fixture process. Protected processes were unchanged before and after every final capture: `米卡.exe` PID 85116 and `sing-box.exe` PID 51276.

### Comparison history

- Pass 1, user baseline: P1 readability failure—the label was placed inside the compact switch content slot and collapsed into an illegible mark.
- Pass 2, final full and focused comparisons: the title, switch and state are independently legible; the compatibility description and all manual mappings remain present. No final P0, P1 or P2 issue remains.
- Pass 3, isolation review: a P2 test-fixture defect was found because ReactiveUI activation still created the one-second metrics timer and called `UpdateSubscriptionQuotaAgeAndSchedule()` before the Core-settings QA `OnLoaded` return. Both operations are now structurally enclosed by `if (!_coreSettingsQaMode)`, with static regression assertions covering timer/quota ordering and the early-return ordering.
- Pass 4, post-remediation rerender: all four settings screenshots and both literal comparison canvases were regenerated from a fresh isolated directory and are pixel-identical to Pass 2. Fixture PIDs 85996, 82768, 78380 and 2368 exited independently; protected Mika/core PIDs did not change. The P2 is resolved and no final P0, P1 or P2 remains.

P0: none

P1: none

P2: none

P3: the render-target evidence excludes the operating-system title bar; the complete WPF settings surface is captured at each requested dimension.

final result: passed

## Active subscription name display verification — 2026-08-05

- The current-node card now resolves the real active profile's subscription and displays only its trimmed remarks as `订阅：名称`. The formatter accepts no URL argument, so domains, paths, query strings and tokens cannot enter this UI text path. Local nodes, removed sources, no active node and unnamed subscriptions use explicit Chinese placeholders.
- Release solution build for `7.24.4.17`: exit 0, 0 warnings and 0 errors. `ServiceLib.Tests`: 202/202. `v2rayN.Tests`: 72/72. `git diff --check`: exit 0.
- Public marker: `QuietControlCenter / win-x64 / 7.24.4.17`; ZIP entries 36, marker payload hashes 35/35, clean-package mismatches 0, ZIP mismatches 0, and forbidden/private runtime entries 0. ZIP SHA-256: `AED18BF66C303CBB680E2BAFC4F320DD6D69EDF9F0A6F0C5ED48A22EB48B1A11`; size 190,875,149 bytes.
- Windows `mt.exe` extracted both clean and local executable manifests; both report `requireAdministrator` with `uiAccess=false`. The packaged executable file version is `7.24.4.17`.
- The local continuity database was created from the currently used `7.24.4.15` data with SQLite online backup; integrity is `ok`, with 2 subscription rows and 174 profile rows. No subscription value was inspected or printed. The public ZIP and clean package contain no configuration database, logs, key material or `guiConfigs` directory.
- The task did not launch, stop, restart, signal or overwrite the active Mika PID 85116 or sing-box PID 83768. The new `7.24.4.17` executable was not launched.

final result: passed

## Active-node truth and activation verification — 2026-08-05

- The previous hero label was bound to `SelectedProfile.Remarks`, so a single row click made the heading look like the selected row was active even though the persisted `Config.IndexId` and running sing-box outbound remained unchanged. The active runtime check proved the mismatch: the selected quota row used port 58511 while `Config.IndexId` and the generated sing-box `proxy` outbound used the profile `日本1 高速中转A` on port 8819.
- The hero label now binds to `ProfilesViewModel.ActiveProfileRemarks`, which is refreshed from `AppManager.Instance.GetProfileItem(_config.IndexId)`. A single click remains selection/details only.
- Row double-click now always awaits `SetDefaultServer`; it no longer consults `DoubleClick2Activate` and cannot open edit. The existing context-menu and detail-panel edit actions remain available.
- A dedicated 30-pixel marker column renders a red `★` only for `ProfileItemModel.IsActive == true`. The footer explicitly explains single-click details, double-click activation and the red-star meaning.
- Release solution build: exit 0, 0 errors and one pre-existing out-of-scope `GlobalHotKeys` nullable warning. `ServiceLib.Tests`: 197/197. Rebuilt `v2rayN.Tests`: 71/71. `git diff --check`: exit 0.
- Public marker: `QuietControlCenter / win-x64 / 7.24.4.15`; ZIP entries 36, marker payload hashes 35/35, mismatches 0, forbidden/private runtime entries 0 and `guiConfigs` 0. ZIP SHA-256: `A540338F7BBC3E954CA3341E13049FC4CF47B4F2CAB6486D808F144AFFFE6C8C`; size 190,875,559 bytes.
- Local continuity was created with SQLite `.backup`; integrity is `ok`, with 2 subscriptions and 174 profiles. Clean and local marker/application hashes match exactly.
- The running 7.24.4.14 Mika process PID 8144 remained unchanged and was not stopped or replaced. Its managed sing-box child restarted independently during packaging; no task command launched, stopped, restarted or signaled an application/core process. The new 7.24.4.15 executable was not launched.

final result: passed

## High-DPI metrics and overall layout inspection — 2026-08-05

### Source visual truth

- User-supplied clipping reference: `C:\Users\ADMINI~1\AppData\Local\Temp\codex-clipboard-e976e5b2-87fa-4b48-bb8f-9a2ebeb4f3c8.png`, 2036x1232 pixels.
- The reported defect was concentrated in the top live-metrics cards: delay, jitter/loss and speed/status content did not have enough vertical space. The final inspection also covered the node toolbar, DataGrid, inspector actions, combined test buttons and bottom status controls.

### Rendered implementation

- Minimum viewport: `C:\Users\Administrator\Documents\Codex\2026-07-28\weekday-morning-brief-7-30-slack\outputs\QuietControlCenter-v7.24.4.13-layout\visual-qa\layout-1120x720.png`, 1120x720, SHA-256 `2A8EDEB0FD03050CC42F08907555ED1E16AF665A9FC9948423C8D28963172373`.
- Intermediate viewport: `C:\Users\Administrator\Documents\Codex\2026-07-28\weekday-morning-brief-7-30-slack\outputs\QuietControlCenter-v7.24.4.13-layout\visual-qa\layout-1365x819.png`, 1365x819, SHA-256 `65015314431B7FCF6CBF95910B5F5DDE204A9280D74049D32313EF9413B64BDD`.
- Normal viewport: `C:\Users\Administrator\Documents\Codex\2026-07-28\weekday-morning-brief-7-30-slack\outputs\QuietControlCenter-v7.24.4.13-layout\visual-qa\layout-1487x1058.png`, 1487x1058, SHA-256 `266A7B496DB703AC039B9FADF4763D81BAEBF006895CD21A09EF09F3C23728F7`.
- Windows registered scaling was 150% (`AppliedDPI=144`) for all three captures.
- QA states intentionally exercised all quality colors: 260 ms / 70 ms / 8% red; 145 ms / 35 ms / 2% yellow; 78 ms / 12 ms / 0% green.

### Comparison evidence

- Full-window comparison: `C:\Users\Administrator\Documents\Codex\2026-07-28\weekday-morning-brief-7-30-slack\outputs\QuietControlCenter-v7.24.4.13-layout\visual-qa\comparison-reference-vs-layout-1365x819.png`, SHA-256 `3FCB3D2D27730F69FF10ABB6A209BEF005F0EF9534460E659ADD37E47F074CD4`.
- Focused metrics comparison: `C:\Users\Administrator\Documents\Codex\2026-07-28\weekday-morning-brief-7-30-slack\outputs\QuietControlCenter-v7.24.4.13-layout\visual-qa\comparison-top-metrics-reference-vs-layout.png`, SHA-256 `B139D1CE85C3C51209FACE4E91DD1061D9E3F47038C2B07510A5A62BDF6DC8FF`.

### Findings

- Fonts and typography: the existing responsive typography remains clamped to 0.92–1.18 after 0.05-step quantization. Metric values may wrap instead of being ellipsized, so both upload/download speed and their labels remain visible.
- Spacing and layout rhythm: fixed summary, toolbar, DataGrid and inspector heights were replaced by content-driven rows with minimum heights. This keeps the compact structure while allowing 150% DPI text to occupy its required space.
- Top metrics: proxy/direct speed, delay, jitter/loss and runtime status are simultaneously visible in all three frames. No value, unit or label is clipped.
- Node work area: the node toolbar, empty/list area, right inspector and node actions keep their boundaries. The inspector uses the center star row for scrolling and preserves the action row at the bottom.
- Testing and status controls: `测延迟`, `延迟+速度`, system proxy, TUN, route mode and both port-status lines remain inside every tested viewport.
- DataGrid: headers and rows have minimum heights rather than forced heights; long cells remain accessible through the existing horizontal/vertical scrolling behavior.
- Process isolation: captures did not reload a core. Pre-existing `米卡.exe` PIDs 53988 and 69932 and `sing-box.exe` PID 79076 remained present after visual inspection; no user application or proxy-core process was closed, restarted or replaced.
- Privacy: the visual fixture was isolated and did not use the user's saved subscription/configuration database. No subscription URL was read or recorded.

### Comparison history

- Pass 1 reproduced the 150% DPI clipping risk and identified fixed vertical dimensions in summary/status, toolbar, DataGrid and inspector regions.
- Pass 2 converted the affected layout rows to Auto plus minimum heights, enabled metric wrapping and constrained responsive font scaling.
- Pass 3 rendered all three native output sizes and the source comparisons. No actionable P0, P1 or P2 visual issue remains.

P0: none

P1: none

P2: none

P3: unusually long provider or node text may still require the existing scrollbars; this is intentional and does not hide persistent controls.

final result: passed

## Responsive typography and compact status-bar inspection — 2026-08-04

### Source visual truth

- Right inspector and clipped port-status reference: `C:\Users\ADMINI~1\AppData\Local\Temp\codex-clipboard-a3e904dd-2fe4-48fa-8e02-b6e97ddfa4f2.png`, 399x761 pixels.
- Bottom system-proxy/TUN/routing reference: `C:\Users\ADMINI~1\AppData\Local\Temp\codex-clipboard-f7a50cc0-54fb-4088-9ab9-29ffb93eea81.png`, 1149x114 pixels.
- Exact structural baseline before this iteration: `C:\Users\Administrator\Documents\Codex\2026-07-28\weekday-morning-brief-7-30-slack\outputs\QuietControlCenter-v7.24.4.11-modern\visual-qa\modern-1120x720.png`, 1120x720 at 96 DPI.

### Rendered implementation

- Minimum viewport: `.codex/state/runs/qcc-20260804-12/visual/responsive-final-1120x720.png`, 1120x720 at 96 DPI, SHA-256 `CEC2D341E77BEA094EC439FB78E13752E5D2AF66F8F47472D2B84E61918A95A4`.
- Normal viewport: `.codex/state/runs/qcc-20260804-12/visual/normal-final-1487x1058.png`, 1487x1058 at 96 DPI, SHA-256 `807ADDB49F0620F9D5E3F688DAA7570D26F08C7DE4101419FF1FA92E7E157E2B`.
- State: isolated empty-profile QA data. No user configuration, subscription, database or log was read or copied. The capture process used a path-specific single-instance key and did not reload a core.

### Density normalization and comparison evidence

- Full-view comparison: `.codex/state/runs/qcc-20260804-12/visual/comparison-full-v11-vs-v12.png`, 2240x720, SHA-256 `73E307A3577D7565176889EA87CB5907F914BA4F79B03DA2C1AE061B54A43DCC`. The 1120x720 v7.24.4.11 baseline is on the left and the exact 1120x720 final frame is on the right; neither frame was rescaled.
- Inspector comparison: `.codex/state/runs/qcc-20260804-12/visual/comparison-right-source-vs-final.png`, SHA-256 `9A081331A68699A83959C9E62084034DDF1D376F737C8C7C72E23836B4B3936C`. The 399x761 source was normalized to 268x511, followed by the final exact 268x572 crop at x=852, y=148 from the 1120x720 frame.
- Status comparison: `.codex/state/runs/qcc-20260804-12/visual/comparison-status-source-vs-final.png`, SHA-256 `868D03DB4AD6B3281A05656ED1004E1C42D8A37A532152FB2BA6A39A30643B51`. The 1149x114 source was normalized to 988x98, followed by the final exact 988x72 crop at x=132, y=648.

### Findings

- Fonts and typography: all custom-shell sizes now resolve through dynamic resources. Window width/height produces a clamped 0.92–1.18 scale, quantized to 0.05 steps. The 1120 frame retains the compact baseline; the 1487 frame visibly increases navigation, summary, filter, inspector and status text without clipping.
- Spacing and layout rhythm: navigation width is reduced from 150 to 132 pixels. Node actions now follow the node details instead of being pinned below a large empty gap. The 72-pixel status region uses one 48-pixel horizontal control row and one 24-pixel live-status row.
- Colors and visual tokens: the existing surface, border, primary, muted and semantic-state resources are unchanged. The typography/layout change introduces no new color drift.
- Image quality and asset fidelity: the supplied Mika logo and Material Design icon family remain unchanged and sharp at both native captures. No placeholder, custom SVG, emoji or substitute asset was introduced.
- Copy and content: all requested Chinese labels remain present. The country filter uses `全部地区` only when no country item is selected; system proxy, TUN, routing and both port lines are visible simultaneously.
- Responsiveness and accessibility: persistent controls remain inside the 1120x720 viewport. Filter watermark duplication is removed, the country and group controls retain explicit automation names, and no information depends on color alone.
- Runtime isolation: process PID snapshots before and after capture were identical (`米卡.exe` 77108 and 61748; `sing-box.exe` 83472). No pre-existing application or core process was closed, restarted or replaced.

### Comparison history

- Pass 1 found a P2 overlap at 1487x1058: Material Design floating hints for country/group collided with the enlarged selected text. The redundant hints were removed and the country track was widened.
- Pass 2 found a P2 empty state: without the floating hint, an unselected country control showed only its arrow. A non-interactive `全部地区` watermark gated by `SelectedIndex == -1` was added, and the track was finalized at 160 pixels.
- Pass 3 used both native viewport captures plus the three normalized comparison artifacts above. No actionable P0, P1 or P2 issue remains.

P0: none

P1: none

P2: none

P3: none

final result: passed

## Subscription quota card inspection — 2026-08-04

### Source visual truth

- User reference and requested red-box placement: `C:\Users\Administrator\AppData\Local\Temp\codex-clipboard-111f1fae-e60e-452b-a528-4162ba8cf8c7.png`, 2078x1170 pixels.

### Rendered implementation

- Success state, minimum viewport: `.codex/state/runs/qcc-subtraffic-20260804-01/visual/qcc-subtraffic-1120x720-success.png`, 1120x720 at 96 DPI, SHA-256 `D955BE4FDFDC7A704CE6188571F052634C310A7D416D2A718F2E7BB3A6638AEF`.
- Success state, normal viewport: `.codex/state/runs/qcc-subtraffic-20260804-01/visual/qcc-subtraffic-1487x1058-success.png`, 1487x1058 at 96 DPI, SHA-256 `FDBF534253D2CD96D402EDCDD95B98156BDC4AF0DB556B15A4EC0DCD962D4724`.
- Unsupported-provider state: `.codex/state/runs/qcc-subtraffic-20260804-01/visual/qcc-subtraffic-1120x720-unsupported.png`, 1120x720 at 96 DPI, SHA-256 `D0B05936CA1267443CF988D499D451B26639B306C8BBA878EABF51E2AB56E73E`.
- Expired state: `.codex/state/runs/qcc-subtraffic-20260804-01/visual/qcc-subtraffic-1120x720-expired.png`, 1120x720 at 96 DPI, SHA-256 `091DDB78E86EF85913AAAF71D7BD065F88911D5B11BEFCFC1F553091FB9149BA`.
- State: isolated QA data, no user subscription persisted; the active-profile quota card shows success, unsupported, and expired outcomes.

### Density normalization and comparison evidence

- Full-view comparison: `.codex/state/runs/qcc-subtraffic-20260804-01/visual/comparison-subtraffic-full-source-vs-1120.png`, 2252x748, SHA-256 `BBF265BECC1F5B97E772AE2B2593EA5C6AF97A228EC7DFFFAEED54A1BF45A9A2`. The 2078x1170 reference was normalized to 1120x631 without stretching and placed beside the exact 1120x720 implementation.
- Focused top-summary comparison: `.codex/state/runs/qcc-subtraffic-20260804-01/visual/comparison-subtraffic-top-source-vs-1120.png`, 1120x372, SHA-256 `7FFD480EA9A3BCF7FAEBF0165F85921D0F6E7E9300E215AD0F7A85C31D2E8914`. The reference top region (2078x264) was normalized to 1120x142; the implementation top region remains an exact 1120x178 crop.

### Findings

- Fonts and typography: the quota title, amount, usage detail, stale-time label, and error states use the existing Quiet Control Center hierarchy. Long unsupported/expired labels remain readable without truncation at 1120 pixels.
- Spacing and layout rhythm: the quota card occupies the user-marked gap between current-node status and live traffic cards. It preserves the existing 10-pixel card radius, border, elevation, padding, and summary-row height.
- Colors and visual tokens: success data uses the established neutral text treatment, stale/error details remain legible, and the neighboring green/yellow/red connection-quality tokens are unchanged.
- Image quality and asset fidelity: the added card contains no raster imagery or substitute artwork. The refresh action reuses the existing Material Design icon family and remains optically aligned.
- Copy and content: `订阅余量`, remaining amount, `已用 / 总量`, expiry/unsupported wording, and last-refresh age are concise Chinese UI labels. The source and capture use different fixture versions and node states; those dynamic values are not layout mismatches.
- Responsiveness: no overlap, clipping, broken wrapping, or hidden persistent control is visible at 1120x720 or 1487x1058. The entire node area, inspector actions, and bottom controls remain available.
- Interaction and state coverage: startup/manual refresh, success, unsupported and expired UI states are represented; static tests cover activity-profile refresh wiring. Network parsing and privacy behavior are covered in ServiceLib tests.
- Accessibility: the card remains in the normal keyboard and screen-reader content order, the refresh button has an accessible tooltip, color is not the sole carrier of quota state, and 1120-pixel rendering shows no text collision.

### Comparison history

- Pass 1: the full-window and focused top-summary comparisons found no actionable P0, P1, or P2 difference. No visual remediation loop was required.

P0: none

P1: none

P2: none

P3: the QA capture displays isolated placeholder node/version data, intentionally different from the user's live screenshot; the final package version is assigned during publishing and real subscription data is loaded only at runtime.

final result: passed

## Rounded frame and quality-color inspection — 2026-08-03

### Source visual truth

- Current full-window reference supplied by the user: `C:\Users\Administrator\AppData\Local\Temp\codex-clipboard-01c889f8-bdcc-4d11-a1b3-8162dc9cf657.png`, 2048x1156 pixels.
- Current quality-card reference supplied by the user: `C:\Users\Administrator\AppData\Local\Temp\codex-clipboard-57709fd2-e03d-49f0-92dc-a22739b92a20.png`, 553x173 pixels.
- Exact 1120x720 structural baseline: `C:\Users\Administrator\Documents\Codex\2026-07-28\weekday-morning-brief-7-30-slack\outputs\QuietControlCenter-v7.24.3\final-responsive-1120x720.png`.

### Rendered implementation

The signed `quiet-7.24.4.6` release was rendered on an isolated GitHub Windows runner at 96 DPI; no local v2rayN or proxy process was started or stopped.

- Good state, 1120x720: `.codex/state/runs/qcc-20260803-01/implement/qcc-1120x720-good.png` — SHA-256 `C9EF7C40A3F3B6A6BDC7EFBB2C4C2BB4894779215421CA110AB073DB8D8178A6`.
- Warning state, 1120x720: `.codex/state/runs/qcc-20260803-01/implement/qcc-1120x720-warning.png` — SHA-256 `4602B33F30A60F4FFA993EBE5237103F7C0F4F096FD3AA90AFA9119CA0099E89`.
- Danger-state capture request, 1487x1058: `.codex/state/runs/qcc-20260803-01/implement/qcc-1487x1058-danger.png` — SHA-256 `70E6894741521B7AB0A821838EEC77122F04EF8BF73249158DAABD0E587CD3D2`. The runner work area constrained the native window to 1120 pixels, so only its top-left 1120-pixel region is valid visual evidence; the remaining black area is capture-environment padding, not application output.

### Density normalization and comparison evidence

- Full-view comparison: `.codex/state/runs/qcc-20260803-01/implement/comparison-baseline-vs-rounded-1120x720.png`, 2240x720, exact 1:1 96-DPI frames placed left-to-right (baseline, final), SHA-256 `C4B5264661F72B51FB3E3AB1A845A8223F0403E005B7DCEA1CC47FAD965EE0AC`.
- Focused quality-card comparison: `.codex/state/runs/qcc-20260803-01/implement/comparison-metrics-source-vs-warning.png`, 1106x173, user source on the left and the final warning state normalized to the same 553x173 region on the right, SHA-256 `2E39609D4D0CF96279491F8B4E7F46C3880A4E697ADF91BFA42BF7464555B53A`.

### Findings

- Fonts and typography: existing font family, 11–18px hierarchy, wrapping and truncation behavior remain unchanged. Metric values retain readable 12px text and semantic color does not reduce legibility.
- Spacing and layout rhythm: the outer 12px frame radius and 10px status-card radii are visibly consistent. Subtle 1px borders and low-opacity elevation add depth without changing control dimensions, columns, navigation, or table layout.
- Colors and visual tokens: 78ms / 12ms / 0% renders green; 145ms / 35ms / 2% renders yellow; 260ms / 70ms / 8% renders red. The combined jitter/loss color follows the more severe value. Disconnected/no-data values remain muted gray.
- Image and icon fidelity: the supplied Mika logo and Material Design icons remain sharp and unchanged; no raster placeholder, custom SVG, emoji, or substitute asset was introduced.
- Copy and content: application labels and feature structure are unchanged. The QA-only sample switch is command-line gated and does not appear in normal use.
- Responsiveness and polish: at 1120x720, all persistent navigation, status, search/filter controls, inspector actions and bottom controls remain visible with no overlap or clipping. Rounded corners and the outer outline remain visible against the white comparison canvas.
- Functional evidence: Release build completed with 0 warnings and 0 errors; ServiceLib tests passed 141/141 and UI tests passed 64/64. Threshold edges are covered at delay 100/101/200/201ms, jitter 20/21/50/51ms, and loss 0/1/5/6%.

### Comparison history

- Pass 1: the 1120x720 full-view comparison and focused metric comparison found no actionable P0, P1, or P2 visual mismatch. No remediation loop was required after the final capture.

P0: none

P1: none

P2: none

P3: the hosted runner cannot produce a true 1487x1058 native-window frame because its work area is smaller; this affects only the large screenshot artifact, not the application layout or the signed package.

final result: passed

## Profile fields and combined testing inspection — 2026-07-31

References:

- `C:\Users\ADMINI~1\AppData\Local\Temp\codex-clipboard-492e321a-1b66-44ef-9a9c-8e35b20db11e.png`
- `C:\Users\ADMINI~1\AppData\Local\Temp\codex-clipboard-9513eb7a-8d93-476c-90e1-4799767aabac.png`

Both final frames were rendered at 96 DPI by the deployed executable at `outputs/QuietControlCenter-v7.24.3/qcc-win-x64/v2rayN.exe` after package-marker and per-file hash validation.

- Normal frame, 1487x1058: `.codex/state/runs/qcc-20260731-01/implement/final-columns-1487x1058.png` — SHA-256 `E4F6764C0AAEAE0C3CDBC66F018C44F9C87A93E872084FDF73D1B22816CC74AF`.
- Minimum supported frame, 1120x720: `.codex/state/runs/qcc-20260731-01/implement/final-columns-1120x720.png` — SHA-256 `DC38FA9DCAD246DE39E2390C80068704D04F90E40906EEE31D51626FE9A0FF4F`.

Inspection confirms that the redesigned hierarchy, typography, search→country→subscription controls, horizontal node-table access, right-side details, bottom connection controls, and both `测延迟` and `延迟+速度` actions remain visible without overlap at 1120x720. The full-size frame exposes protocol, node name, address, port, transport, security and group columns; remaining columns are reachable through the table's horizontal scroll. Configurable hidden columns use `Visibility.Collapsed`, so neither headers nor cell bands remain in layout. Independent review also verified that saving field visibility does not reset live column widths or order.

P0: none

P1: none

P2: none

final result: passed

## First-save subscription update verification — 2026-08-05

- A subscription created through either subscription entry point is persisted first, reloaded by its generated opaque ID, and automatically updated once only when it remains enabled and has an HTTP(S) address. Edits, invalid or disabled items, empty IDs, validation failures and persistence failures do not start an automatic update.
- The awaited callback is scoped `MainWindowViewModel -> ProfilesViewModel/SubSettingViewModel -> SubEditViewModel`; there is no new global event or fire-and-forget subscription callback. One global semaphore serializes all updates. Coalescing now requires the same trimmed non-empty subscription ID and identical `UseProxy`, `AllowDirectFallback`, and `IsAutomatic` semantics; same-ID requests with different policies remain separate tasks and execute in arrival order.
- Automatic first updates require the existing proxy, target only the newly persisted ID, and prohibit direct fallback. Manual update behavior retains direct fallback compatibility.
- HTTP and HTTPS eligibility is case-insensitive in both the first-save policy and subscription handler, while exact persisted subscription-ID matching remains unchanged. Successful automatic updates restore and save the original active profile only when the import changed `IndexId`; manual behavior is unchanged. The dedicated automatic callback refreshes both profile and status-bar node data and optionally adjusts column widths; it performs no reload, core lifecycle, system-proxy or active-node switch.
- Subscription response bodies, short bodies, URLs and exception details are not emitted by the subscription handler. Automatic failure leaves the saved subscription in place and restores pre-existing nodes if parsing fails.
- Release build: exit 0, 0 errors, with one pre-existing nullable warning in the out-of-scope `GlobalHotKeys/HotKeyManager.cs`. `ServiceLib.Tests`: 197/197, including deterministic identical-policy coalescing and both cross-policy orderings. `v2rayN.Tests`: 70/70, including the production-call and successful-selection-preservation assertions. `git diff --check`: exit 0.
- Public clean package marker: product `QuietControlCenter`, platform `win-x64`, version `7.24.4.14`; 35/35 immutable files matched their marker SHA-256 values. The ZIP contains 36 file entries including the marker and no runtime/config/database/log/key/sidecar/debug content. Its payload set matches 7.24.4.13 with no added or removed basename, and the unbranded build app host is not present.
- Public ZIP SHA-256: `398D872849B91E5B083CC6ED8B626A4B3BC86F93976F76463696BE37959A2254`; size 190,602,700 bytes. It remained separate from local continuity state.
- Local continuity database was created with SQLite `.backup`; integrity is `ok`, with 2 subscription rows and 174 profile rows. The public ZIP and clean package contain no `guiConfigs` directory.
- Process isolation: Mika PIDs 53988/69932 remained unchanged. The user-controlled Mika process 53988 independently replaced its child `sing-box` PID 66500 with PID 87620 during the verification interval; no build, test, packaging, or QA command launched, stopped, restarted, signaled, or replaced an application/core process, and the active connection remained running.

final result: passed

## Administrator launch policy verification — 2026-08-05

- `v2rayN/v2rayN/app.manifest` now embeds `<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />`, and `v2rayN.csproj` continues to use that manifest through `ApplicationManifest`. This policy therefore follows every future locally built or auto-updated `米卡.exe`; it does not depend on shortcut properties.
- The manifest was extracted back from both the staged and final local `米卡.exe` with Windows `mt.exe`. Both reported `requireAdministrator` and `uiAccess=false`; the final file version is `7.24.4.16`.
- Release solution build: exit 0, 0 errors and one pre-existing out-of-scope `GlobalHotKeys` nullable warning. `ServiceLib.Tests`: 197/197. Rebuilt `v2rayN.Tests`: 72/72. `git diff --check`: exit 0.
- Public marker: `QuietControlCenter / win-x64 / 7.24.4.16`; ZIP entries 36, marker payload hashes 35/35, mismatches 0, forbidden/private runtime entries 0 and `guiConfigs` 0. ZIP SHA-256: `48149C3743DABF8475CD66776661B9392F9F04889825110287D1AA2CDC4CB945`; size 190,875,220 bytes.
- After the user independently launched 7.24.4.15, local continuity was refreshed from that active version with SQLite `.backup`; integrity is `ok`, with 2 subscriptions and 174 profiles. No subscription values were inspected.
- The task did not launch, stop, restart or signal the active Mika or sing-box processes. The new 7.24.4.16 executable was not launched.

final result: passed

## Automatic compatible-core selection verification — 2026-08-05

- Version `7.24.4.18` selects between Xray and sing-box after a subscription has been parsed into nodes. The decision uses each node's protocol, normalized transport and, for Shadowsocks, encryption method; it does not inspect or classify the subscription URL itself.
- A node's explicit core remains highest priority. With automatic matching disabled, the existing manual core mapping is used unchanged. With it enabled, the manual mapping remains preferred and only falls back to the other Xray/sing-box core when the preferred core is incompatible. Custom, strategy-group and non-Xray/sing-box mappings retain their prior behavior.
- TUIC, AnyTLS and Naive select sing-box when the manual preference is incompatible. `kcp` and `xhttp` nodes fall back from sing-box to Xray. Hysteria2 and WireGuard preserve the preferred compatible core. Unknown or mutually unsupported combinations remain on the preferred core so the existing validator can report the actual incompatibility.
- The Chinese option `自动匹配合适的内核` is enabled by default, including for older configurations that do not contain the new field. Existing manual mapping controls remain available and save normally.
- Release solution build completed with 0 warnings and 0 errors. `ServiceLib.Tests`: 233/233. `v2rayN.Tests`: 73/73. `git diff --check`: exit 0. Independent review found no P0-P3 issue.
- Public package marker: product `QuietControlCenter`, platform `win-x64`, version `7.24.4.18`; 35/35 immutable payload hashes matched, with 36 ZIP entries and no forbidden/private runtime entries. ZIP SHA-256: `AF702A61B80D2FC6696434BC4861809AA6DBA78362A86BEEF0080FCE17D0D72C`; size 190,873,187 bytes.
- The public ZIP contains no `guiConfigs`, database, log, key, subscription or local-continuity data. The separate local continuity build preserves 2 subscription records and 174 profiles through a SQLite backup whose integrity result is `ok`; no subscription values were inspected or printed.
- Process isolation: the task did not stop, restart, overwrite or signal Mika PID 85116 or its core. The user-controlled Mika process independently replaced sing-box PID 87832 with PID 72228 during final verification; the new `7.24.4.18` executable was not launched.

P0: none

P1: none

P2: none

P3: the selected core is resolved at runtime and is not yet shown as a separate per-node table column.

final result: passed

## Latest gate — profile traffic layout — 2026-08-05

Automated tests and the Release build pass, but the current administrator-elevation boundary prevents a same-state rendered screenshot. The latest visual gate for the profile-traffic layout therefore remains blocked; no running client or proxy-core process was interrupted for capture.

final result: blocked

## Latest gate — active-node traffic card — 2026-08-06

The detailed same-state comparison is recorded in `Active-node traffic card — 2026-08-06` above. The isolated 1120x720 WPF capture and focused crop show both traffic rows fully visible in the new rounded card, with no P0/P1/P2 differences. Build and all 316 automated tests passed, and the existing `玄同` and `sing-box` processes were unchanged.

final result: passed
