# Contributing to macshot for Windows

This branch is the Windows port. The macOS app it is a port of lives on `main`.

## Before you start

- **Bug fixes:** Open a PR directly with a clear description of what's broken and how you fixed it.
- **New features / large changes:** Open an issue first to discuss the approach. This avoids wasted effort if the feature doesn't fit the project direction.
- **Small improvements** (UI polish, performance, code cleanup): PRs welcome without prior discussion.

## Development setup

1. Windows 11, with the .NET 8 SDK and the Windows App SDK
2. `dotnet build windows/Macshot.Windows.sln`
3. `dotnet run --project windows/src/Macshot.Windows`

`Macshot.Windows.Core` targets plain `net8.0` and builds anywhere, macOS and Linux
included. `Macshot.Windows` needs Windows.

## Guidelines

- **This is a port, not a rewrite.** The macOS app is the specification: same layout,
  same defaults, same wording, same behaviour. Where the two disagree, the Mac is right
  and this is the bug. Cite the Swift file and line in a comment when a number comes
  from it — `git worktree add ../macshot-mac main` puts the reference on disk.
- **Logic goes in `Macshot.Windows.Core`, which is unit-tested.** Anything decidable
  without a window belongs there: geometry, formatting, parsing, state machines.
  `Macshot.Windows` is the part that draws.
- **Pure WinUI 3.** No WPF, no WinForms, no web views.
- **No new dependencies** unless there is no framework way to do it.
- **Comments say why, not what.** A comment restating the line above it is noise. One
  recording the trade-off, the platform quirk, or the macOS number behind a constant is
  why the file is still readable a year later.
- **Match the existing style.** Warnings are errors; the analyzers are the style guide.

## PR checklist

- [ ] `dotnet build windows/Macshot.Windows.sln --configuration Release` is clean
- [ ] `dotnet test windows/tests/Macshot.Windows.Core.Tests` passes
- [ ] New logic in Core has a test saying *why* the behaviour matters
- [ ] Anything visual was compared against the macOS app side by side
- [ ] Commit message describes *what* and *why*

## Questions?

Open an issue or start a discussion.
