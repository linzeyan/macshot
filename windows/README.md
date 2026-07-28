# macshot for Windows

A native Windows port of [macshot](https://github.com/sw33tLie/macshot), on the
`windows` branch. C#, .NET 8, WinUI 3 (Windows App SDK). `main` stays the macOS
product; this tree is not built from it.

Still in progress. Capture, annotation, and delivery work; text, OCR, recording,
and an installer do not. See `docs/windows-port/` in the repository for the
roadmap and the feature-parity matrix.

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
| Preferences, Quit | right-click the icon |

Drag to select, annotate with the toolbar, `Enter` to finish, `Esc` to cancel.
`Ctrl+Z` / `Ctrl+Shift+Z` undo and redo.

By default a finished capture goes to the clipboard, is saved to
`Pictures\Macshot`, and appears as a floating thumbnail with copy, save, pin, and
edit. All of that is configurable in Preferences, which writes to
`%LOCALAPPDATA%\macshot\settings.json`.

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
