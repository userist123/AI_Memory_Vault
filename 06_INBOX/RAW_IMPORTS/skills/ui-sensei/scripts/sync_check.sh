#!/usr/bin/env bash
# sync_check.sh — detects when an embedded source has moved past the commit
# ui-sensei's references/ were adapted from.
#
# Requires: git, jq. Run with network access (this won't work in a sandboxed
# agent session without internet — run it on your own machine).
#
# Usage:
#   ./sync_check.sh            # report drift for every source in sources.lock.json
#   ./sync_check.sh --update   # after reviewing drift, stamp current HEAD as the new baseline

set -euo pipefail

LOCK_FILE="$(dirname "$0")/sources.lock.json"
UPDATE_MODE=false
[[ "${1:-}" == "--update" ]] && UPDATE_MODE=true

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required (brew install jq / apt install jq) — aborting." >&2
  exit 1
fi

names=$(jq -r 'keys[]' "$LOCK_FILE")
drift_found=false

for name in $names; do
  repo=$(jq -r ".\"$name\".repo" "$LOCK_FILE")
  branch=$(jq -r ".\"$name\".branch" "$LOCK_FILE")
  known=$(jq -r ".\"$name\".last_known_commit" "$LOCK_FILE")

  current=$(git ls-remote "$repo" "refs/heads/$branch" 2>/dev/null | cut -f1)

  if [[ -z "$current" ]]; then
    echo "⚠️  $name — couldn't reach $repo (branch $branch). Skipping."
    continue
  fi

  if [[ "$known" == "null" || -z "$known" ]]; then
    echo "🆕 $name — no baseline recorded yet. Current HEAD: $current"
    if $UPDATE_MODE; then
      tmp=$(mktemp)
      jq ".\"$name\".last_known_commit = \"$current\" | .\"$name\".last_checked = \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"" "$LOCK_FILE" > "$tmp" && mv "$tmp" "$LOCK_FILE"
      echo "   → baseline recorded."
    fi
    continue
  fi

  if [[ "$known" != "$current" ]]; then
    drift_found=true
    echo "🔴 $name — DRIFT DETECTED"
    echo "   known:   $known"
    echo "   current: $current"
    echo "   → re-review $repo before trusting references/ for this source; PROVENANCE.md notes may be stale."
    if $UPDATE_MODE; then
      tmp=$(mktemp)
      jq ".\"$name\".last_known_commit = \"$current\" | .\"$name\".last_checked = \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"" "$LOCK_FILE" > "$tmp" && mv "$tmp" "$LOCK_FILE"
      echo "   → baseline updated (you confirmed you reviewed the change)."
    fi
  else
    echo "✅ $name — up to date ($current)"
    tmp=$(mktemp)
    jq ".\"$name\".last_checked = \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"" "$LOCK_FILE" > "$tmp" && mv "$tmp" "$LOCK_FILE"
  fi
done

if $drift_found && ! $UPDATE_MODE; then
  echo
  echo "Drift found in one or more sources. Review the diffs above, then re-run with --update once you've synced references/ accordingly."
  exit 1
fi