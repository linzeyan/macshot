# macshot for Windows

A port of [macshot](https://github.com/sw33tLie/macshot) — a native macOS screenshot and
annotation tool — to Windows. C#, .NET 10, WinUI 3 (Windows App SDK). No Electron, no web
views, no WPF.

**This branch carries only the Windows product.** The Swift app lives on `main`. It is
still the specification (see below), but nothing here builds from it.

---

## How to work on this

These come first because they are what decides whether a session goes well. The rest of
this file is reference.

### The macOS app is the specification

Same layout, same defaults, same wording, same behaviour. Where the two disagree, the Mac
is right and this is the bug. Only the language changes.

The Swift source is not in this branch. Put it on disk when you need it:

```bash
git worktree add ../macshot-mac main     # read-only reference; remove when done
```

Roughly 60 files here cite it by name and line (`ToolOptionsRowView.swift:1139`). Those
citations are still correct — they just need the worktree, or `git show
main:macshot/UI/Toolbar/ToolOptionsRowView.swift`.

**The Swift source says what to draw, not what it looks like.** `NSSlider` at width 100
and WinUI's `Slider` at width 100 are not the same width on screen — different padding,
different thumb, different minimum height. Transcribing macOS's numbers faithfully once
cost the toolbar ~114pt of overspend, which showed up as the vertical strip covering the
stamp row. When a layout has to match, **ask for the macOS measurement in rendered
points**, not for the file it came from.

### Ask the VM, do not guess

The WinUI half cannot be compiled on macOS at all, and the Core suite passing says nothing
about how the app looks. Both questions are answerable without a human in the loop:

```bash
windows/tools/vm-build.sh                 # does it compile? ~15s, warnings as errors
windows/tools/vm-build.sh --test          # …and do the tests pass on Windows
windows/tools/vm-build.sh --offline       # …does the variant compile on its own
windows/tools/vm-shot.sh --keys '^+x'     # what does it look like? → a PNG to read
```

Use them. Before this existed, every layout, weight and alignment bug cost a full round
trip through a person with a screenshot tool, and that is most of why the toolbar took as
many commits as it did.

**An unverified WinUI change is unfinished, not done.** And never bet on a framework
behaviour instead of checking it — "the weight will inherit through the
`ContentPresenter`" was such a bet, it was wrong, and it took three round trips to find
out. `.github/workflows/windows-build.yml` is the fallback when the VM is down.

### Change the class of thing, not the instance

When the instruction is "make all of X do Y", or the report is "this one is still wrong":

1. **Enumerate every site first** — `rg` for the pattern, print the list.
2. Show the list.
3. Then edit.

Patching only what was pointed at is how "make every label bold" took four commits: three
buttons fixed, then a `SplitButton` that could not come from the same factory, then the
code-built checkboxes no XAML style reaches. Each round was a full trip through a
screenshot. The scan costs one command.

### Bias

Slow is fast. Read the callers before editing. Prefer the smallest change that fixes the
cause rather than the symptom. Surface conflicts instead of averaging them. If something
was skipped, say so — "done" is wrong if anything was silently left out.

---

## Build and test

| Target | Where it can be built |
| --- | --- |
| `Macshot.Windows.Core` and its tests | Anywhere — plain `net10.0` |
| `Macshot.Windows` (WinUI 3) | Windows only |

```bash
# On this Mac — Core only.
dotnet test windows/tests/Macshot.Windows.Core.Tests/Macshot.Windows.Core.Tests.csproj
```

```powershell
# On Windows — everything. This is what CI runs.
dotnet build windows/Macshot.Windows.sln -c Release --warnaserror
dotnet test  windows/Macshot.Windows.sln -c Release --no-build
.\windows\build.ps1 -Run     # publishes to windows/dist and starts it
```

### The Windows VM

A UTM guest reachable over ssh as `macshot-vm`. `vm-build.sh` compiles there and prints
the errors here; `vm-shot.sh` photographs its desktop, optionally pressing something
first, and writes a PNG that can be read directly. One-time setup is in the header of
`vm-build.sh`; `vm-shot.sh` needs nothing beyond it.

Two things about the guest are load-bearing and cost a round trip each to learn:

- **Its ssh shell must be git's bash.** Git's transport sends `git-receive-pack 'C:/path'`
  and assumes the remote strips those quotes; cmd.exe does not, so every push fails.
- **A capture must run in the interactive session.** An ssh session has its own window
  station with no desktop on it, so a screenshot taken from there is blank and
  `CopyFromScreen` throws on the way. `vm-shot.sh` goes through a scheduled task
  registered with `/IT`, which is what puts it on the screen that exists.

And when running anything with `/switches` over ssh, prefix `MSYS_NO_PATHCONV=1` — git's
bash rewrites `/create` into `C:/Program Files/Git/create` otherwise.

### Reading CI failures without log access

The workflow log endpoint returns 403 for this repository, but the check-run annotations
carry the compiler messages:

```bash
gh api "repos/{owner}/{repo}/commits/$(git rev-parse HEAD)/check-runs" \
  --jq '.check_runs[] | select(.conclusion=="failure") | .id'
gh api "repos/{owner}/{repo}/check-runs/<id>/annotations" \
  --jq '.[] | "\(.path):\(.start_line) \(.message)"'
```

---

## Layout

```
windows/
├── Macshot.Windows.sln
├── build.ps1                       # Build / test / publish / run, on Windows
├── tools/
│   ├── vm-build.sh                 # Compile on the Windows VM, from here
│   ├── vm-shot.sh                  # Photograph the VM's desktop, from here
│   ├── sync-upstream-strings.sh    # Refresh the Mac app's translations from main
│   └── extract_meshes.py
│
├── src/Macshot.Windows.Core/       # net10.0, no Windows references, unit-tested
│   ├── Annotations/                # Model, tools, toolbar layout, stamps, shortcuts
│   ├── Capture/                    # Regions, monitors, snapping, recording plans, HUD placement
│   ├── Imaging/                    # Rasterizing, effects, beautify, scroll stitching
│   ├── Input/                      # HotkeyBinding
│   ├── Localization/               # AppLanguages, StringTable, ChineseText
│   ├── Output/                     # Settings, filename templates, formats, themes
│   ├── Recognition/                # OCR and QR result shaping
│   └── Upload/                     # Request and response shapes for every provider
│
├── src/Macshot.Windows/            # WinUI 3, Windows only
│   ├── App.xaml(.cs)               # Entry point → CaptureController
│   ├── CaptureController.cs        # Tray icon, hotkeys, capture orchestration
│   ├── *Window.xaml(.cs)           # One per surface: overlay, editor, preferences, history…
│   ├── AnnotationCanvasView        # The drawing surface
│   ├── AnnotationToolbarView.cs    # The toolbar and its per-tool options row
│   ├── Toolbar/                    # Pickers, swatches, segments, palette
│   ├── Rendering/                  # Sprites, glyphs, composition
│   ├── Services/                   # Capture, settings, history, OCR, fonts, localization…
│   ├── Upload/                     # imgbb, Google Drive, S3
│   └── Strings/
│       ├── *.strings               # This port's own strings
│       └── upstream/*.strings      # The Mac app's, vendored — do not edit, run the sync script
│
└── tests/Macshot.Windows.Core.Tests/
```

**Logic goes in Core.** Anything decidable without a window — geometry, formatting,
parsing, state machines, layout arithmetic — belongs there, because that is the half that
can be tested from this machine. `Macshot.Windows` should be the part that draws. Core must
not reference `Microsoft.UI.*`, `Windows.*`, or P/Invoke; that rule is the only thing
keeping the logic testable off Windows.

---

## Architecture

`CaptureController` is the orchestrator: it owns the tray icon, registers the global
hotkeys, drives a capture, and holds the windows that result. There is no main window —
macshot lives in the notification area.

A capture goes: hotkey → `GraphicsCaptureService` grabs every monitor → one
`CaptureOverlayWindow` per monitor → selection → annotation on `AnnotationCanvasView` →
output. `EditorWindow` is the same canvas without the selection chrome, inside a
`ScrollViewer`.

Annotations are a Core model (`Annotation`) that the WinUI half rasterizes. A tool's
creation logic is its own handler; the toolbar dispatches by tool rather than switching
inline.

Settings live in `%LOCALAPPDATA%\macshot\settings.json` (`SettingsStore`), history in
`%LOCALAPPDATA%\macshot\history\`, the diagnostic log in
`%LOCALAPPDATA%\macshot\macshot.log`.

### Build variants

One tree, two compilations. `-p:Variant=Offline` defines `OFFLINE`, renames the assembly to
`Macshot.Windows.Offline`, and drops `UploadToastWindow.xaml` from the build. Every network
feature must sit behind `#if !OFFLINE`, including any field only that code reads — an
unused field is a warning, warnings are errors, so the offline build fails loudly rather
than shipping dead upload code.

Collapse the UI a removed feature owned; do not disable it. A greyed-out button reads as
temporarily unavailable rather than as absent.

CI asserts the offline binary contains no `translation.googleapis.com`, because a control
collapsed at run time would leave the endpoint in the assembly and nothing else would
notice.

### Localization

Keys are the English strings themselves. `LocalizedTree.Localize()` walks a page's object
graph after `InitializeComponent` and replaces each string with its translation, so the
XAML *is* the key list — no `x:Uid`, no resource identifiers, and the worst case for a
missing key is English rather than a control that renders empty.

The Mac app's 40 languages are vendored under `Strings/upstream/` and refreshed with
`windows/tools/sync-upstream-strings.sh`. Do not edit them — edit the Mac app's. This
port's own strings, for what Windows names its own parts, go in `Strings/*.strings` and
resolve underneath the upstream ones.

Anything built **after** the page-wide pass has run must call `L(...)` itself. The tools
page in preferences is the example.

---

## Traps that only a Windows build finds

Valid-looking C# and XAML that compiles nowhere but Windows, because the markup compiler
and the type-info generator run only there. Each of these cost a CI round trip.

| Error | The rule | The fix |
| --- | --- | --- |
| `CS0234` The type or namespace 'X' does not exist in 'Macshot.Windows' | Inside `namespace Macshot.Windows.*`, a leading `Windows.` binds to **`Macshot.Windows`**, not to the Windows SDK. | `global::Windows.Foundation.Size`, or put the `using` *above* the namespace declaration. |
| `WMC0011` Unknown member 'Resources' on element 'Window' | A WinUI `Window` is **not** a `FrameworkElement`. No `Resources`, no `Style`, no `DataContext`. WPF's is, which is why the markup looks right. | Put `<Grid.Resources>` on the root content element. |
| `CS0108` … hides inherited member 'Window.X' | A member must not share a name with one the base class has — even a `private static` helper. `Bounds` is the one that catches people. | Rename it. `new` would silence a collision worth keeping. |
| `CS8852` Init-only property … | No `init` or `required` on a public property of a type XAML instantiates: `XamlTypeInfo.g.cs` writes an assignment outside an initializer. | Take it as a constructor parameter and leave the property get-only. |
| `CS8629` Nullable value type may be null | The analyser will not carry a fact about a local into an access on a *field*. | Match the field: `if (_x is { } value)`. |
| Redundant `using` | `ImplicitUsings` plus `TreatWarningsAsErrors` makes an already-implicit `using` fatal. | Delete it. |

### WinUI behaviours that are not obvious

- **A named `Style` replaces the implicit style outright**, it does not add to it. Every
  `Style x:Key="…" TargetType="TextBlock"` is a hole in any app-wide `TextBlock` styling
  unless it says `BasedOn="{StaticResource MacshotTextStyle}"`.
- **A local value beats any style**, implicit or keyed. Setting a property in code or as a
  XAML attribute wins.
- **There is no theme resource for a font weight.** `ContentControlThemeFontFamily` exists;
  no weight equivalent does. A control's weight has to be set on the control.
- **A tooltip given a bare string is parented to the popup root**, not to the control that
  owns it, so nothing set on the owner reaches it. Use `AppFonts.Tip(...)`.
- **`CheckBox` defaults to `VerticalContentAlignment="Top"`, `Padding="8,5,0,0"`,
  `MinHeight=32`** — a settings-page shape that puts the label above the box on a short row.
- **`NumberBox` is a text field first**: it cannot go below the text-control minimum
  height, and its compact spin buttons appear on focus, reflowing the row around it.
- **A running macshot blocks the build.** It has no window; the only sign is the tray icon.
  The project kills any running instance before building, or `Stop-Process -Name
  Macshot.Windows -Force`.

---

## Conventions

- **Language.** Discussion and analysis in 台灣正體中文. Code, comments, identifiers and
  commit messages in English.
- **Comments say why, not what.** Record the trade-off, the platform quirk, or the macOS
  number behind a constant. A comment restating the line above it is noise.
- **Search tools.** `fd` for filenames, `rg -n` for content (`--hidden` when needed), `sg`
  for structure. Never `find` or `grep`.
- **Warnings are errors** in every project. Do not suppress one to get a build through.
- **Tests are MSTest**, named `Method_ExplainsWhyItMatters`, and the doc comment says *why
  the behaviour matters* — a test that cannot fail when the intent changes is wrong.
- **Nullable is enabled** everywhere.
- **Fonts.** `AppFonts` names Segoe UI Variable Text with 微軟正黑體 UI behind it, resolved
  per glyph. The Chinese weight is decided **per string** (`AppFonts.Heavier` /
  `AppFonts.Weigh`, backed by `Core.Localization.ChineseText`), never per interface —
  bolding the whole window because the language is Chinese puts every English label in the
  Chinese weight.
- **Toolbar and popovers are always dark**, whatever the system appearance. Never use
  system-adaptive brushes for text there without checking contrast.

---

## Releasing

`.github/workflows/build-release.yml` triggers on `v*.*.*` and `v*.*.*-beta.*`, or on
`workflow_dispatch` with a `tag` input. It builds a 2×2 matrix — `x64`/`arm64` ×
`normal`/`offline` — self-contained, and attaches four zips to a GitHub Release, plus four
MSIX installers when there is a certificate to sign them with.

1. Add a `CHANGELOG.md` entry under `## [<version>]`. CI extracts that section as the
   release notes; a version with no entry gets the tag and an empty body.
2. `git tag v1.0.0 && git push origin v1.0.0`.

The tag is the version. It reaches the About page and the update check through
`-p:Version`; without it a build calls itself `0.0.0` and would be offered an update
forever. A `-beta.` in the tag marks the release as a pre-release automatically.

**Asset names are load-bearing.** `ReleaseCheck.IsWindowsAsset` requires `win` in the name
and matches `offline` to the variant, so an offline user is only ever offered an offline
build. Changing the naming scheme without changing that method breaks the update check for
everyone already running the app. `EveryNameTheReleaseWorkflowAttachesIsOfferedToExactlyOneVariant`
pins the eight names the workflow produces; a rename there has to be made here too.

### The MSIX, and signing it

Each matrix leg publishes once. `windows/tools/pack-msix.ps1` packs that same directory,
with the manifest and logos from `windows/packaging/msix/`, after the zip has been made
and uploaded — packing writes an `AppxManifest.xml` and an `Assets/` into the directory it
is given, so the order matters.

A second publish with `-p:WindowsPackageType=MSIX` looks like the right way to do this and
is not: it fails with *no AppxManifest is specified*, because a single-project MSIX build
wants the manifest as an item in the project, and this one keeps a single template outside
it for all four legs. The package made from the ordinary publish installs and launches.

Signing reads two repository secrets:

| Secret | What it holds |
| --- | --- |
| `WINDOWS_SIGNING_CERT_BASE64` | The code-signing certificate, a `.pfx` in base64. |
| `WINDOWS_SIGNING_CERT_PASSWORD` | Its password. |

**Neither is set today, and there is no certificate.** With them absent the MSIX is still
packed — a packaging step that only ran once a certificate existed would be one nobody had
seen work — but it is **not attached to the release**. Windows refuses to install an
unsigned MSIX, so attaching one would put a file on the release page that cannot be used.
It goes to a workflow artifact named `unsigned-msix-*`, which the release job's
`pattern: macshot-*` deliberately does not collect, and the run carries a `::warning::` and
a line in the job summary saying the release has no installer. Releases stay exactly as
they are today: four zips.

The certificate's **subject must be the package's `Publisher` verbatim** or the MSIX will
not install, so `pack-msix.ps1` reads the subject out of the `.pfx` and writes it into the
manifest before packing. Nothing needs updating when the certificate is bought — only the
two secrets.

To install one locally, `.\build.ps1 -Msix -CertificatePath test.pfx`; `pack-msix.ps1`'s
help has the `New-SelfSignedCertificate` line that makes a usable test certificate.

**A packaged macshot is not identical to an unpackaged one.** MSIX gives it a package
identity, which is what Windows AI Foundry requires — so `BackgroundRemover` starts working
on a Copilot+ PC, its check having always asked about the capability rather than the
packaging. It also puts the process in an MSIX container, and two things macshot does by
writing to `HKCU` are what a packaged app is supposed to declare in its manifest instead:
**Launch at login** (`StartupRegistration`, the `Run` key → `windows.startupTask`) and the
**`macshot:` URL scheme** (`UrlSchemeHost` → `windows.protocol`). Expect both to be dead in
the MSIX build. Where settings and history land under the container has not been checked.

**And the packaged build cannot take a screenshot yet.** Installed and launched from
`C:\Program Files\WindowsApps`, it starts, logs *Screen capture fell back to the older
backend: access denied*, and the capture hotkey raises no overlay — the container denies
what the unpackaged build is simply allowed. That is a capability the manifest has to ask
for, and it is unfinished work rather than a footnote: an installer that installs an app
which cannot do the one thing it is for is not shippable. Until it is settled the MSIX is
an addition and the zip is still how macshot is meant to be run.

A tag push runs the workflow **as it exists at the tagged commit**, so tagging here cannot
start the macOS pipeline on `main`, and vice versa.

---

## Known gaps

- The effects band carries all six of macOS's kinds — zoom, censor, cut, speed, freeze
  and text — through `VideoEffects`, the compositor and the band's own picker, and an
  export carries the audio, re-timed where a speed or freeze moved it. What has not been
  worked through on a real desktop is placing and exporting each kind in turn; the entry
  that used to sit here said the band was zoom-only and dropped the audio, which the code
  contradicts on both counts.
- The MSIX installs and launches but cannot capture: the container denies the screen, and
  the manifest has still to ask for it. It is also never signed — there is no certificate,
  so no release has carried an installer yet. With `windows.startupTask` and
  `windows.protocol`, that is what stands between it and being the way macshot is
  installed. See Releasing.
- Save formats stop at PNG, JPEG and HEIC. macOS also offers WebP and AVIF; WinRT exposes
  no encoder for either — WIC's WebP support is a decoder — so both would mean bundling a
  third-party codec. HEIC is offered only where its codec is registered, and the encode
  falls back to JPEG (renaming the file) where it is registered but broken.
- `docs/` is a symlink to a private directory outside the repository — the architecture
  notes and the manual verification procedure live there and resolve on Ricky's machine
  only.
