# Snapzy for Windows v1.0.0 - Acceptance Results

Date: 2026-07-27. Environment: Windows 10 Enterprise 10.0.19045, 1920x1080 at 150% scaling,
corporate EDR, Chinese IME active. Verification was automated where possible (input
injection + pixel-level checks of produced files); items needing a human eye are
listed at the end.

## PRD Section 9 checklist

### 1. F1-F8 functional requirements

| Req | Status | Evidence |
|---|---|---|
| F1.1 Fullscreen capture | PASS | `Ctrl+Shift+3` produced 1920x1080 PNG (current monitor), indexed in history.json |
| F1.2 Area capture | PASS | Overlay drag 600x450 produced exact 600x450 PNG; arrow-key nudge moved selection by exactly 1px/press; Enter confirms; Esc cancels |
| F1.3 Window capture | PASS | Click-without-drag on Notepad returned its HWND + DWM frame bounds (460,300 1180x740, shadow excluded) |
| F1.4 Area + annotate | PASS | `Ctrl+Shift+7` opened the editor directly on the captured area |
| F1.5 Output formats | PASS (png), PASS (jpg/webp by unit test + code path) | PNG magic-byte verified; jpg/webp share the tested `ImageSaver` chokepoint |
| F2.1 MP4 recording | PASS | Real ffmpeg ddagrab, h264/yuv420p 800x600@30fps; odd dimensions auto-rounded even |
| F2.2 GIF export | PASS | Two-pass palette GIF produced alongside MP4 ("both" mode) |
| F2.3 Mic audio | PARTIAL | dshow device enumeration parser unit-tested; live audio capture needs a human check (no mic assumption made) |
| F2.4 Pause/HUD | PASS (pause), VISUAL (HUD) | Pause/resume produced 2 segments concat'ed into one seamless 11.3s MP4 excluding the pause gap; HUD layout needs a human look |
| F2.5 Cursor toggle | PASS | draw_mouse flag verified in ffmpeg args (unit test) |
| F3 Annotation editor | PASS | Pixel-verified in saved output: arrow (295 px), blur (754 px changed), counter badges (241 red px), crop (600x450 -> 199x150); undo removed the arrow completely; untouched pixels round-trip byte-identical |
| F4 Quick Access panel | PASS | Appeared 280x210 at bottom-right (16px margins), auto-dismissed after the 8s timeout; drag-out/button visuals need a human check |
| F5 History | PASS (store), VISUAL (window) | Store add/list/delete/retention unit-tested; window compiled + wired, visuals need a human look |
| F6 Hotkeys | PASS | All defaults dispatch globally; conflict detection verified by pre-registering Ctrl+Shift+3 externally -> warning logged/ballooned |
| F7 Tray/settings/localization | PASS (mechanics), VISUAL (menus) | Tray menu built from resx; zh-CN satellite ships and switches (unit-tested); settings persistence round-trips |
| F8 Portability | PASS | State beside exe only; `reg query HKCU /f Snapzy` -> 0 matches; read-only guard in code; copy-run proof below |

### 2. Unit tests

PASS - 36/36 (`dotnet test`): settings round-trip + corrupt-file recovery, history
store + retention, hotkey parse/normalize/conflicts, ffmpeg args (ddagrab/gdigrab/
concat/gif/dshow), capture + PNG magic bytes, localization en/zh-CN, recording
controller state machine (fake ffmpeg: segments, quit, concat list, ddagrab->gdigrab
retry, gif mode), dshow device parser.

### 3. End-to-end smoke

PASS - hotkey area capture -> editor (arrow + text box + blur) -> save verified by
pixel diff; recording with one pause -> single seamless MP4 + GIF, both probed with
ffmpeg (h264 yuv420p / gif 15fps). Copy-to-clipboard verified (600x450 image
readable from another process).

### 4. Portable proof

PASS - published folder launched from a second location created `portable.json`,
`Captures/`, `logs/` in that location; fullscreen capture landed in that folder's
`Captures/`; folder deleted cleanly afterwards; no registry keys, no Startup
shortcut (option off).

### 5. Both languages

PASS (mechanics) - all UI strings resolve through resx; zh-CN satellite assembly
ships in the package and the language switch is unit-tested. Full visual sweep in
Chinese is on the human checklist.

## Deviations from PRD targets

- **Package size**: unzipped folder is ~273 MB vs the 180 MB target. The target
  assumed a ~25 MB "LGPL essentials" ffmpeg; that assumption is stale - current
  static ffmpeg builds are 100-120 MB, and a build with libx264 (required for MP4)
  is GPL, not LGPL. The zip is 109 MB. Path to target: a custom minimal ffmpeg
  build (ddagrab/gdigrab + libx264 + aac + gif + libwebp only).
- **Clean-VM run**: not available in this environment; the self-contained publish
  plus no-registry proof are the strongest available substitutes.

## Remaining human checklist (visual polish)

- Tray menu rendering in both languages; balloon texts
- Quick Access drag-out into Explorer; all six buttons
- Recording HUD placement/timer; pause button; mic audio in the MP4
- History window cards/filters/search
- Settings tabs layout; hotkey rebinding by keyboard; launch-at-login shortcut
- SmartScreen flow on another machine
