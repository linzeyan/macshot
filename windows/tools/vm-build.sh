#!/usr/bin/env bash
#
# Compile the WinUI half on the Windows VM and print the errors here.
#
# The WinUI project cannot be built on macOS at all, so without this the only oracle is a
# CI round trip — which is minutes, and which happens after a commit and a push. This makes
# the compiler answer in seconds and before the commit, which is the whole point.
#
# It sends the working tree as it is, uncommitted changes included, so there is nothing to
# commit first and nothing to remember to undo afterwards.
#
#   windows/tools/vm-build.sh              # build Release, warnings as errors
#   windows/tools/vm-build.sh --test       # …and run the full test suite on Windows
#   windows/tools/vm-build.sh --offline    # …the offline variant instead
#   windows/tools/vm-build.sh --run        # …publish and start it in the VM
#
#   MACSHOT_VM=name        ssh host to use          (default: macshot-vm)
#   MACSHOT_VM_ROOT=path   the clone inside the VM  (default: C:/src/macshot)
#
# ── One-time setup, inside the Windows guest ──────────────────────────────────────────
#
#   # 1. OpenSSH server
#   Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
#   Set-Service -Name sshd -StartupType Automatic; Start-Service sshd
#
#   # 2. Your Mac's public key. For an ADMIN account this file, not ~/.ssh/authorized_keys
#   #    — the admin override is the single most common reason key auth silently fails.
#   notepad C:\ProgramData\ssh\administrators_authorized_keys
#   icacls C:\ProgramData\ssh\administrators_authorized_keys /inheritance:r `
#     /grant "Administrators:F" /grant "SYSTEM:F"
#
#   # 3. Toolchain
#   winget install --id Microsoft.DotNet.SDK.10 -e
#   winget install --id Git.Git -e
#
#   # 4. The clone. Any branch; this script overwrites the tree on every run.
#   git clone -b windows https://github.com/linzeyan/macshot.git C:\src\macshot
#
#   # 5. Git's own bash as the ssh shell. Not optional: git's transport sends
#   #    `git-receive-pack 'C:/path'` and assumes the remote strips those quotes. cmd.exe
#   #    does not, so the repository is looked for at a path with quote marks in its name
#   #    and every push fails with "does not appear to be a git repository".
#   New-ItemProperty -Path HKLM:\SOFTWARE\OpenSSH -Name DefaultShell `
#     -Value "C:\Program Files\Git\bin\bash.exe" -PropertyType String -Force
#
# Then, on the Mac, add to ~/.ssh/config:
#
#   Host macshot-vm
#     HostName 192.168.64.x        # UTM > the VM > Network: use Shared Network, then
#     User     <your windows user> #   `ipconfig` in the guest for the address
#     IdentityFile ~/.ssh/id_ed25519
#
# Check it with:  ssh macshot-vm dotnet --version
# ──────────────────────────────────────────────────────────────────────────────────────
set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/vm-wake.sh"

VM="${MACSHOT_VM:-macshot-vm}"
ROOT="${MACSHOT_VM_ROOT:-C:/src/macshot}"

configuration=Release
variant=""
action=build

for argument in "$@"; do
    case "$argument" in
    --test) action=test ;;
    --run) action=run ;;
    --offline) variant="-p:Variant=Offline" ;;
    --debug) configuration=Debug ;;
    *)
        echo "unknown option: $argument" >&2
        exit 2
        ;;
    esac
done

cd "$(git rev-parse --show-toplevel)"

if ! vm_wake "$VM" || ! ssh -o BatchMode=yes -o ConnectTimeout=10 "$VM" "git --version" >/dev/null 2>&1; then
    echo "cannot reach $VM over ssh, or git is not on its PATH." >&2
    echo "the setup steps are in the header of this script." >&2
    exit 1
fi

# A ref outside refs/heads/, so the guest never has it checked out and the push cannot be
# refused for updating the current branch. The guest resets onto it instead.
echo "→ sending $(git rev-parse --short HEAD) to $VM"
git push --quiet --force "$VM:$ROOT" "HEAD:refs/vm/head"

# clean without -x: bin/ and obj/ are ignored, and wiping them turns every run into a
# cold build. The guest's tree is disposable in every other respect.
ssh "$VM" "git -C $ROOT reset --quiet --hard refs/vm/head && git -C $ROOT clean -qfd"

# Through a scratch index rather than `git diff HEAD`, because that one cannot see a file
# git has never been told about — and a new file is what a port adds most often. It was
# exactly this: a new enum stayed on the Mac while every file that referred to it went
# over, and the guest reported it missing. `add -A` here writes to the copy, so the real
# index and anything staged in it are untouched.
scratch="$(mktemp -t macshot-vm-index)"
trap 'rm -f "$scratch"' EXIT
cp "$(git rev-parse --git-dir)/index" "$scratch"

if ! GIT_INDEX_FILE="$scratch" git add -A 2>/dev/null; then
    echo "cannot read the working tree." >&2
    exit 1
fi

if ! GIT_INDEX_FILE="$scratch" git diff --cached --quiet HEAD; then
    echo "→ applying uncommitted changes"
    GIT_INDEX_FILE="$scratch" git diff --cached HEAD --binary \
        | ssh "$VM" "git -C $ROOT apply --whitespace=nowarn -"
fi

case "$action" in
build) command="dotnet build $ROOT/windows/Macshot.Windows.sln -c $configuration --warnaserror --nologo $variant" ;;
test) command="dotnet test $ROOT/windows/Macshot.Windows.sln -c $configuration --nologo $variant" ;;
run) command="powershell -ExecutionPolicy Bypass -File $ROOT/windows/build.ps1 -Configuration $configuration -Run" ;;
esac

echo "→ $action"
set +e
output="$(ssh "$VM" "$command" 2>&1)"
status=$?
set -e

echo "$output"

if [ $status -ne 0 ]; then
    echo
    echo "── errors ─────────────────────────────────────────────────────────────────"
    # Deduplicated: MSBuild reports the same diagnostic once per project that saw it,
    # and three copies of one error reads as three problems.
    echo "$output" | grep -E "error [A-Z]+[0-9]+" | sed 's|^.*[\\/]macshot[\\/]||' | sort -u
fi

exit $status
