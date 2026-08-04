# macshot for Windows

<p align="center">
  <img src="assets/logo.svg" alt="macshot logo" width="200"/>
</p>

<p align="center">
  <b>A port of macshot, the most feature-rich open-source screenshot tool on macOS.</b><br>
  <br>
  19+ annotation tools, screen recording with a full video editor, OCR + translation,<br>
  auto-redact PII, scroll capture, beautify — native WinUI 3, no Electron, all free.
</p>

<p align="center">
  <a href="https://github.com/linzeyan/macshot/releases/latest">Download</a> · <a href="CHANGELOG.md">Changelog</a> · <a href="PRIVACY.md">Privacy</a> · <a href="SECURITY.md">Security</a>
</p>

<p align="center">
  <i>The macOS original is <a href="https://github.com/sw33tLie/macshot">sw33tLie/macshot</a>, and lives on this repository's <code>main</code> branch.</i>
</p>

---

### Why macshot?

- **Capture & annotate in one flow** — select a region, draw arrows/text/shapes/blur, copy to clipboard. One hotkey, zero friction.
- **Screen recording with built-in editor** — record any area or full screen as MP4/GIF with system audio + microphone. Audio merge dialog with per-track volume control. Trim and export without leaving the app.
- **Scroll capture** — select a region and scroll. macshot stitches it into one seamless tall (or wide) image automatically.
- **Upload anywhere** — one-click upload to Google Drive, imgbb, or any S3-compatible service (Cloudflare R2, AWS S3, MinIO, etc.). Link copied to clipboard instantly.
- **Lightweight & native** — lives in your notification area. Built with C# and WinUI 3, not a web browser in disguise.
- **40 languages** — English, 中文, 日本語, 한국어, Deutsch, Français, Español, Italiano, Português, العربية, हिन्दी, and 29 more, shared with the macOS app. Auto-detects your system language.

---

## Install

Download the `.zip` for your machine from [Releases](https://github.com/linzeyan/macshot/releases), unpack it anywhere, and run `macshot.exe`.

| Build | For |
|---|---|
| `macshot-<version>-win-x64.zip` | Intel and AMD PCs |
| `macshot-<version>-win-arm64.zip` | Snapdragon / ARM PCs, and Windows on Apple silicon |
| `macshot-Offline-…` | The same app with every upload and translation feature compiled out — no network code in the binary at all |

Self-contained: no .NET runtime to install.

---

## Quick Start

1. Launch macshot — it appears in your notification area
2. Press `Ctrl+Shift+X` to capture
3. Drag to select, annotate with the toolbar, press `Ctrl+C` to copy
4. Press `Esc` to cancel

---

<details>
<summary><b>All Features</b></summary>

### Capture
- **Instant capture** — global hotkey freezes your screen, select any region
- **Window snap** — hover over a window and click to capture it exactly; `Tab` toggles snap, `F` for full screen
- **Resolution & aspect presets** — set an exact pixel size or lock an aspect ratio (1:1, 4:3, 16:9, 9:16, …) before or after selecting; editable width/height fields
- **Boundary snap** — selection edges snap to strong color edges (UI lines, window borders) while dragging or resizing; hold `Option` to bypass
- **Scroll capture** — auto-detects vertical or horizontal scrolling, stitches by matching overlap, live preview panel beside the capture region
- **Capture delay** — 3/5/10/30 second countdown before capture, set from the tray menu. Escape to cancel.
- **Multi-monitor** — captures all screens simultaneously; drag a selection across screens for a stitched image
- **Quick save** — `Ctrl+Shift+S` to select and save/copy instantly without annotation. Enter key also saves/copies based on preference.
- **Quick OCR** — `Ctrl+Shift+T` to select and extract text instantly

### Annotation Tools
- **Arrow** — 5 styles: single, thick/banner, double, open, tail; flip direction toggle; right-click to add anchor points for complex curves
- **Shapes** — rectangle and ellipse with 3 fill modes (stroke, stroke+fill, fill), corner radius slider
- **Text** — rich formatting (bold/italic/underline/strikethrough), resizable text box, left/center/right alignment, background fill & outline colors, click to re-edit
- **Pencil & Marker** — freeform drawing with optional smoothing; smart marker mode snaps to text lines via OCR
- **Numbered markers** — auto-incrementing (1/I/A/a formats), with optional pointer cone
- **Stamp / Emoji** — 21 quick emojis, 100+ in categorized picker, or load any image
- **Censor (Pixelate / Blur / Solid / Erase)** — unified redaction tool with 4 modes: pixelate, Gaussian blur, solid color fill, or smart erase that samples surrounding colors for invisible content removal. Auto-redact PII (emails, phones, credit cards, SSNs, API keys), auto-detect faces and people, or draw in "Text Only" mode to censor just the text in a region
- **Measure** — pixel ruler with px/pt toggle; hold `1` or `2` for auto-measure
- **Loupe** — 2x magnifier
- **Highlight (spotlight)** — drag a region to keep it bright while dimming the rest; adjustable dim strength and solid/dashed border
- **Color sampler** — eyedropper to pick any color; right-click to copy hex; auto-saves to custom palette slots
- **Space to reposition** — hold Space while drawing to move the shape without changing its size
- **Rotation** — rotate shapes via handle, Shift for 90° snaps
- **Click-to-select** — click any annotation to select it, then edit properties (stroke, style, fill), drag to move, resize via handles, rotate, or delete — all without switching tools

### Screen Recording
- **MP4 (H.264)** up to 120fps or **GIF** (5/10/15fps)
- **System audio capture** — toggle on/off, excludes macshot's own sounds
- **Microphone recording** — record voice narration alongside screen capture (permission requested on first use)
- **Mouse click highlights** — visual ripple on clicks during recording
- **Selection border** — visible capture region outline during recording
- **Tray stop button** — stop recording from the notification-area icon
- **Quick settings popover** — change format, FPS, and post-recording action on the fly without opening Preferences
- **Video editor** — trim timeline, mute/strip audio, play/pause, save (with Save As), upload, reveal in Finder

### Output & Upload
- **Formats** — PNG, JPEG, and HEIC where Windows has the codec for it, with quality slider
- **Google Drive** — sign in once, uploads to a private "macshot" folder
- **imgbb** — anonymous image hosting with shareable links
- **S3-compatible** — upload to Cloudflare R2, AWS S3, MinIO, DigitalOcean Spaces, Backblaze B2, etc.
- **Retina downscale** — optional 1x export for smaller files
- **sRGB color profile** — optional embedding for cross-display consistency

### Editor Window
- Standalone resizable window with full annotation tools, beautify preview
- **Add Capture** — capture additional screen regions and compose them into a single image, drag to reposition
- **Paste image** — `Ctrl+V` drops a clipboard image into the canvas as a draggable layer
- Crop (with rule-of-thirds grid), flip H/V, zoom 0.1x–8x
- Top bar with pixel dimensions, zoom dropdown (presets, fit canvas, zoom in/out)

### Beautify
- 30 gradient styles including mesh gradients, adjustable padding, corner radius and shadow

### Image Effects (Adjust)
- Non-destructive CIFilter adjustments: Brightness, Contrast, Saturation, Sharpness
- 8 presets: Noir, Mono, Sepia, Chrome, Fade, Instant, Vivid
- Works independently or combined with Beautify
- Live preview in the overlay

### Other
- **OCR & QR** — extract text with the Windows OCR engine, auto-copy to clipboard, translate to 30+ languages, Google AI Search; also reads QR codes with open/copy/scan actions
- **Invert colors** — one-click color inversion, apply twice to revert
- **Background removal** — Windows AI image object extractor. Needs a Copilot+ PC; the button says so when the model is unavailable.
- **Pin to screen** — floating always-on-top window
- **Floating thumbnail** — auto-dismiss preview with Copy/Save/Pin/Edit/Upload
- **Screenshot history with editable annotations** — tray submenu + drop-down history panel (`Ctrl+Shift+H`). Re-open any capture in the editor with live annotations preserved — edit, then press Done to save back. Drag-and-drop and right-click actions
- **QR & barcode detection** — inline Open/Copy actions
- **Snap alignment guides** — annotations snap to midlines and edges
- **Custom tray icon** — use the default or a built-in preset
- **Update check** — reads GitHub releases and offers only the matching variant and architecture

</details>

<details>
<summary><b>Keyboard Shortcuts</b></summary>

**Global hotkeys** (configurable in Preferences)

| Shortcut | Action |
|---|---|
| `Ctrl+Shift+X` | Capture Area |
| `Ctrl+Shift+F` | Capture Full Screen |
| `Ctrl+Shift+S` | Quick Capture (instant save) |
| `Ctrl+Shift+T` | Capture OCR (instant text extraction) |
| `Ctrl+Shift+R` | Record Area |
| `Ctrl+Shift+H` | Show History Panel |

**General** (during capture)

| Shortcut | Action |
|---|---|
| `Enter` | Confirm (save or copy based on preference) |
| `Ctrl+C` | Copy to clipboard |
| `Ctrl+S` | Save to file |
| `Ctrl+Z` / `Ctrl+Shift+Z` | Undo / Redo |
| `Ctrl+0` | Reset zoom to 1x |
| `Esc` | Cancel / close popover |
| `Delete` | Remove selected annotation |
| `Tab` | Toggle window snap mode |
| `F` | Capture full screen (snap mode) |
| `Shift` (while drawing) | Constrain to straight lines / perfect shapes |
| `Space` (while drawing) | Reposition shape without changing size |
| `Right-click` on line/arrow | Add anchor point for multi-point curves |

**Tool shortcuts** (active after selecting a region — customizable in Preferences > Shortcuts)

| Key | Tool |
|---|---|
| `A` | Arrow |
| `L` | Line |
| `P` | Pencil |
| `M` | Marker |
| `R` | Rectangle |
| `O` | Ellipse |
| `T` | Text |
| `N` | Number |
| `B` | Censor (Pixelate/Blur) |
| `H` | Highlight (spotlight) |
| `I` | Color Sampler |
| `G` | Stamp / Emoji |
| `S` | Select & Edit |
| `E` | Open in Editor |

</details>

---

## Permissions

macshot captures through the Windows Graphics Capture API. Windows prompts on first
capture, and you can review it under Settings > Privacy & security > App permissions.

---

## Donations

Thanks for thinking about it, but macshot doesn't take donations. I make this in my free time and I'm happy to keep it that way, so there's no "buy me a coffee" or sponsorship link.

If you'd like to help out, starring the repo, reporting bugs, or contributing is more than enough. Thank you! 🙏

---

## Requirements

Windows 11 (build 22000) or later, x64 or arm64.

## Credits

macshot is [sw33tLie](https://github.com/sw33tLie)'s. This is a port of it to Windows,
and it follows the original rather than diverging from it — where the two disagree, the
Mac is right.

## License

[GPLv3](LICENSE)
