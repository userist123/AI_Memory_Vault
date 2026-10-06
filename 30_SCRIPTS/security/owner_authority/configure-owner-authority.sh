#!/usr/bin/env bash
set -euo pipefail

REPO="${1:-userist123/AI_Memory_Vault_CODEX_READY}"
BRANCH="${2:-main}"
OWNER_ID="${3:-199154571}"

command -v gh >/dev/null || { echo "gh CLI is required."; exit 1; }
gh auth status >/dev/null

echo "Configuring owner-approval environment for $REPO"
# deployment_branch_policy is null on purpose: the owner-approval job runs on pull_request
# events (refs/pull/N/merge), which are never protected branches. A protected-branches-only
# policy would reject every PR run instead of waiting for the owner's approval.
gh api --method PUT "repos/$REPO/environments/owner-approval" \
  -H "Accept: application/vnd.github+json" \
  --input - <<JSON
{
  "prevent_self_review": false,
  "reviewers": [{"type":"User","id":$OWNER_ID}],
  "deployment_branch_policy": null
}
JSON

echo "Configuring protected branch $BRANCH"
protection_file="$(mktemp)"
trap 'rm -f "$protection_file"' EXIT
cat > "$protection_file" <<JSON
{
  "required_status_checks": {
    "strict": true,
    "contexts": ["owner-approval"]
  },
  "enforce_admins": true,
  "required_pull_request_reviews": null,
  "restrictions": null,
  "required_linear_history": true,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "block_creations": false,
  "required_conversation_resolution": true,
  "lock_branch": false,
  "allow_fork_syncing": false
}
JSON

gh api --method PUT "repos/$REPO/branches/$BRANCH/protection" \
  -H "Accept: application/vnd.github+json" \
  --input "$protection_file"

echo "Owner-authority repository boundary configured."
echo "Do not add bypass actors to the protected branch."
