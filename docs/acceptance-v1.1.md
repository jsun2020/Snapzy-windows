# Snapzy for Windows v1.1.0 - Acceptance Results

Date: 2026-07-27. Environment: Windows 10 Enterprise 10.0.19045, 1920x1080 at 150% scaling,
corporate EDR, Chinese IME. Same verification approach as v1.0: automated and
pixel/stream-level wherever possible.

## v1.1 features

| Feature | Status | Evidence |
|---|---|---|
| OCR service (Windows.Media.Ocr) | PASS | Rendered "HELLO SNAPZY 123" recognized verbatim; blank image returns empty; 6300px-tall image processed via 2000px slices |
| OCR capture hotkey (`Ctrl+Shift+2`) | PASS (wiring), VISUAL (flow) | Default binding ships (7 hotkeys, forward-compat TryAdd for v1.0 settings); overlay-select -> clipboard flow reuses verified paths |
| OCR from history (right-click) | PASS (wiring) | Context-menu item on image entries -> RecognizeFileAsync -> clipboard |
| Scrolling capture | PASS | Notepad with 300 lines: 25 wheel steps stitched into 850x6328 px; height arithmetic exact (478 + 25x234); OCR of a middle band shows LINE 126..134 sequential - no seam duplication or loss |
| Scrolling capture UI | PASS (wiring), VISUAL (progress window) | Tray item, window-mode overlay, progress+cancel window, post-actions |
| System audio recording | PASS | 8.4s recording with pause/resume on a silent system: muxed MP4 carries `Audio: aac 48000 Hz stereo`; 2 wav segments concat'ed; silence-rendering keepalive makes loopback continuous |
| Animated WebP export | PASS | `mp4+webp` produced a 4.3 MB RIFF/WEBP file with ANIM+ANMF chunks (animated); `webp`-only mode deletes the mp4 (unit-tested) |
| Output mode matrix | PASS | mp4 / gif / webp / mp4+gif / mp4+webp unit-tested at the controller level |

## Bugs found and fixed during verification

1. **Stitcher false "no movement"**: the bottom scrollbar band is static furniture;
   a bottom-anchored probe always matched itself. Fixed with identical-suffix
   detection + regression test.
2. **Vertical scrollbar broke overlap matching**: its arrow/thumb chrome is a
   static right-edge column inside scrolling rows. Fixed by excluding the
   scrollbar column from the capture region.
3. **Silent system = empty loopback wav**: WASAPI loopback delivers no data when
   nothing plays; `-shortest` then truncated the mux to an empty file which
   overwrote the good video. Fixed with a silence-rendering keepalive plus a
   minimum-size guard on every ffmpeg output (empty containers now count as
   failures and never replace good files).

## Regression

- Full unit suite: 51/51 PASS (2 OCR tests assert only when an OCR language is
  installed - it is on this machine, and they ran for real).
- Published build (v1.1.0, self-contained + ffmpeg): launches portable, fullscreen
  capture via hotkey path verified, state beside exe, no registry keys.
- Package: `Snapzy-Windows-v1.1.0-portable.zip` 115.3 MB; folder 296.9 MB
  (same documented deviation as v1.0: full ffmpeg build + .NET runtime; +24 MB
  vs v1.0 from WinRT projections and NAudio).

## Remaining human checklist

- Scrolling capture on a real browser page (engine verified on Notepad; browsers
  honor wheel messages but sticky headers stop the stitch early by design)
- OCR accuracy on real Chinese screenshots (engine follows installed language packs)
- System-audio recording while music plays (verified silent-path; audible-path
  produces the same pipeline with real samples) - listen to the result
- Settings Recording tab: new checkbox and 5-entry output combo rendering
