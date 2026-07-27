# Windows Upstream Parity

## Current status

No `windows` to `main` commit mapping is recorded yet. The Windows product has
not reached full feature parity with the macOS product.

Each upstream `main` commit is tracked by a GitHub issue created by the
`Track Windows Upstream Progress` workflow. Close an issue only after its
Windows impact is assessed and its required work is complete or explicitly
waived.

## Record the first mapping only after full parity

After every user-visible feature and supported behavior has a verified Windows
equivalent, replace the status above with exactly one mapping entry:

| Windows version | Windows commit | Equivalent `main` commit | Verification date |
| --- | --- | --- | --- |
| `<version>` | `<windows-sha>` | `<main-sha>` | `<YYYY-MM-DD>` |

Future mappings are allowed only for later releases that regain full parity.
