# R001 Secret Incident Inventory

Status: OPEN / PENDING OWNER VERIFICATION  
Evidence level: `CLAIMED_ONLY` for incident existence; no secret values are stored here.

This register records secret categories reported by the R001 remediation brief. The repository work cannot revoke or rotate credentials in external provider accounts. Each item therefore remains `PENDING` until the owner provides independent evidence of revocation/rotation.

| Incident ID | Secret type | Reported remediation status | Required owner evidence | Residual risk |
|---|---|---|---|---|
| SEC-R001-001 | GitHub OAuth access token | PENDING | provider revocation/rotation confirmation | credential may remain valid outside current tree/history |
| SEC-R001-002 | GitHub refresh token | PENDING | provider revocation/rotation confirmation | credential may remain valid outside current tree/history |
| SEC-R001-003 | Google API key | PENDING | key disabled/replaced confirmation | external key may remain usable |
| SEC-R001-004 | Google OAuth client ID / client secret | PENDING | client secret rotation confirmation | OAuth credential may remain usable |
| SEC-R001-005 | Telegram bot token | PENDING | regenerated bot token confirmation | bot control may remain exposed |
| SEC-R001-006 | Slack incoming webhook | PENDING | webhook revoked/recreated confirmation | webhook may remain callable |
| SEC-R001-007 | LinkedIn client secret | PENDING | application secret rotation confirmation | client credential may remain usable |
| SEC-R001-008 | MongoDB Atlas credential / URI | PENDING | database credential rotation and old-credential invalidation | unauthorized database access remains possible |
| SEC-R001-009 | Docker Swarm join token | PENDING | token rotation/revocation confirmation | unauthorized node enrollment risk |
| SEC-R001-010 | Docker Swarm unlock key | PENDING | unlock key rotation confirmation | encrypted swarm secrets may remain exposed |

## Evidence handling

Only redacted identifiers, file paths, affected SHAs and status values belong in this register. Never paste the secret itself, even into private-looking Markdown tracked by Git.

## Closure rule

An item may move from `PENDING` to `REVOKED`, `ROTATED`, or `INVALIDATED` only after owner-provided evidence is recorded. Removing a token from the working tree does not prove revocation.

## History cleanup

Repository history cleanup remains a separate change-control action. The intended procedure is a complete-history rewrite using `git filter-repo` or an equivalent approved mechanism, followed by a force-push only after explicit owner approval. Before approval, no force-push is performed.

After an approved rewrite, scan all branches/tags/refs again and attach exact-SHA scan evidence to the final R001 report.


## 🔗 Legături Sinaptice
- [[04 Security Integrity Map]]
- [[Security_Practices]]
- [[Knowledge Graph Home]]
