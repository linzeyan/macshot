#!/usr/bin/env bash
# Refresh the Mac app's translations into this branch.
#
# The two products share their wording: a key is the English string, and every language
# for it is contributed and reviewed against the Mac app. This branch carries a copy
# rather than a link because it does not carry the Mac tree, so the copy has to be
# refreshed deliberately — that is what this is.
#
#   usage: windows/tools/sync-upstream-strings.sh [ref]   (ref defaults to main)
set -euo pipefail

ref="${1:-main}"
root="$(git rev-parse --show-toplevel)"
out="$root/windows/src/Macshot.Windows/Strings/upstream"

mkdir -p "$out"
count=0
while read -r file; do
  code="$(basename "$(dirname "$file")" .lproj)"
  git show "$ref:$file" > "$out/$code.strings"
  count=$((count + 1))
done < <(git ls-tree -r --name-only "$ref" | grep -E 'lproj/Localizable\.strings$')

if [ "$count" -eq 0 ]; then
  echo "no .lproj files on $ref — is that the branch with the Mac app on it?" >&2
  exit 1
fi

echo "$count languages refreshed from $ref into windows/src/Macshot.Windows/Strings/upstream"
