#!/usr/bin/env bash
#
# Build what a release attaches, unpack it the way a user does, and make each one open a
# window.
#
# Every check this project had ran against the build output, and the build output has
# always worked. The zips did not: publish dropped the compiled markup, so a released
# macshot started, put its icon in the notification area, and threw XamlParseException at
# the first window a capture asked for. CI now asserts the markup survives publish, which
# closes that one cause. This is the check that does not need to know the cause — it runs
# the artifact and asks the app whether it is all right.
#
#   windows/tools/vm-release-smoke.sh                  # all four, about ten minutes
#   windows/tools/vm-release-smoke.sh --version 1.0.0  # stamp it as a release would
#
# Two things make it worth the ten minutes. It expands the zip rather than running the
# publish directory, because a file that publish wrote and Compress-Archive skipped would
# look fine in the one and be missing from the other. And it reads the app's own log
# afterwards, so a window that failed to open is an exit code here rather than something
# to notice in a screenshot.
#
# The guest is Windows on ARM: arm64 runs natively and x64 runs under emulation. Both are
# the binaries that ship. What this cannot tell you is how they behave on an x64 host.
set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/vm-wake.sh"

TOOLS="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VM="${MACSHOT_VM:-macshot-vm}"
ROOT="${MACSHOT_VM_ROOT:-C:/src/macshot}"
OUT=/c/tmp/relsmoke

version=0.0.0-smoke

while [ $# -gt 0 ]; do
    case "$1" in
    --version)
        version="$2"
        shift 2
        ;;
    *)
        echo "unknown option: $1" >&2
        exit 2
        ;;
    esac
done

# Through vm-build.sh rather than repeating its half of the work: it wakes the guest,
# sends the working tree with uncommitted changes included, and fails loudly if the tree
# does not compile — all of which has to have happened before publishing it is worth
# anything, and none of which belongs in a second copy here.
echo "→ sending the tree and compiling it"
"$TOOLS/vm-build.sh" >/dev/null

ssh "$VM" "cat > /c/tmp/release-smoke.ps1" <<PS1
# Exactly what build-release.yml does, minus the upload: publish self-contained per
# architecture and variant, zip the output, expand the zip somewhere clean.
\$ErrorActionPreference = 'Stop'
Remove-Item -Recurse -Force '$OUT' -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force '$OUT' | Out-Null

foreach (\$arch in @('x64', 'arm64')) {
    foreach (\$variant in @('normal', 'offline')) {
        \$v = if (\$variant -eq 'offline') { 'Offline' } else { '' }
        \$pub = "$OUT\pub-\$variant-\$arch"

        dotnet publish '$ROOT/windows/src/Macshot.Windows/Macshot.Windows.csproj' \`
            -c Release -r "win-\$arch" --self-contained true -p:Variant=\$v \`
            -p:Version=$version -p:InformationalVersion=$version \`
            -o \$pub --nologo 2>&1 | Out-Null
        if (\$LASTEXITCODE -ne 0) { throw "publish failed: \$variant \$arch" }

        \$offline = if (\$variant -eq 'offline') { '-Offline' } else { '' }
        \$zip = "$OUT\macshot\$offline-$version-win-\$arch.zip"
        Compress-Archive -Path "\$pub\*" -DestinationPath \$zip -CompressionLevel Optimal
        Expand-Archive -Path \$zip -DestinationPath "$OUT\run-\$variant-\$arch"
    }
}
PS1

echo "→ publishing and packaging four artifacts"
ssh "$VM" "powershell -ExecutionPolicy Bypass -File C:/tmp/release-smoke.ps1"

status=0

for artifact in normal-x64 offline-x64 normal-arm64 offline-arm64; do
    case "$artifact" in
    offline-*) name=Macshot.Windows.Offline ;;
    *) name=Macshot.Windows ;;
    esac

    directory="$OUT/run-$artifact"

    # The zip has to carry it, not just the publish directory it was made from.
    if ! ssh "$VM" "test -f '$directory/$name.pri'"; then
        echo "✗ $artifact: the zip has no compiled markup, so it cannot open a window"
        status=1
        continue
    fi

    # A log left from the previous artifact would be read as this one's.
    ssh "$VM" "rm -f \"\$LOCALAPPDATA/macshot/macshot.log\"" || true

    windows_exe="$(ssh "$VM" "cygpath -w '$directory/$name.exe'")"
    "$TOOLS/vm-shot.sh" --start "$windows_exe" --wait 4 >/dev/null

    # Ctrl+Shift+X is the capture-area hotkey, and the overlay it raises is the first XAML
    # window the app builds — which is why this keystroke, and not merely starting it, is
    # the test. A tray icon appears either way.
    shot="${TMPDIR:-/tmp}/macshot-release-$artifact.png"
    "$TOOLS/vm-shot.sh" --keys '^+x' --wait 3 "$shot" >/dev/null
    "$TOOLS/vm-shot.sh" --keys '{ESC}' --wait 1 >/dev/null

    log="$(ssh "$VM" "cat \"\$LOCALAPPDATA/macshot/macshot.log\" 2>/dev/null" || true)"
    ssh "$VM" "MSYS_NO_PATHCONV=1 taskkill /IM $name.exe /F" >/dev/null 2>&1 || true

    if printf '%s' "$log" | grep -qi 'exception'; then
        echo "✗ $artifact: the app logged an exception"
        printf '%s\n' "$log" | grep -i -A3 'exception' | head -12
        status=1
    else
        echo "✓ $artifact: opened the overlay, log clean → $shot"
    fi
done

exit $status
