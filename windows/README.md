# macshot for Windows

A native Windows port of [macshot](https://github.com/sw33tLie/macshot), on the
`windows` branch. C#, .NET 8, WinUI 3 (Windows App SDK). `main` stays the macOS
product; this tree is not built from it.

Still in progress. Capture, annotation, editing, recognition, recording and
delivery work; uploads, HEIC and WebP encoding, and an installer do not. None of
it has been run on Windows hardware yet — CI compiles the WinUI half and the
portable half is unit-tested, which is not the same thing. See
`docs/windows-port/` in the repository for the roadmap and the feature-parity
matrix.

## Requirements

- Windows 10 20H1 (10.0.19041) or newer
- [.NET SDK 8.0](https://dotnet.microsoft.com/download) or newer

Visual Studio is not needed. Everything else, including the Windows App SDK, is
restored from NuGet on the first build.

## Build and run

```powershell
git clone https://github.com/sw33tLie/macshot.git
cd macshot
git switch windows
.\windows\build.ps1 -Run
```

`-Run` publishes a self-contained copy to `windows/dist/Release` and starts it.
There is no installer yet and nothing is written outside that folder, so copying
it elsewhere is all that "installing" means today. Delete the folder to uninstall.

Other switches:

| Command | What it does |
| --- | --- |
| `.\windows\build.ps1` | Builds Release, the same way CI does. |
| `.\windows\build.ps1 -Test` | Builds and runs the unit tests. |
| `.\windows\build.ps1 -Publish` | Publishes without starting it. |
| `.\windows\build.ps1 -FrameworkDependent -Publish` | Much smaller output, but needs the .NET 8 Desktop Runtime installed. |
| `.\windows\build.ps1 -Configuration Debug -Run` | Debug build. |

If PowerShell refuses to run the script, it is the execution policy rather than
the script:

```powershell
powershell -ExecutionPolicy Bypass -File .\windows\build.ps1 -Run
```

## Using it

macshot has no window. It runs in the notification area:

| Action | How |
| --- | --- |
| Capture an area | `Ctrl+Shift+X`, or left-click the icon |
| Capture every screen | `Ctrl+Shift+F` |
| Record the screen | `Ctrl+Shift+R`, and again to stop |
| Capture after a delay, History, Preferences, Quit | right-click the icon |

Drag to select, or click a window to take it whole. The chosen region can be
adjusted with the eight grips or the arrow keys. Annotate with the toolbar,
`Enter` to finish, `Esc` to cancel. `Ctrl+Z` / `Ctrl+Shift+Z` undo and redo.

The first tool is a pointer: it selects a mark already drawn so it can be moved,
reshaped by its handles, turned, bent, or deleted. Each tool is offered only the
options it uses — arrow ends, rounded corners, a dash pattern, an emoji — so the
bar never shows a control that would do nothing. Picking a colour magnifies the
pixels under the pointer, and a label breaks its line on `Shift+Enter`.

For anything more than a few marks, press **Editor** for a resizable window with
zoom, cropping, flipping and gradient backgrounds. A past capture reopens there
too, from the thumbnail, the Recent captures menu, or the History panel.

By default a finished capture goes to the clipboard, is saved to
`Pictures\Macshot`, and appears as a floating thumbnail with copy, save, pin, and
edit; a run of captures stacks up the corner rather than replacing each other. All
of that is configurable in Preferences, which writes to
`%LOCALAPPDATA%\macshot\settings.json`. Shortcuts there are set by pressing the
keys, not by typing their names.

## Working on it

- `src/Macshot.Windows.Core` — platform-neutral, unit-tested, builds on any OS.
- `src/Macshot.Windows` — WinUI 3, Windows only.
- `tests/Macshot.Windows.Core.Tests` — MSTest, runs anywhere.

Core must not reference `Microsoft.UI.*`, `Windows.*`, or P/Invoke; that rule is
what keeps the logic testable off Windows. On macOS or Linux you can still build
and test Core:

```bash
dotnet test windows/tests/Macshot.Windows.Core.Tests/Macshot.Windows.Core.Tests.csproj -c Release
```

Anything you change under `src/Macshot.Windows` is unverified until CI is green.
Some of it is valid-looking C# and XAML that only the Windows markup compiler
rejects — a `Window` has no `Resources`, a member must not share a name with one
`Window` already has, and a public property of a type XAML instantiates cannot be
`init`. Those and the rest are in
[`docs/windows-port/build.md`](../docs/windows-port/build.md), under "Traps that
only a Windows build finds"; each one there cost a CI round trip to learn.
