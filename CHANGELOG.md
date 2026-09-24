# Changelog

macshot for Windows. The macOS app's changelog is on the `main` branch — the two ship
separately and their version numbers are not related.

## [0.8.18] - 2026-09-24

### Fixed

- **Recording part of the screen no longer puts a yellow border round the whole display.**
  Windows draws that border round anything being captured, and for a screen or region
  recording it went round the entire display, however small the region. On Windows 11
  macshot now asks Windows to leave it off, for screenshots and recordings alike. Windows 10
  has no way to turn it off, so there a screen or region recording no longer uses Windows'
  capture at all and copies the screen instead: no border, at a lower frame rate — around
  twenty frames a second for a region, whatever rate is chosen. Recording a single window
  on Windows 10 still shows the border, round that window only, and a screenshot there can
  still show it for the instant it takes.

## [0.8.17] - 2026-09-23

### Fixed

- **Recording on some Windows 10 machines no longer falls back to copying the screen by
  hand.** The reason 0.8.16 started writing down arrived: on those machines Windows refused
  to start capturing the screen on every recording, with error `0x8001010E`, because macshot
  asked from a different thread than the one that had set the capture up. Windows 11 accepts
  that and Windows 10 does not. Every recording there was made from copies of the screen
  taken by hand, about twenty a second whatever frame rate was chosen. macshot now asks from
  the right thread.

- **Check for Updates now says what went wrong, in your language.** When GitHub could not
  be reached, the message was the raw error — "A task was canceled." in English, under a
  translated heading. It now says whether GitHub did not answer in time, could not be
  reached, or answered with an error, and offers to open the download page. A failed update
  download says the same.

### Changed

- **Updates download only what changed.** An update used to download the whole 77MB
  release, nearly all of which is the .NET runtime and the Windows App SDK, and those do not
  change from one release to the next: from 0.8.15 to 0.8.16, 9 of 521 files differed.
  macshot now fetches just those files out of the release and copies the rest from the
  copy it is already running from — 3MB instead of 77MB, which on the connection it was
  measured on took 34 seconds instead of three and a half minutes. If the download cannot
  be done that way, it downloads the whole release as before. This takes effect from the
  update *after* this one, since this release is downloaded by the version before it.

## [0.8.16] - 2026-09-22

### Fixed

- **Closing the video editor now gives its memory back.** Closing it let go of the player
  and the preview straight away, but the rest — tens of megabytes of working copies — sat
  there until the next screenshot was taken, because macshot allocates nothing while it is
  idle and so the cleanup it relies on never runs on its own. Measured on a test machine,
  closing the editor now returns 38MB that used to stay for the rest of the session.

- **A large recording could have stopped tidying its memory altogether.** The rule added in
  0.8.15 that decides when tidying is worth doing was set from a measurement that turned out
  to be on the small side: an ordinary recording taken after a screenshot in the same
  session is already most of the way to the line where macshot would have stopped. The line
  has been moved to five times the largest case ever measured.

### Added

- **The log now says why a recording captured nothing.** On some machines — virtual
  desktops especially — Windows refuses when macshot asks it to start capturing the screen.
  macshot fell back to copying the screen by hand, which works, but the refusal itself was
  thrown away: nothing was left to distinguish it from a screen that simply never changed,
  and the recording reported `0 frames` either way. The refusal is now written down with its
  error code. The same is true of a recording whose sound would not start.

- **The video editor writes down what it costs.** Opening and closing it are now in the log
  with what the recording was and what the process weighed either side, which is what the
  rest of macshot's windows have been doing since 0.8.14 and this one was not.

## [0.8.15] - 2026-09-21

### Fixed

- **A finished recording now gives its memory back the way a screenshot does.** Cleaning
  up after a recording deliberately left the large blocks it had been working in where
  they lay, on the strength of one measurement taken on a much older build. On a machine
  whose recordings are small — which includes every machine where the screen recorder
  receives no frames and macshot falls back to copying the screen by hand — that left
  around 21MB of unusable gaps behind each recording, and nothing reclaimed them until the
  next screenshot was taken. Whether to tidy up is now decided by how much memory is in
  use at that moment, which is the thing that actually made the difference.

### Added

- **The log says which renderer macshot was given.** A screenshot and a recording each ask
  Windows for a graphics device of their own, and on a machine with no usable GPU — a
  virtual desktop, a remote session — either can quietly fall back to rendering in
  software without the other doing so. That is now written down, and says which of the two
  it was. So is the moment a recording's capture actually starts, which was previously
  written just before the attempt rather than after it — on a machine that then recorded
  nothing, that line was answering the only useful question wrongly.

## [0.8.14] - 2026-09-20

### Fixed

- **Taking a screenshot no longer needs two extra copies of your screen while it is being
  taken.** The moment a capture is taken is when macshot is at its largest, and two of the
  copies it was making at that moment were producing pixels identical to the ones they had
  just been read from: one to assemble a single display into a picture the same size, and
  one on the way to showing it. On a 2038×1588 display that is 12.3MB each. Measured
  against the previous version over three captures, memory in use after each falls from
  156/187/190MB to 141/177/176MB.

- **A closed editor was holding on to every earlier version of the picture.** Cropping,
  flipping or removing a background keeps the picture it replaced so the change can be
  undone, up to 512MB of them. Closing the window let go of the current picture and left
  that history standing, and a closed window is never cleaned up on its own — so it stayed
  for the rest of the session. The scroll-capture preview panel kept its stitched page the
  same way.

### Added

- **macshot can now say where its memory went, in its own log.** Turn on detailed logging
  in Preferences and every capture writes down what one screen costs on your machine, what
  the process weighed before and after taking it, what was handed back, and how much of
  that a cleanup actually returned. This exists because memory reports come from machines
  we cannot measure — with the log there is no longer any guessing about whether a number
  in Task Manager is something being held or simply something not yet cleaned up.

## [0.8.13] - 2026-09-19

### Fixed

- **Every global shortcut was being taken away and registered again on every settings
  save** — including the save that happens as each capture is delivered, because macshot
  remembers the region you last chose. For the moment in between, all twelve were dead; if
  Windows refused one because another program holds it, you were told so again after every
  single capture. A shortcut is now left alone unless the change was about that shortcut.

- **A screenshot no longer leaves tens of megabytes behind for the rest of the session.**
  Nothing was leaking — six captures in a row settle at the same size, and a minute later
  the memory in use is back where it started — but the first capture pushed the process up
  and it never came back down. Three separate causes, all measured on a 2038x1588 display:

  - On a single display, the capture was being copied a second time to cut out a region
    that was already the whole picture. 12.9MB per capture, for nothing.
  - The holes that a capture's large buffers leave behind were never being closed up, so
    Windows kept 59MB reserved to hold 16MB of actual data. Closing them up after a
    capture — and only after a capture, never after a recording, where the same request
    was measured to be three times worse — takes that to 44MB with no holes at all.
  - The picture chosen as a beautify background was decoded and kept in full for the whole
    session even when the chosen background was a gradient, and both swatches that offer
    it were drawn from that full-sized copy rather than a thumbnail.

  Two minutes after a capture, macshot now settles at 148MB where it was 167MB. For anyone
  who has picked a beautify picture and since gone back to a gradient, idle memory falls
  from 66MB to 50MB.

  What is left is mostly not macshot's: about 31MB is what Windows' interface layer costs
  the first time any window is shown — opening the history panel alone costs it — and
  about 32MB is the graphics and software-rendering libraries, loaded once. Releasing the
  capture device between captures was tried and returned nothing at all.

## [0.8.12] - 2026-09-17

### Fixed

- **A recording on a machine Windows sends no frames to no longer takes hundreds of
  megabytes of memory with it.** Where macshot has to take the frames itself, each frame
  was two fresh buffers the size of the picture — 12.9MB apiece on a full 2038x1588
  display, twenty times a second — and no amount of collecting kept up. A minute of
  recording left macshot holding close to a gigabyte, and it was still holding it long
  after the recording had stopped. It now reuses the same handful of buffers for a whole
  recording, and takes each frame straight into the one it hands the encoder rather than
  building the frame twice. The same minute now settles at 175MB where it was 845-994MB,
  and the whole recording is six buffers rather than a thousand. A region recording gains
  the same thing in proportion to its size.

  Asking the collector to work harder was tried first and measured worse, not better: a
  compacting large-object collection after each recording settled at 2691MB, having grown
  *after* the recording stopped. The buffers had to stop being made, not be cleared up
  faster.

## [0.8.11] - 2026-09-16

### Changed

- **A region recording on a machine Windows sends no frames to moves noticeably more.**
  Where macshot has to take the frames itself, it was copying the whole screen and cutting
  the region out afterwards — three times the work for the same frame, several times a
  second, and the copy was what limited how often a frame could be taken at all. It copies
  only the rectangle being recorded now. Measured on an 864x744 recording: the copy fell
  from 40.8ms to 22.0ms and the recording rose from 11.4 frames a second to 15.9. A
  full-screen recording has nothing to trim and is unchanged.

  Asking for the frames faster was measured too, and is not the answer: at an 8ms cadence
  instead of 33ms the recording got *fewer* frames, 13.5 a second, and each copy got slower.
  Copies started closer together only get in each other's way.

## [0.8.10] - 2026-09-15

### Added

- **A recording that Windows will not give the microphone or the camera to now says so, and
  offers to open the page where it is turned back on.** Both were silent: a recording made
  without sound because a privacy setting was off looked exactly like one made on a machine
  with no microphone in it, and neither said anything at all. macshot now tells the two
  apart, names which one it was, turns the setting off so the next recording does not stop
  for it again, and offers a button that opens Windows' own microphone or camera privacy
  page. A machine that simply has no such device is still left alone — there is nothing to
  turn on and nothing worth interrupting anyone for.
- **The log says why a recording has no sound.** Five different failures — no endpoint, a
  refusal, a format the device would not take — all wrote nothing at all. Each now writes a
  line naming what failed, with the code Windows answered, and marks a refusal as such.

### Fixed

- **Every box macshot asks a question with now appears in front, and in the middle of the
  screen.** They were being raised in the top-left corner, behind whatever was already
  there: macshot has no main window, so it had nothing on screen to carry one forward and
  nothing on the taskbar to find one behind. A recording that stopped to ask about the
  microphone waited on an answer to a question showing only a corner of its title bar. This
  affected every alert raised from the notification area, including the update prompt.

### Note

- Screen capture itself needs no permission on Windows, unlike macOS, so nothing asks for
  one. The microphone and the camera are the only two things a recording can be refused.

## [0.8.9] - 2026-09-14

### Changed

- **A recording on a machine Windows sends no frames to follows the frame rate it was asked
  for, instead of ten a second whatever was chosen.** Where macshot has to take the frames
  itself it was doing so at a fixed rate, so a recording set to 120 and one set to 10 came
  out the same. It now takes them as often as the file can hold one, up to thirty a second —
  past which a copy of the screen costs more than it shows, because copying one is itself
  slower than a frame at those rates. Measured on a recording given nothing by Windows:
  19.5 frames a second against 9.1.

## [0.8.8] - 2026-09-14

### Changed

- **A recording on a machine that Windows sends no frames to starts moving in a third of a
  second rather than a whole one.** macshot waits before taking frames itself, so that a
  recording about to work properly is never interrupted by one that is only slow to start —
  and a working session has been measured answering within a tenth of a second, so the wait
  was far longer than it needed to be. It was set while taking a frame by hand meant asking
  Windows a second time, which was expensive and could hang; it is a copy of the screen
  now, which costs almost nothing and has been measured as identical to what Windows sends.
  The first second of every such recording was a frozen picture.

## [0.8.7] - 2026-09-14

### Fixed

- **About was off the right edge of the settings window and could only be reached by
  resizing it.** The seven tabs came to more than the window was wide, and a row of tabs
  that does not fit scrolls rather than shrinking, so the last one sat outside the window
  with nothing to say it was there. Each tab was carrying spacing it was never meant to
  have, and the window is now a little wider than the Mac's as well, because these tabs are
  drawn larger than the Mac draws its own.

## [0.8.6] - 2026-09-14

### Fixed

- **The fallback for a recording that comes out as one still picture had never once run on
  the machine it was written for.** 0.8.3 added it and 0.8.5 changed how it takes a frame,
  and on that machine neither got as far as taking one: both asked the recording's own
  capture handle a question first, from the thread that writes the video rather than the one
  that opened the recording, and Windows refuses that there. All three attempts failed
  within a fifth of a second, the fallback retired itself, and the log said only that no
  frame had been taken — which reads exactly like a machine that cannot take one. A
  recording now asks nothing of its own capture while taking a frame by hand, so the screen
  copy 0.8.5 added is finally what runs.

### Changed

- **A failed attempt to take a frame by hand records the error's number, not only its
  text.** The text is in the language the machine is running in, and the report that found
  the fault above arrived in Chinese, where the number would have named it on sight.

## [0.8.5] - 2026-09-10

### Fixed

- **A recording of a screen Windows never reports as changing is now copied off the screen
  instead.** 0.8.3 taught macshot to take frames itself where Windows delivers none, but it
  took them the same way Windows was already failing to — a second capture session — which
  works on machines that did not need it and delivered nothing on the one that did. It now
  copies the screen directly, which is what a screenshot already falls back to and owes
  nothing to whatever is wrong with the capture path. Measured against the frames Windows
  did deliver, in the same file, the two are pixel for pixel identical. Recording a single
  *window* is unchanged and still asks Windows, because a copy off the screen would carry
  whatever happens to be in front of that window.

### Changed

- **A recording says far more about what it caught.** The log now records the capture
  session opening and its size, when the first frame arrived, how many times Windows
  announced a frame, and how many of those announcements had nothing behind them — which
  were previously indistinguishable, both ending as `0 frames, 0 dropped`. A recording that
  caught nothing now says which of the two happened.

## [0.8.4] - 2026-09-09

### Fixed

- **No key did anything on the capture overlay.** Escape would not cancel a capture, Enter
  would not finish one, and the arrow keys would not nudge a selection — the only way out
  was the ✕ on the action column. The overlay never took the keyboard when it opened, and
  its key handlers were attached to something that could not be given it, so every key
  went nowhere. This arrived in 0.8.1, where the overlay stopped being a window of its own,
  and anyone who updated from 0.8.0 met it on the first capture.
- **A recording lost its first seconds on a machine where frames cannot be taken by hand.**
  0.8.3 taught macshot to take frames itself where Windows delivers none, and where that
  does not work either — one Windows 10 machine — each of the three attempts it makes
  before giving up held the recording up for two seconds. Measured on the report: 966
  frames written where the frame rate called for 1800. It now takes them alongside the
  recording rather than in the middle of it, which costs a recording that needs the
  fallback nothing, and no longer halves the frame rate of one that uses it successfully.

## [0.8.3] - 2026-09-08

### Fixed

- **A recording could come out as a single still picture, however long it ran.** Windows
  sends a recording a new frame when something on screen changes and at no other time, and
  on at least one machine it sent none at all — fifteen seconds of recording, not one
  frame — while ordinary screenshots on the same machine were fine. 0.8.1 taught macshot to
  start every recording holding a frame of its own, which turned that from no file into a
  file of that one frame repeated a thousand times: honest, and still not a recording.
  macshot now notices. A second into a recording it has been given nothing for, it starts
  taking frames itself, the way it takes a screenshot, until Windows delivers one of its
  own — after which it stops, because a recording that is working never needs this. GIF
  recordings had the same failure and are fixed the same way, and the line the log ends
  with says how many frames were taken this way.

## [0.8.2] - 2026-09-08

### Fixed

- **Memory no longer climbs with every capture.** 0.8.1 cut the growth down; this finds
  what was actually causing it. Two timers were holding on to interfaces that had already
  been taken off screen — the one behind the pencil's hold-to-select, which belongs to the
  thread rather than to the overlay that asked for it, and the one that dismisses the
  floating panel, which went on trying to close a panel that was already closed for the
  rest of the session. Between them they kept every overlay and every panel macshot had
  ever raised, with everything drawn in them. Measured over ten captures on the same
  machine as the report: memory settles after the first capture and stays there, against
  22MB growing to 452MB before.

## [0.8.1] - 2026-09-08

### Fixed

- **Half the icons were blank on Windows 10.** The ones macshot draws itself asked for
  Segoe Fluent Icons by name, and that font ships with Windows 11 only — on 10 the same
  symbols live in Segoe MDL2 Assets. The icons the interface builds from markup kept
  drawing, which is why some appeared and some did not. Every one of them now names both
  faces, and the history item's icon, which existed in the newer face alone, was changed
  for one that exists in both.
- **A recording of a screen that never moved produced no file at all.** 0.8.0 taught a
  still screen to repeat the frame it is showing, but there has to be a first frame to
  repeat, and Windows sends none until something changes — so a recording started on a
  quiet desktop waited for a frame that was not coming and ended with an error and nothing
  written. macshot now takes the first frame itself, the same way it takes a screenshot,
  before the recording starts.
- **Memory grew with every capture and was never given back** — 22MB at startup, 452MB
  after ten screenshots, and the same climb for recordings. Most of it was never in use:
  a capture turns tens of megabytes into rubbish at a stroke and macshot then goes back to
  being an idle tray icon, so nothing ever asked for it to be cleared up. It now asks, once
  a capture is saved, cancelled or recorded, and lets go by name of everything a finished
  capture was holding — the frozen screen of each display, the index behind edge snapping,
  the working copies the drawing surface keeps, and the picture each panel window was
  showing. Measured over eight captures, the growth per capture is roughly a third of what
  it was and the working set no longer climbs at all. Some remains: Windows does not
  reclaim an interface built for one capture, and what is left is that.

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
