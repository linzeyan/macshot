# Changelog

macshot for Windows. The macOS app's changelog is on the `main` branch — the two ship
separately and their version numbers are not related.

## [1.0.0-beta.1] - 2026-08-03

The first Windows release: a port of macshot to C# and WinUI 3, following the macOS app
rather than reinterpreting it — same layout, same defaults, same wording.

### Added

- **Capture** — region select with eight resize handles, full screen, window snapping,
  multi-monitor, delay capture, remembered last selection.
- **19 annotation tools** — pencil, line, arrow, rectangle, filled rectangle, ellipse,
  marker, text, number, stamp, pixelate, blur, measure, loupe, select and edit, translate
  overlay, crop, colour sampler, spotlight.
- **Screen recording** — MP4 and GIF, area or full screen, system audio and microphone,
  mouse click highlighting, and a video editor for trimming and exporting.
- **Scroll capture** — automatic scroll detection and stitching, with a live preview.
- **Censor** — pixelate, blur, solid and erase, plus one-click redaction of all text, of
  detected PII, of faces, and of people.
- **OCR** — the Windows OCR engine, with translation and QR/barcode reading.
- **Output** — copy, save as PNG/JPEG/WebP, pin, beautify, background removal, and upload
  to imgbb, Google Drive or any S3-compatible service.
- **Editor window** — a standalone resizable editor with the full tool set and 0.1x–8x
  zoom.
- **History** — recent captures with editable annotations, in the tray menu and a
  drop-down panel.
- **40 languages**, shared with the macOS app and auto-detected from the system.
- **Offline build** — the same app with every upload and translation feature compiled
  out, so the binary contains no network code at all.
- **Update check** — reads GitHub releases and offers only the matching variant and
  architecture.
