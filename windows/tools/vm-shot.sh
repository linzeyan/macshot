#!/usr/bin/env bash
#
# Photograph the Windows VM's desktop, and optionally press something first.
#
# The other half of vm-build.sh. That one answers "does it compile"; this one answers
# "what does it look like", which for a port whose whole job is to match another app's
# appearance is the question that actually decides whether the work is done. Without it
# every layout, weight and alignment bug costs a round trip through a human with a
# screenshot tool.
#
#   windows/tools/vm-shot.sh                     # the desktop as it is
#   windows/tools/vm-shot.sh --keys '^+x'        # press Ctrl+Shift+X, then photograph
#   windows/tools/vm-shot.sh --keys '{ESC}'      # …and this dismisses it again
#   windows/tools/vm-shot.sh --wait 3 out.png    # wait longer; write somewhere specific
#
# SendKeys notation: ^ is Ctrl, + is Shift, % is Alt, {ESC} {ENTER} {TAB} {F1} and so on.
#
# Setup is vm-build.sh's, plus nothing: the helper and the scheduled task that runs it are
# installed on first use.
#
# ── Why a scheduled task ──────────────────────────────────────────────────────────────
# An ssh session gets its own window station, which has no desktop on it — a capture taken
# from there is a blank bitmap, and CopyFromScreen throws a Win32Exception on the way. The
# task is registered with /IT, which runs it as the logged-on user in the session that
# actually has the screen, and that is the only thing that makes the picture real.
# ──────────────────────────────────────────────────────────────────────────────────────
set -euo pipefail

VM="${MACSHOT_VM:-macshot-vm}"
TASK=macshot-vm-shot

keys=""
wait_for=1
destination=""

while [ $# -gt 0 ]; do
    case "$1" in
    --keys)
        keys="$2"
        shift 2
        ;;
    --wait)
        wait_for="$2"
        shift 2
        ;;
    -*)
        echo "unknown option: $1" >&2
        exit 2
        ;;
    *)
        destination="$1"
        shift
        ;;
    esac
done

if [ -z "$destination" ]; then
    destination="${TMPDIR:-/tmp}/macshot-vm.png"
fi

home="$(ssh "$VM" 'echo $HOME')"
remote_script="$home/vm-shot.ps1"
remote_image="$home/vm-shot.png"

# Rewritten every run rather than checked: it is four hundred bytes, and a stale copy
# would be a silent wrong answer rather than a loud one.
ssh "$VM" "cat > '$remote_script'" <<'PS1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

# Read rather than taken as parameters: a scheduled task's command line is fixed when the
# task is registered, so anything that varies per run has to arrive some other way.
$arguments = Get-Content (Join-Path $env:USERPROFILE "vm-shot.args") -ErrorAction SilentlyContinue
$Keys = if ($arguments.Count -ge 1) { $arguments[0] } else { "" }
$Wait = if ($arguments.Count -ge 2 -and $arguments[1]) { [double]$arguments[1] } else { 1 }

if ($Keys) {
    # Whatever the user was last looking at is what has focus, which for a tray app with
    # a global hotkey is exactly right: the keystroke has to reach the shell, not macshot.
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
}

Start-Sleep -Seconds $Wait

$area = [System.Windows.Forms.SystemInformation]::VirtualScreen
$bitmap = New-Object System.Drawing.Bitmap $area.Width, $area.Height
$canvas = [System.Drawing.Graphics]::FromImage($bitmap)
$canvas.CopyFromScreen($area.Location, [System.Drawing.Point]::Empty, $area.Size)
$bitmap.Save((Join-Path $env:USERPROFILE "vm-shot.png"), [System.Drawing.Imaging.ImageFormat]::Png)
PS1

windows_script="$(ssh "$VM" "cygpath -w '$remote_script'")"

# scp's server side is a Windows binary and does not understand git-bash's /c/… form, so
# the copy at the end needs the path spelled the way Windows spells it.
windows_image="$(ssh "$VM" "cygpath -m '$remote_image'")"

# MSYS_NO_PATHCONV, because git's bash rewrites anything that looks like a POSIX path —
# /create and /tn arrive at schtasks as C:/Program Files/Git/create and it fails on an
# argument nobody wrote.
if ! ssh "$VM" "MSYS_NO_PATHCONV=1 schtasks /query /tn $TASK" >/dev/null 2>&1; then
    echo "→ installing the capture task in $VM"
    ssh "$VM" "MSYS_NO_PATHCONV=1 schtasks /create /tn $TASK \
        /tr 'powershell -ExecutionPolicy Bypass -File \"$windows_script\"' \
        /sc once /st 00:00 /it /f" >/dev/null
fi

# The task takes no arguments, so what to press is left where the helper reads it. A
# scheduled task's command line is fixed at registration; this is not.
ssh "$VM" "printf '%s\n%s\n' '$keys' '$wait_for' > '$home/vm-shot.args'" 2>/dev/null || true

ssh "$VM" "rm -f '$remote_image'; MSYS_NO_PATHCONV=1 schtasks /run /tn $TASK" >/dev/null

# Polled rather than slept: the task is asynchronous, and a fixed sleep is either a wasted
# second or a race, depending on how busy the guest is.
for _ in $(seq 1 40); do
    if ssh "$VM" "test -s '$remote_image'" 2>/dev/null; then
        break
    fi
    sleep 0.5
done

if ! ssh "$VM" "test -s '$remote_image'" 2>/dev/null; then
    echo "the capture task ran but produced nothing." >&2
    echo "is anyone logged in to the guest? /IT needs a session with a screen on it." >&2
    exit 1
fi

scp -q "$VM:$windows_image" "$destination"
echo "$destination"
