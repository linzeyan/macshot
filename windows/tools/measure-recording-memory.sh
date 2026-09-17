#!/usr/bin/env bash
#
# Measure what a starved full-screen recording costs the process in private bytes.
#
# Working set decays on its own and hides retention entirely; PrivateMemorySize64 does
# not, which is why that is the number this prints. Settings are forced to the shape that
# leaves nothing but the recording running — no editor on stop, no microphone, no
# webcam — and put back afterwards.
#
#   windows/tools/measure-recording-memory.sh [seconds]     # default 60
#
# Requires a build already on the VM (windows/tools/vm-build.sh) whose Mp4Frames has
# ProbeDropEverything on, or the VM's own compositor works and nothing is starved.
#
# The PowerShell goes over as files rather than as -Command strings: git's bash on the
# guest eats $variables out of a command line, and every attempt to quote around that has
# cost a round trip. scp'd with forward slashes, because a backslash path is eaten too.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VM="${MACSHOT_VM:-macshot-vm}"
length="${1:-60}"

exe='C:\src\macshot\windows\src\Macshot.Windows\bin\Release\net10.0-windows10.0.26100.0\win-x64\Macshot.Windows.exe'

out="${TMPDIR:-/tmp}/macshot-memory-$(date +%H%M%S)"
mkdir -p "$out"
echo "→ $out"

cat >"$out/prepare.ps1" <<'PS1'
$settings = "$env:LOCALAPPDATA\macshot\settings.json"
Stop-Process -Name Macshot.Windows -Force -ErrorAction SilentlyContinue
Start-Sleep 1
Copy-Item $settings "$settings.measuring" -Force
$s = Get-Content $settings -Raw | ConvertFrom-Json
$s.recordingOnStop = "DoNothing"
$s.recordSystemAudio = $false
$s.recordMicAudio = $false
$s.recordWebcam = $false
$s | ConvertTo-Json -Depth 10 | Set-Content $settings -Encoding utf8
"prepared"
PS1

cat >"$out/restore.ps1" <<'PS1'
$settings = "$env:LOCALAPPDATA\macshot\settings.json"
Stop-Process -Name Macshot.Windows -Force -ErrorAction SilentlyContinue
Start-Sleep 1
if (Test-Path "$settings.measuring") { Move-Item "$settings.measuring" $settings -Force }
"restored"
PS1

cat >"$out/sample.ps1" <<'PS1'
$p = Get-Process Macshot.Windows -ErrorAction SilentlyContinue
if ($p) { [int]($p.PrivateMemorySize64 / 1MB) } else { -1 }
PS1

cat >"$out/tail.ps1" <<'PS1'
Get-Content "$env:LOCALAPPDATA\macshot\macshot.log" -Tail 40
PS1

scp -q "$out/prepare.ps1" "$out/restore.ps1" "$out/sample.ps1" "$out/tail.ps1" "$VM:C:/src/"

# LC_ALL: the log carries localized messages in the guest's code page, and a tr that
# aborts on an illegal byte sequence silently truncates everything after it — which read
# as "the recording wrote no log line at all" for a whole round trip.
run() { ssh "$VM" "powershell -NoProfile -ExecutionPolicy Bypass -File C:/src/$1" 2>/dev/null | LC_ALL=C tr -d '\r'; }
sample() { run sample.ps1; }

echo "→ stopping macshot and forcing the settings"
run prepare.ps1 | tee "$out/00-prepare.log"

restore() {
  echo "→ restoring the settings"
  run restore.ps1 >"$out/99-restore.log" 2>&1
}
trap restore EXIT

echo "→ starting macshot"
"$here/vm-shot.sh" --start "$exe" --wait 8 "$out/01-started.png" >"$out/01-start.log" 2>&1
echo "  idle $(sample)MB"

echo "→ recording for ${length}s"
"$here/vm-shot.sh" --start 'C:\src\record-fullscreen.cmd' --wait 5 "$out/02-recording.png" \
  >"$out/02-record.log" 2>&1

sleep $((length / 2))
echo "  at $((length / 2))s $(sample)MB"

sleep $((length - length / 2))
echo "  at ${length}s $(sample)MB"

echo "→ stopping"
"$here/vm-shot.sh" --keys '{ESC}' --wait 5 "$out/03-stopped.png" >"$out/03-stop.log" 2>&1

for at in 30 60 90; do
  sleep 30
  echo "  ${at}s after the stop $(sample)MB"
done

echo "→ what the recording says it did"
run tail.ps1 | tee "$out/04-log.txt" | rg 'recorded|compositor|buffers' || true
