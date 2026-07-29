# Quiet Control Center design QA

## Visual source of truth

- Reference: `C:\Users\ADMINI~1\AppData\Local\Temp\codex-clipboard-b51a56d2-1f5b-4dec-9020-8fa47bb6d75f.png`
- Reference dimensions: 1487 x 1058 px.
- Final connected fixture: `.codex/state/runs/qcc-20260729-03/implement/qcc-connected-ultimate-1487x1058.png`
- Final empty fixture: `.codex/state/runs/qcc-20260729-03/implement/qcc-empty-ultimate-1487x1058.png`
- Final responsive fixture: `.codex/state/runs/qcc-20260729-03/implement/qcc-responsive-ultimate-1120x720.png`
- Full side-by-side comparison: `.codex/state/runs/qcc-20260729-03/implement/comparison-reference-vs-verified.png` (2974 x 1058 px).

Captures were produced from disposable copies of the published application at
150% system scale, then normalized to the stated 96-DPI logical target. The
fixture database and local loopback server live only under the run evidence
directory and are not included in `artifacts/qcc-win-x64`.

## Comparison history

1. `qcc-20260729-02`: first native implementation; empty table had no guidance,
   the 1120 x 720 table truncated headers, inspector actions fell below the
   fold, status copy contradicted itself, and official GUI replacement remained
   reachable.
2. `qcc-20260729-03` intermediate: added empty state, fixed inspector action
   placement and narrow columns, then corrected fixture Unicode, selected-row
   binding, and speed/delay units.
3. `qcc-20260729-03` final: build/test/publish passed; wide connected, empty,
   and responsive captures were inspected at original resolution.

## Explicit checks

| Area | Result | Evidence / notes |
| --- | --- | --- |
| Typography | Pass | Chinese labels remain legible; hierarchy follows the reference with a strong hero title, detail headings, and restrained supporting copy. |
| Spacing and layout | Pass | Left navigation, hero, node table, fixed inspector actions, five-part controls, and status strip preserve the reference hierarchy. |
| Colors and semantic state | Pass | Blue primary, pale selection, green/orange/red delay semantics, and neutral disconnected state are consistent. |
| Assets and icons | Pass | Material Design vector icons are consistent and scale cleanly; no missing raster asset is visible. |
| Copy and content | Pass | “检查内核更新” states the safe scope; startup status uses one authoritative “未连接”; delay and speed include `ms` and `MB/s`. |
| Empty state | Pass | Empty capture gives an explanation and a working “添加订阅” action. |
| 1120 x 720 | Pass | Secondary table columns collapse; remaining headers are readable and all six inspector actions plus advanced settings are immediately discoverable. |
| Long text | Pass | Address and status fields trim or scroll without overlapping adjacent controls. |
| Connected-state parity | Pass | A disposable local VLESS server at `127.0.0.1:24443`, availability endpoint at `127.0.0.1:24444`, and client SOCKS inbound at `127.0.0.1:20808` produced a real core connection. The fixture-owned `xray.exe` listened on 20808, while the UI showed node “香港 01”, badge “已连接”, and the same 7 ms core state in the hero and status bar. |

## Runtime and cleanup evidence

- The local VLESS/HTTP/client loopback was a real end-to-end runtime fixture;
  no production-only fake status hook was added.
- Final cleanup found zero processes whose executable path was under the
  disposable fixture and zero listeners on ports 20808, 24443, and 24444.
- The user's pre-existing administrator instance PID 58048 remained alive and
  was never touched.

`final result: passed`
