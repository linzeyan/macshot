#!/usr/bin/env bash
#
# What is still held a minute after a recording has finished, and whether it is managed.
#
# measure-recording-memory.sh answers "how much"; this answers "what". It records, waits
# for the process to settle, then takes a heap dump and asks the runtime for the GC heap's
# own size and its largest types. Private bytes minus the GC heap is the native half —
# Media Foundation, GDI and the allocator's own retention — which nothing in the managed
# code can collect its way out of, so knowing which half it is decides what to do next.
#
#   windows/tools/what-a-recording-keeps.sh [seconds]     # default 120
#
# Needs a build on the VM whose Mp4Frames has ProbeDropEverything on, or the VM's own
# compositor works and the by-hand path this is for never runs.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VM="${MACSHOT_VM:-macshot-vm}"
length="${1:-120}"

exe='C:\src\macshot\windows\src\Macshot.Windows\bin\Release\net10.0-windows10.0.26100.0\win-x64\Macshot.Windows.exe'

out="${TMPDIR:-/tmp}/macshot-keeps-$(date +%H%M%S)"
mkdir -p "$out"
echo "→ $out"

cat >"$out/prep.ps1" <<'PS1'
$settings = "$env:LOCALAPPDATA\macshot\settings.json"
Stop-Process -Name Macshot.Windows -Force -ErrorAction SilentlyContinue
Start-Sleep 1
if (-not (Test-Path "$settings.keeping")) { Copy-Item $settings "$settings.keeping" -Force }
$s = Get-Content $settings -Raw | ConvertFrom-Json
$s.recordingOnStop = "DoNothing"; $s.recordSystemAudio = $false; $s.recordMicAudio = $false
$s.recordWebcam = $false; $s.recordingFormat = "Mp4"
$s | ConvertTo-Json -Depth 10 | Set-Content $settings -Encoding utf8
"prepared"
PS1

cat >"$out/undo.ps1" <<'PS1'
$settings = "$env:LOCALAPPDATA\macshot\settings.json"
Stop-Process -Name Macshot.Windows -Force -ErrorAction SilentlyContinue
Start-Sleep 1
if (Test-Path "$settings.keeping") { Move-Item "$settings.keeping" $settings -Force }
Remove-Item C:\src\keep.dmp -ErrorAction SilentlyContinue
"undone"
PS1

cat >"$out/sample.ps1" <<'PS1'
$p = Get-Process Macshot.Windows -ErrorAction SilentlyContinue
if ($p) { "$([int]($p.PrivateMemorySize64/1MB)) $([int]($p.WorkingSet64/1MB)) $($p.Id)" } else { "-1 -1 -1" }
PS1

# Counters rather than a heap dump: dotnet-dump's analyzer answers "No CLR runtime found"
# against this build, and the question does not need type-level detail to be answered.
# What the GC has committed, against the process's private bytes, is the whole of it —
# the difference is native, and nothing in the managed code can collect that.
cat >"$out/counters.ps1" <<'PS1'
$p = Get-Process Macshot.Windows -ErrorAction Stop
$tool = "$env:USERPROFILE\.dotnet\tools\dotnet-counters.exe"
& $tool collect -p $p.Id --counters System.Runtime --format csv -o C:\src\counters.csv `
    --refresh-interval 5 --duration 00:00:15 2>&1 | Out-String
Get-Content C:\src\counters.csv | Select-String -Pattern 'heap.size|committed_size|fragmentation' |
    Select-Object -Last 14
PS1

scp -q "$out"/prep.ps1 "$out"/undo.ps1 "$out"/sample.ps1 "$out"/counters.ps1 "$VM:C:/src/"

run() { ssh "$VM" "powershell -NoProfile -ExecutionPolicy Bypass -File C:/src/$1" 2>/dev/null | LC_ALL=C tr -d '\r'; }

echo "→ preparing"
run prep.ps1 >"$out/00-prep.log"

restore() { echo "→ restoring"; run undo.ps1 >"$out/99-undo.log" 2>&1; }
trap restore EXIT

echo "→ starting"
"$here/vm-shot.sh" --start "$exe" --wait 10 "$out/01.png" >"$out/01.log" 2>&1
echo "  idle       $(run sample.ps1) MB (private, working set, pid)"

echo "→ recording for ${length}s"
"$here/vm-shot.sh" --start 'C:\src\record-fullscreen.cmd' --wait 5 "$out/02.png" >"$out/02.log" 2>&1
sleep "$length"
echo "  recording  $(run sample.ps1)"

"$here/vm-shot.sh" --keys '{ESC}' --wait 5 "$out/03.png" >"$out/03.log" 2>&1
for at in 30 60 90; do
  sleep 30
  echo "  +${at}s      $(run sample.ps1)"
done

echo "→ what the GC has, against what the process has"
run counters.ps1 >"$out/04-counters.txt" 2>&1
cat "$out/04-counters.txt"
