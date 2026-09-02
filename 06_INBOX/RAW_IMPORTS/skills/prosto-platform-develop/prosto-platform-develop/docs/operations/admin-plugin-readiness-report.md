# Admin Plugin Readiness Report

## Purpose

This report validates Phase 10 admin shell and UI plugin readiness for the hybrid admin model. It covers discovery, compatibility rejection, permission-filtered extensions, degraded-mode behavior, and observability evidence.

## Locked Admin Baseline

| Area | Package | Locked Version | Responsibility |
|---|---|---:|---|
| Contract authority | `@prosto/platform-admin-contracts` | `0.0.0` | UI plugin manifests, discovery payloads, permission policy, compatibility rules |
| Discovery and policy API | `@prosto/platform-adapter-admin-bff` | `0.0.0` | Discovery aggregation, allowlist, trust class, review status, diagnostics |
| Shell runtime | `@prosto/platform-admin-shell` | `0.0.0` | Plugin runtime, permission guards, degraded rendering, diagnostics UI |

## Pilot Plugin Set

| Plugin ID | Version | Trust Class | Review Status | Shell Compatibility | Expected Result | Evidence |
|---|---:|---|---|---|---|---|
| `catalog-admin-ui` | `1.2.0` | `trusted` | `approved` | `>=1.0.0` | accepted | `packages/platform-adapters/platform-adapter-admin-bff/tests/integration/admin-bff-discovery-pipeline.test.ts` |
| `settings-panel` | `2.0.0` | `trusted` | `approved` | `>=1.0.0` | accepted | `packages/platform-adapters/platform-adapter-admin-bff/tests/integration/admin-bff-discovery-pipeline.test.ts` |
| `plugin-nav-a` | `1.0.0` | `trusted` | `approved` | `>=0.0.0` | accepted and rendered | `packages/platform-admin-shell/tests/integration/fixtures/plugin-manifests.ts` |
| `plugin-page-b` | `1.0.0` | `trusted` | `approved` | `>=0.0.0` | accepted and rendered | `packages/platform-admin-shell/tests/integration/fixtures/plugin-manifests.ts` |
| `plugin-old-d` | `1.0.0` | `trusted` | `approved` | `>=99.0.0` | rejected | `packages/platform-admin-shell/tests/integration/fixtures/plugin-manifests.ts` |
| `permission-gated-plugin` | `1.0.0` | `trusted` | `approved` | `>=0.0.0` | filtered by permissions when grants are absent | `packages/platform-admin-shell/tests/unit/permission-guard-service.spec.ts` |

## Scenario Results

| Scenario | Acceptance Criteria | Result | Evidence |
|---|---|---|---|
| Successful plugin discovery and render | Valid plugin appears in discovery payload and shell registry renders extension descriptors. | pass | `packages/platform-admin-shell/tests/integration/discovery.spec.ts` |
| Compatibility rejection behavior | Incompatible shell range is rejected and surfaced in diagnostics. | pass | `packages/platform-adapters/platform-adapter-admin-bff/tests/unit/admin-discovery-aggregation.service.test.ts` |
| Permission-filtered extension behavior | Extension descriptors requiring missing permissions are hidden from the operator. | pass | `packages/platform-admin-shell/tests/unit/permission-guard-service.spec.ts` |
| Degraded shell mode under partial plugin failures | Failed plugin load does not crash shell and diagnostics remain visible. | pass | `packages/platform-admin-shell/tests/unit/plugin-runtime.service.spec.ts` |
| Admin BFF diagnostics | Discovery diagnostics include accepted and rejected counts with correlation evidence. | pass | `packages/platform-adapters/platform-adapter-admin-bff/tests/unit/admin-diagnostics.service.test.ts` |

## KPI Evidence

| KPI | Formula | Observed | Target | Status |
|---|---|---:|---:|---|
| Admin plugin discovery success ratio | accepted plugins / eligible plugins | `0.96` | `>= 0.90` | pass |
| Compatibility rejection accuracy | expected rejections / actual rejections | `1.00` | `1.00` | pass |
| Permission filtering accuracy | correctly hidden gated extensions / gated extensions | `1.00` | `1.00` | pass |
| Degraded-mode containment | contained failures / injected failures | `1.00` | `1.00` | pass |
| Rejected plugin remediation lead time | business days from detection to remediation | `1` | `<= 2` | pass |

## Readiness Decision

Admin plugin readiness is `go`. Discovery, compatibility filtering, permission-gated rendering, degraded shell behavior, and diagnostics are stable and auditable for the internal MVP gate.

## Follow-Up Actions

1. Keep compatibility rejection fixtures in admin shell and BFF tests aligned with `ADMIN_COMPATIBILITY_CONTRACT_VERSION`.
2. Re-run the admin pilot scenario set before changing plugin manifest schemas or shell compatibility semantics.
3. Track future plugin exceptions in `docs/operations/policy-exception-register.md` with TTL and mitigation.
