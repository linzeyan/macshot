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
#   windows/tools/vm-shot.sh --click 640,400     # click there, then photograph
#   windows/tools/vm-shot.sh --right-click 1823,1519  # …with the other button
#   windows/tools/vm-shot.sh --right-click 1712,1546 --click 1734,1439  # …then take an item
#   windows/tools/vm-shot.sh --drag 100,100,500,400  # drag a region, then photograph
#   windows/tools/vm-shot.sh --start 'C:\\x.exe'  # launch it, then photograph
#   windows/tools/vm-shot.sh --wait 3 out.png    # wait longer; write somewhere specific
#
# One action per call, so they compose: launch, shoot, press, shoot, click, shoot. What is
# on screen persists between calls, which is what makes a sequence of them a session.
#
# The exception is a menu, which does not: a flyout is dismissed by this script's own task
# starting, so it cannot be opened by one call and clicked by the next. Pass both at once —
# --right-click opens it and --click, which runs after, takes an item off it. Keys do not
# work on a menu: it is a modal loop inside macshot's thread and SendKeys goes to whatever
# window is foreground, so '{DOWN 16}{ENTER}' dismisses it and chooses nothing.
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

. "$(dirname "${BASH_SOURCE[0]}")/vm-wake.sh"

VM="${MACSHOT_VM:-macshot-vm}"
TASK=macshot-vm-shot

keys=""
click=""
right_click=""
drag=""
start=""
wait_for=1
destination=""

while [ $# -gt 0 ]; do
    case "$1" in
    --keys)
        keys="$2"
        shift 2
        ;;
    --click)
        click="$2"
        shift 2
        ;;
    --right-click)
        right_click="$2"
        shift 2
        ;;
    --drag)
        drag="$2"
        shift 2
        ;;
    --start)
        start="$2"
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

if ! vm_wake "$VM"; then
    echo "cannot reach $VM over ssh. the setup steps are in vm-build.sh's header." >&2
    exit 1
fi

home="$(ssh "$VM" 'echo $HOME')"
remote_script="$home/vm-shot.ps1"
remote_image="$home/vm-shot.png"

# Rewritten every run rather than checked: it is four hundred bytes, and a stale copy
# would be a silent wrong answer rather than a loud one.
ssh "$VM" "cat > '$remote_script'" <<'PS1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

# Before anything asks how big the screen is. PowerShell is not DPI-aware, so on a scaled
# display Windows lies to it: the capture comes back at the virtualized size, softened by
# the scaler, which is the one thing a picture taken to judge layout must not be. It also
# keeps the pointer's coordinates and the image's the same numbers.
Add-Type -Namespace VmShot -Name Dpi -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
"@
[VmShot.Dpi]::SetProcessDPIAware() | Out-Null

# Read rather than taken as parameters: a scheduled task's command line is fixed when the
# task is registered, so anything that varies per run has to arrive some other way.
$arguments = Get-Content (Join-Path $env:USERPROFILE "vm-shot.args") -ErrorAction SilentlyContinue
$Keys = if ($arguments.Count -ge 1) { $arguments[0] } else { "" }
$Wait = if ($arguments.Count -ge 2 -and $arguments[1]) { [double]$arguments[1] } else { 1 }
$Click = if ($arguments.Count -ge 3) { $arguments[2] } else { "" }
$Start = if ($arguments.Count -ge 4) { $arguments[3] } else { "" }
$Drag = if ($arguments.Count -ge 5) { $arguments[4] } else { "" }
$RightClick = if ($arguments.Count -ge 6) { $arguments[5] } else { "" }

if ($Start) {
    Start-Process -FilePath $Start
}

# Out here rather than in the branch that first needed it: it was declared inside the click
# and used by the drag as well, so a drag on its own threw on a type nobody had added — and
# the failure looked exactly like a desktop that had not changed.
Add-Type -Namespace VmShot -Name Pointer -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, int extra);
"@

if ($RightClick) {
    # The other button, because several things have no other way in: the notification
    # area's menu is the only route to Preferences and History, the colour wheel opens
    # only on a right-click over the capture, and a tool leaves the strip by being
    # right-clicked. Without this a whole class of the checklist cannot be reached at all.
    #
    # Nothing is clicked first to focus anything. A menu that needs its owner foregrounded
    # would be a bug in macshot rather than something to paper over here.
    $at = $RightClick.Split(",")
    [VmShot.Pointer]::SetCursorPos([int]$at[0], [int]$at[1]) | Out-Null
    Start-Sleep -Milliseconds 120
    [VmShot.Pointer]::mouse_event(0x0008, 0, 0, 0, 0)
    [VmShot.Pointer]::mouse_event(0x0010, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 400
}

if ($Click) {
    # Moved, then given a moment, then pressed. An overlay that tracks the pointer places
    # its chrome from the moves, and a press in the same breath as the move arrives before
    # the window has been told where the pointer now is.
    #
    # After the right button rather than before it, which is what makes --right-click and
    # --click together the way to take an item off the tray menu. Keys cannot do it: the
    # menu is a modal loop inside macshot's own thread, and SendKeys arrives at whatever
    # window is foreground instead — {DOWN 16}{ENTER} dismissed the menu and chose nothing.
    # The menu does survive the wait above, so the second press lands on it.
    $at = $Click.Split(",")
    [VmShot.Pointer]::SetCursorPos([int]$at[0], [int]$at[1]) | Out-Null
    Start-Sleep -Milliseconds 120
    [VmShot.Pointer]::mouse_event(0x0002, 0, 0, 0, 0)
    [VmShot.Pointer]::mouse_event(0x0004, 0, 0, 0, 0)
}

if ($Drag) {
    # In steps rather than one jump. A rubber-band selection is built from the moves, and
    # a press followed by a single move to the far corner is a gesture some of the app
    # never sees happening.
    $at = $Drag.Split(",")
    $fromX = [int]$at[0]; $fromY = [int]$at[1]; $toX = [int]$at[2]; $toY = [int]$at[3]
    [VmShot.Pointer]::SetCursorPos($fromX, $fromY) | Out-Null
    Start-Sleep -Milliseconds 120
    [VmShot.Pointer]::mouse_event(0x0002, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 200

    foreach ($step in 1..20) {
        [VmShot.Pointer]::SetCursorPos(
            $fromX + [int](($toX - $fromX) * $step / 20),
            $fromY + [int](($toY - $fromY) * $step / 20)) | Out-Null
        Start-Sleep -Milliseconds 40
    }

    Start-Sleep -Milliseconds 200
    [VmShot.Pointer]::mouse_event(0x0004, 0, 0, 0, 0)
}

if ($Keys) {
    # Last, after whatever the pointer did. Alone it reaches whatever the user was looking
    # at, which for a tray app with a global hotkey is exactly right. Paired with a click it
    # reaches what the click opened — and a menu is the only way to reach several of
    # macshot's commands, while a flyout does not survive between two runs of this script:
    # the task's own activation dismisses it. Sent first, it could only ever have talked to
    # the window the click was about to replace.
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
#
# -WindowStyle Hidden, because the helper's own console is on the desktop it is
# photographing. It opens on top of whatever is being examined, which means a --click lands
# on the console rather than on the app, and the picture is of the console. Rewritten every
# run rather than only when missing: a guest that already carries the visible-window version
# would keep it forever, and the symptom — clicks quietly hitting the wrong window — reads
# as the app ignoring them.
ssh "$VM" "MSYS_NO_PATHCONV=1 schtasks /create /tn $TASK \
    /tr 'powershell -ExecutionPolicy Bypass -WindowStyle Hidden -File \"$windows_script\"' \
    /sc once /st 00:00 /it /f" >/dev/null 2>&1

# The task takes no arguments, so what to press is left where the helper reads it. A
# scheduled task's command line is fixed at registration; this is not.
ssh "$VM" \
    "printf '%s\n%s\n%s\n%s\n%s\n%s\n' '$keys' '$wait_for' '$click' '$start' '$drag' '$right_click' > '$home/vm-shot.args'" \
    2>/dev/null || true

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
