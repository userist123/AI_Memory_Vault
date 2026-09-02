# Policy Exception Register

## Purpose

This register records time-bound Phase 10 policy exceptions and remediations. It ensures that no exception bypasses production-like staging controls without owner, mitigation, expiry, and closure evidence.

## Exception Policy

An exception is valid only when all fields are present:

1. Unique ID.
2. Scope and affected control.
3. Owner and approver.
4. Reason and mitigation.
5. `expires_at` value.
6. Closure or renewal decision before expiry.

## Register

| ID | Scope | Control | Owner | Approver | Reason | Mitigation | Expires At | Status | Evidence |
|---|---|---|---|---|---|---|---|---|---|
| `EX-10-001` | admin-plugin-compatibility-policy | shell compatibility range | admin-platform-owner | architecture-owner | Candidate plugin required `>=99.0.0` during pilot rejection scenario. | Keep plugin rejected from discovery payload, document remediation, and retest after manifest correction. | 2026-07-31 | closed-remediated | `docs/operations/admin-plugin-readiness-report.md` |
| `EX-10-002` | staging-artifact-integrity | strict module checksum lock | core-runtime-owner | security-team | One pilot artifact checksum was stale after artifact regeneration. | Replace staged artifact with locked checksum evidence and re-run strict startup validation. | 2026-07-24 | closed-remediated | `docs/operations/incident-register.md` (`INC-10-001`) |

## Active Exception Summary

| Status | Count | Gate Impact |
|---|---:|---|
| `approved-with-mitigation` | 0 | none |
| `closed-remediated` | 2 | none |
| `expired` | 0 | none |

No active exception remains at the Phase 10 gate decision point.

## Review Cadence

- Review active exceptions weekly during hardening and pilot windows.
- Escalate any exception within two business days of expiry to the approving owner.
- Convert repeated exceptions into backlog items with control updates, not permanent bypasses.
