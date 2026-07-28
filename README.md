# Snapzy for Windows

A portable Windows port of [Snapzy](https://github.com/inject-cell/Snapzy) - screenshot,
screen recording, OCR and annotation from the system tray. No installer, no admin
rights, no .NET runtime required.

## Quick start

1. Extract `Snapzy-Windows-v1.1.0-portable.zip` anywhere (do not run from inside the zip).
2. Run `Snapzy.exe`. The icon appears in the system tray.
3. Press `Ctrl+Shift+4` and drag to take your first screenshot.

> **SmartScreen note**: the exe is unsigned. If Windows shows "Windows protected your PC",
> click **More info -> Run anyway**.

## Default hotkeys

| Action | Hotkey |
|---|---|
| OCR capture (copy text) | `Ctrl+Shift+2` |
| Fullscreen screenshot | `Ctrl+Shift+3` |
| Area / window screenshot | `Ctrl+Shift+4` |
| Area screenshot + annotate | `Ctrl+Shift+7` |
| Record start / stop | `Ctrl+Shift+5` |
| Open annotation editor | `Ctrl+Shift+A` |
| Capture history | `Ctrl+Shift+H` |

All hotkeys can be changed in Settings (tray menu -> Settings -> Hotkeys).
The tray menu shows each action's current hotkey next to its label (v1.3).

In the selection overlay: drag to select, `A` toggles window mode, click a window
to snap to it, arrow keys nudge (Shift+arrows resize), `Enter` confirms, `Esc` cancels.

In the annotation editor: `V` select, `R` rectangle, `O` ellipse, `L` line, `A` arrow,
`P` pen, `T` text, `B` blur, `X` pixelate, `N` counter, `C` crop; `Ctrl+Z`/`Ctrl+Y`
undo/redo, `Ctrl+wheel` zoom, space-drag pan, `Ctrl+S` save, `Ctrl+C` copy.

Line width (which also scales annotation text size) can be typed freely
(1-64) in the width box, and the color popup's **Custom...** opens the full
system color picker (v1.5).

**Add Image / stitching (v1.4)**: the editor's **Add Image...** button inserts more
screenshots onto the canvas - stitched to the right, stitched below (the canvas
grows automatically), or floating on top. Inserted images can be moved with the
Select tool and deleted; everything is undoable. Use it to build side-by-side
comparison images; uncovered canvas area exports as white.

## Portability

Everything lives beside `Snapzy.exe`:

- `portable.json` - settings
- `Captures/` - screenshots, recordings and the history index
- `logs/` - application and ffmpeg logs

No registry writes, nothing in `%APPDATA%`. Deleting the folder removes Snapzy
completely. The single optional exception: enabling **Launch at login** in Settings
creates `Snapzy.lnk` in your Startup folder (removed when you disable the option).

## OCR (v1.1)

`Ctrl+Shift+2` selects a region (or window) and copies its text to the clipboard
using the in-box Windows OCR - no cloud, works offline. When the Chinese OCR
language pack is installed Snapzy uses it for everything (it reads both Chinese
and Latin/digits; the English engine cannot read Chinese), with an automatic
English retry for stubborn Latin-only table cells.
Old screenshots can be OCR'd from the history window's right-click menu.

**Table recognition (v1.2)**: when the image contains a table (spreadsheet
screenshots, reports), Snapzy detects the rows and columns from the text layout
and copies tab-separated cells - paste straight into Excel/WPS and each value
lands in its own cell. The history right-click menu offers both modes
explicitly: **Copy text (OCR)** always copies plain lines, **Copy table (OCR)**
always copies rows/cells. The `Ctrl+Shift+2` hotkey auto-detects.

**Copy table (OCR)** has a dedicated engine for ruled tables (v1.2.2): when the
image contains drawn cell borders, Snapzy detects the grid lines, crops each
cell's content and recognizes it cell by cell - this also handles cells with a
single character, which whole-image Windows OCR cannot see at all. Borderless
layouts use text-position clustering instead. When nothing is recognized the
clipboard is left unchanged and a "No text found" balloon is shown.
Single letters without word context can still be misread occasionally
(e.g. g/a, j/i at small sizes) - capturing the table at a higher zoom level
improves accuracy.

## Scrolling capture (v1.1)

Tray menu -> Scrolling Capture, then click the target window in the overlay.
Snapzy scrolls the window and stitches the frames into one tall image, starting
from the current scroll position. Works with windows that honor mouse-wheel
messages (browsers, editors, file lists); pages with sticky headers or animated
content may stop the stitch early - the partial result is kept.

## Recording

Recording uses the bundled `ffmpeg/ffmpeg.exe` (BtbN ffmpeg build, GPL, includes
ddagrab/libx264). MP4 (H.264) by default; GIF, animated WebP, or MP4+GIF /
MP4+WebP via Settings -> Recording. Pause/resume produces seamless output.
Optional microphone audio via DirectShow, and (v1.1) system audio ("what you
hear") via WASAPI loopback - enable it in Settings -> Recording. Audio is
start-aligned with the video; no drift compensation is applied.

If your machine cannot use Desktop Duplication (some remote sessions), Snapzy
automatically falls back to GDI capture.

**ffmpeg licensing**: ffmpeg is bundled as a separate, unmodified executable invoked
as a command-line tool; it is licensed under the GPL (this build includes libx264).
Replace `ffmpeg/ffmpeg.exe` with your own build if you need different licensing;
any build with `ddagrab` or `gdigrab` plus `libx264` works.

## Languages

English and 简体中文 (Settings -> General -> Language; applies immediately to the
tray menu, other windows use the new language when reopened).

## Known limitations

- CET shadow-stack enforcement is disabled for the Snapzy process (v1.2.5): with
  it on (the .NET 9+ default), the Open/Save file dialog hard-crashes on
  Windows 11 machines that have CET-incompatible shell extensions installed
  (dotnet/wpf#10305). `publish.ps1` verifies the flag stays off.

- Mixed-DPI multi-monitor setups: the selection overlay uses the primary monitor's
  scale for its visuals; captured pixels are always correct.
- Theme setting affects Snapzy's own windows only.
- Scrolling capture: requires the window to honor posted wheel messages; captures
  content from the current scroll position downward; window chrome (scrollbars)
  is trimmed automatically, sticky in-page headers are not.
- OCR of very tall images is processed in 2000px slices with a 40px overlap; an
  occasional duplicated line at a slice boundary is possible.
- Folder size is ~300 MB unpacked, dominated by the bundled full ffmpeg build
  (~120 MB) and the self-contained .NET runtime; a trimmed ffmpeg build would
  reduce this substantially.

## Third-party components

- ffmpeg (bundled exe, GPL) - screen/GIF/WebP encoding
- [NAudio](https://github.com/naudio/NAudio) (MIT) - WASAPI loopback capture

## Uninstall

Delete the folder. If you enabled Launch at login, also remove `Snapzy.lnk` from
`shell:startup` (or disable the option in Settings first).

## Building from source

```powershell
cd windows
dotnet test          # unit tests (Snapzy.Core)
.\publish.ps1        # produces publish\Snapzy + the portable zip
```

Requires the .NET 10 SDK. `publish.ps1` copies `C:\Windows\system32\ffmpeg.exe`
into the package; point it at another ffmpeg if yours lives elsewhere.
