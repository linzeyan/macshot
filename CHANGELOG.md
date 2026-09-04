# Changelog

macshot for Windows. The macOS app's changelog is on the `main` branch — the two ship
separately and their version numbers are not related.

## [0.8.0] - 2026-09-05

### Added

- **macshot installs its own updates.** The Check for Updates item used to open the
  downloads page and leave the rest to the user. It now asks once, fetches the right build
  for this installation — the matching architecture, and the offline build for an offline
  one — shows how far the download has got, puts it in place of the running one and starts
  it again. Where it cannot, it says why and still opens the page: an MSIX is Windows's to
  replace, and a macshot in Program Files would need administrator rights that a
  notification-area app has no business asking for.

### Fixed

- **A recording could end with a warning and a file of nothing in it.** macshot asked
  Windows for a video sample and then waited silently for the screen to change; Windows
  gives a still screen no frames at all, so the wait had no end and the encoder gave up
  having written nothing. Measured on a quiet desktop, nine sample requests in ten arrived
  with nothing new to show. A still screen now repeats the frame it is still showing,
  which is what a recording of it is, and a recording that does fail says what went wrong
  and takes the empty file with it rather than leaving it to be found later.
- **Recording held three copies of every frame** where one will do, and each was newly
  allocated — a minute of 30fps was eighteen hundred allocations of the size of the screen.
- **Editing a large capture grew without limit.** Every crop, flip or rotate kept the
  whole picture it replaced so it could be undone, and nothing ever let one go: a dozen
  operations on a 4K screenshot was most of a gigabyte the app would not give back. The
  history is now held to a memory budget rather than to no limit — six hundred steps at an
  ordinary capture size, fifteen at 4K, and never fewer than the one that would be undone
  next.

## [0.7.0] - 2026-09-04

### Added

- **AVIF** as a save format, which closes the last gap against the macOS app's format
  list. Windows writes no AVIF of its own — the Store's AV1 extension is a decoder — so
  it is encoded by `windows/native/macshot-avif`, a Rust library over `ravif` that is
  built from source as part of the app. Like HEIC and WebP it is probed for before it is
  offered, and falls back to JPEG if the encoder is missing or refuses a picture.
- **Captions can be styled** — face, size, weight, colour, what sits behind them and a rim
  round the glyphs — and a new one starts in the style the last one was left in, so a
  recording captioned throughout is styled once.
- **A zoom's subject is chosen by dragging it on the picture** rather than by numbers: the
  rectangle on the preview is what the zoom magnifies, moved and resized where it can be
  seen.
- **The MP4 export says how far it has got.** A long export used to give no sign it was
  running.
- **Auto-adjust selection**, on `S`: the region snaps out to the edges already in the
  picture under it — a window, a panel, a dialog — instead of being dragged to them.
- **Undo and Redo can be put on other keys**, alongside the tool shortcuts that already
  could be.
- **Ctrl+W closes any window with a title bar** — the editor, preferences, history and the
  rest.
- **Every control in preferences has a name a screen reader can announce.** The rows were
  labelled for the eye only, so a reader arriving at one was told what it was bound to and
  nothing about which command that is.
- **The webcam overlay is sized by dragging it**, and the Beautify switch now comes first
  in its row, which is where macshot puts it.

### Fixed

- **Exporting a recording that has sound, with anything on the effects band except a speed
  change, failed instead of exporting.** The recording's own file was being laid back
  beside the rendered frames as a background audio track, which Windows refuses outright
  for a file with video in it — so a zoom, a censor, a caption or a cut on a recording with
  audio threw. The track is now lifted out of the recording first, whichever way the sound
  is going back on.
- **A recording's sound was dropped from every export that had no speed change on it.**
  Only the re-timing path carried the audio back; a plain trim, a zoom or a caption
  exported silence.
- **A GIF exported from the editor ran at the wrong speed.** The frame rate the picker
  offered was rounded to a whole number of hundredths per frame, so 15 a second was
  written as 14.3.
- **The colour sampler picked the pixel under the annotations rather than the one on
  screen.** It now reads the canvas as composited, which is what the pointer is over.
- **Escape during a scroll capture finished it instead of abandoning it**, leaving a
  stitched image nobody asked for.
- **Beautify, Adjust and Invert showed the wrong thing when on.** The first two now tint
  their icon gold as macshot does, and Invert shows nothing at all — the turned picture is
  what says it worked.
- **The effect rectangle stayed on the picture after a recording's format was switched to
  GIF**, with the band that owns it gone and no way left to move it, deselect it or delete
  it.
- **The timeline clock stopped at whole seconds**, so it could not say where a trim handle
  had been put — the handles move in tenths.
- **The shortcut recorder spoke English whatever the machine was set to.** Its four
  messages are translated now.
- **macshot asked the system in the shell's language rather than its own**, so a machine
  running in one language with macshot set to another got answers in the wrong one.
- **The update check ran after the first window had opened.** It now runs before any of
  them, which is what makes an update offered on launch reachable.

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
