#!/usr/bin/env bash
set -euo pipefail

REPO="${1:-userist123/AI_Memory_Vault_CODEX_READY}"
BRANCH="${2:-main}"
OWNER_ID="${3:-199154571}"

command -v gh >/dev/null || { echo "gh CLI is required."; exit 1; }
gh auth status >/dev/null

echo "Configuring owner-approval environment for $REPO"
gh api --method PUT "repos/$REPO/environments/owner-approval" \
  -H "Accept: application/vnd.github+json" \
  --input - <<JSON
{
  "prevent_self_review": false,
  "reviewers": [{"type":"User","id":$OWNER_ID}],
  "deployment_branch_policy": {
    "protected_branches": true,
    "custom_branch_policies": false
  }
}
JSON

echo "Configuring protected branch $BRANCH"
cat > /tmp/mv-branch-protection.json <<JSON
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
  --input /tmp/mv-branch-protection.json

rm -f /tmp/mv-branch-protection.json

echo "Owner-authority repository boundary configured."
echo "Do not add bypass actors to the protected branch."
