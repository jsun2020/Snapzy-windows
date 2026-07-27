# Snapzy for Windows

A portable Windows port of [Snapzy](https://github.com/inject-cell/Snapzy) - screenshot,
screen recording and annotation from the system tray. No installer, no admin rights,
no .NET runtime required.

## Quick start

1. Extract `Snapzy-Windows-v1.0.0-portable.zip` anywhere (do not run from inside the zip).
2. Run `Snapzy.exe`. The icon appears in the system tray.
3. Press `Ctrl+Shift+4` and drag to take your first screenshot.

> **SmartScreen note**: the exe is unsigned. If Windows shows "Windows protected your PC",
> click **More info -> Run anyway**.

## Default hotkeys

| Action | Hotkey |
|---|---|
| Fullscreen screenshot | `Ctrl+Shift+3` |
| Area / window screenshot | `Ctrl+Shift+4` |
| Area screenshot + annotate | `Ctrl+Shift+7` |
| Record start / stop | `Ctrl+Shift+5` |
| Open annotation editor | `Ctrl+Shift+A` |
| Capture history | `Ctrl+Shift+H` |

All hotkeys can be changed in Settings (tray menu -> Settings -> Hotkeys).

In the selection overlay: drag to select, `A` toggles window mode, click a window
to snap to it, arrow keys nudge (Shift+arrows resize), `Enter` confirms, `Esc` cancels.

In the annotation editor: `V` select, `R` rectangle, `O` ellipse, `L` line, `A` arrow,
`P` pen, `T` text, `B` blur, `X` pixelate, `N` counter, `C` crop; `Ctrl+Z`/`Ctrl+Y`
undo/redo, `Ctrl+wheel` zoom, space-drag pan, `Ctrl+S` save, `Ctrl+C` copy.

## Portability

Everything lives beside `Snapzy.exe`:

- `portable.json` - settings
- `Captures/` - screenshots, recordings and the history index
- `logs/` - application and ffmpeg logs

No registry writes, nothing in `%APPDATA%`. Deleting the folder removes Snapzy
completely. The single optional exception: enabling **Launch at login** in Settings
creates `Snapzy.lnk` in your Startup folder (removed when you disable the option).

## Recording

Recording uses the bundled `ffmpeg/ffmpeg.exe` (BtbN ffmpeg build, GPL, includes
ddagrab/libx264). MP4 (H.264) by default; GIF or both via Settings -> Recording.
Pause/resume produces seamless output. Optional microphone audio via DirectShow.

If your machine cannot use Desktop Duplication (some remote sessions), Snapzy
automatically falls back to GDI capture.

**ffmpeg licensing**: ffmpeg is bundled as a separate, unmodified executable invoked
as a command-line tool; it is licensed under the GPL (this build includes libx264).
Replace `ffmpeg/ffmpeg.exe` with your own build if you need different licensing;
any build with `ddagrab` or `gdigrab` plus `libx264` works.

## Languages

English and 简体中文 (Settings -> General -> Language; applies immediately to the
tray menu, other windows use the new language when reopened).

## Known limitations (v1.0)

- Mixed-DPI multi-monitor setups: the selection overlay uses the primary monitor's
  scale for its visuals; captured pixels are always correct.
- Theme setting affects Snapzy's own windows only.
- Folder size is ~270 MB unpacked, dominated by the bundled full ffmpeg build
  (~120 MB) and the self-contained .NET runtime; a trimmed ffmpeg build would
  reduce this substantially.

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
