# R001 Git History Secret Cleanup Procedure

Status: `PENDING_OWNER_APPROVAL`  
Evidence level: `DESIGN_ONLY`

## Scope

Clean tracked secret material from Git history after external credential rotation/revocation has been confirmed. This procedure is intentionally not executable automatically and must not perform a force-push without explicit owner approval.

## Preconditions

1. Every incident in `R001_SECRET_INCIDENT_INVENTORY.md` has external provider evidence of `REVOKED`, `ROTATED`, or `INVALIDATED`, or is explicitly marked `NOT_APPLICABLE`.
2. A new backup/mirror of the repository is stored outside the working tree.
3. The exact refs to retain are inventoried: branches, tags and release refs.
4. The owner explicitly approves history rewrite and force-push.

## Proposed procedure

```text
git clone --mirror <repository> <repo>.git
cd <repo>.git

# Generate a redacted incident-driven paths/objects map first.
# Use git filter-repo with an approved path/blob replacement plan.
# Never paste secret values into the plan.

git fsck --full
# run gitleaks against all refs after rewrite
# verify expected refs and commit ancestry

git push --force --mirror <repository>
```

The exact `git filter-repo` invocation is intentionally filled only after the incident inventory identifies affected paths/objects. Do not substitute a guessed command.

## Verification after rewrite

- `git fsck --full` reports no unexpected dangling secret-bearing objects that are still reachable from maintained refs.
- Gitleaks scans all refs/tags and returns zero findings.
- Current working-tree scan returns zero findings.
- Required protected branches/tags still exist.
- A clean clone can reproduce the expected repository state at the published SHA.

## Abort conditions

Abort without force-push when external rotation evidence is missing, scan results are non-zero, protected refs cannot be inventoried, or the rewrite would remove required forensic evidence.
