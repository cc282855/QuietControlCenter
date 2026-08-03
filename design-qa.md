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
