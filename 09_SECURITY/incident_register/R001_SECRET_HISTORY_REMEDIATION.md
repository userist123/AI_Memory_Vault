# R001 SECRET HISTORY REMEDIATION

Status: PLAN_ONLY / HUMAN_APPROVAL_REQUIRED

This repository has historical secret-scanning findings. No Git history rewrite is performed by R001.

## Safe remediation sequence

1. Create and verify a read-only mirror backup of every relevant branch and tag.
2. Revoke/rotate every exposed credential before rewriting history; never put replacement credentials in this repository.
3. Run `git filter-repo` from the clean mirror with an explicit path/pattern inventory reviewed by the owner.
4. Verify the rewritten history with Gitleaks and provider-side credential checks.
5. Force-push the rewritten refs only after explicit owner approval and an agreed maintenance window.
6. Invalidate/replace local clones and CI caches that may still contain the old objects.
7. Scan all branches and tags again, including refs not visible in the default branch.
8. Verify the post-rewrite object graph, secret-scan result, and deployment configuration.
9. Record the responsible human reviewer and close only after all rotation, rewrite, scan, and downstream verification evidence exists.

## Closure criteria

No historical finding is considered closed merely because a current tree is clean. Closure requires credential rotation/revocation evidence, rewrite evidence when approved, complete-ref scanning, and post-remediation verification.
