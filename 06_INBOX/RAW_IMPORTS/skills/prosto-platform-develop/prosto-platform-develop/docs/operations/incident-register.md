# Incident Register

## Purpose

This register captures Phase 10 internal MVP pilot incidents, root causes, owners, corrective actions, and closure evidence. It is the audit trail for severity-high issues and for confirming that no unresolved pilot incident blocks the internal MVP gate.

## Severity Model

| Severity | Definition | Gate Impact |
|---|---|---|
| `critical` | Data loss, unauthorized access, or platform-wide outage | automatic no-go until closed |
| `high` | Failed strict startup, broken admin discovery, or missing mandatory diagnostics | no-go until RCA and corrective action are complete |
| `medium` | Degraded behavior with working mitigation and complete diagnostics | allowed only with owner and due action |
| `low` | Cosmetic, documentation, or non-blocking observability issue | tracked outside gate decision |

## Register

| ID | Detected In | Severity | Scope | Owner | Status | Due Date | RCA | Corrective Action | Evidence |
|---|---|---|---|---|---|---|---|---|---|
| `INC-10-001` | `P10-C3` | `high` | strict startup pilot | core-runtime-owner | closed | 2026-07-24 | Checksum metadata in one staged module artifact was regenerated after pilot lock, causing a strict-mode rejection. | Rebuilt artifact from locked source, refreshed checksum evidence, and re-ran strict startup cycle. | `examples/module-health/artifacts/module-health-0.0.0.zip.sha256`, `npm run validate:runtime-policy` |
| `INC-10-002` | `P10-C2` | `medium` | admin plugin discovery | admin-platform-owner | closed | 2026-07-24 | One candidate UI plugin used an incompatible `shellCompatibility` range and was correctly rejected. | Added remediation guidance to readiness report and confirmed rejected plugin lead time. | `docs/operations/admin-plugin-readiness-report.md`, `docs/operations/policy-exception-register.md` |
| `INC-10-003` | `P10-C4` | `medium` | degraded shell mode | admin-platform-owner | closed | 2026-07-24 | Partial plugin load failure exercised degraded shell path without user-facing crash. | Verified degraded diagnostics panel and runtime isolation behavior. | `packages/platform-admin-shell/tests/unit/plugin-runtime.service.spec.ts`, `packages/platform-admin-shell/tests/unit/diagnostics-store.spec.ts` |

## Open Incident Summary

| Severity | Open Count | Gate Status |
|---|---:|---|
| `critical` | 0 | pass |
| `high` | 0 | pass |
| `medium` | 0 | pass |
| `low` | 0 | pass |

## RCA Requirements

Every `critical` and `high` incident must include:

1. Triggering cycle and scope.
2. Root cause statement.
3. Corrective action with owner.
4. Evidence link to a test, gate, artifact, or report.
5. Closure confirmation before a `go` decision.

All Phase 10 high-severity incidents are closed with evidence.
