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
OUT_WINDOWS=C:/tmp/relsmoke

# The order the four are built and then run in.
ARTIFACTS='normal-x64 offline-x64 normal-arm64 offline-arm64'

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

# One leg per ssh call rather than one PowerShell loop over all four. Run together they
# failed on a different leg each time — a leg that succeeded by hand a minute later — and
# whatever the failure was never made it out of the loop. A process per leg isolates them
# and lets the error arrive here as itself.
#
# Exactly what build-release.yml does, minus the upload: publish self-contained per
# architecture and variant, zip the output, expand the zip somewhere clean.
echo "→ publishing and packaging four artifacts"
ssh "$VM" "rm -rf '$OUT' && mkdir -p '$OUT'"

for artifact in $ARTIFACTS; do
    variant="${artifact%-*}"
    arch="${artifact#*-}"

    property=""
    tag=""
    if [ "$variant" = offline ]; then
        property="-p:Variant=Offline"
        tag="-Offline"
    fi

    echo "  · $artifact"
    if ! output="$(ssh "$VM" "dotnet publish '$ROOT/windows/src/Macshot.Windows/Macshot.Windows.csproj' \
        -c Release -r win-$arch --self-contained true $property \
        -p:Version=$version -p:InformationalVersion=$version \
        -o '$OUT/pub-$artifact' --nologo" 2>&1)"; then
        # Only on failure: four publishes are four hundred lines of success nobody reads,
        # and the one that fails is the only thing that matters.
        printf '%s\n' "$output" | tail -25
        echo "✗ publish failed: $artifact" >&2
        exit 1
    fi

    # Windows-shaped paths from here on. PowerShell reads /c/tmp as a path rooted on the
    # current drive and writes C:\c\tmp — which is where an earlier version of this put
    # all four artifacts while the checks below read the right directory and found
    # somebody else's leftovers there. A tool that can pass against stale files is worse
    # than one that fails.
    zip="$OUT_WINDOWS/macshot$tag-$version-win-$arch.zip"
    ssh "$VM" "powershell -NoProfile -Command \"
        Compress-Archive -Path '$OUT_WINDOWS/pub-$artifact/*' -DestinationPath '$zip' -CompressionLevel Optimal
        Expand-Archive -Path '$zip' -DestinationPath '$OUT_WINDOWS/run-$artifact'\""
done

status=0

for artifact in $ARTIFACTS; do
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
