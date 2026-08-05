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
