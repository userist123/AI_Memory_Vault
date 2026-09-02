# Phase 08 - Admin BFF Adapter and Discovery Pipeline

**Status**: Completed (2026-06-08)

## Phase Objective
Implement `@prosto/platform-adapter-admin-bff` to provide policy-aware admin APIs, UI plugin discovery aggregation, compatibility filtering, and diagnostics required by hybrid admin model.

## Scope Boundaries
### In Scope
- Package `@prosto/platform-adapter-admin-bff`.
- Discovery endpoint and aggregation pipeline for admin shell.
- Permission-aware operation mapping and response filtering.
- Compatibility and allowlist enforcement for UI plugin discovery.
- Structured diagnostics output for rejected plugins and policy failures.

### Out of Scope
- Shell-side rendering runtime.
- Full operator-facing UI implementation.
- Non-admin transport adapters unrelated to BFF flow.

## Prerequisites and Dependencies
- Phase 05 core runtime baseline completed.
- Phase 06 security controls available for allowlist and integrity checks.
- Phase 07 admin contracts package completed and versioned.
- ADR references:
  - `.context/02-architecture-design/adr/ADR-0003-module-loading-security-allowlist-integrity.md`
  - `.context/02-architecture-design/adr/ADR-0007-observability-and-operability-baseline.md`
  - `.context/02-architecture-design/adr/ADR-0009-admin-ui-hybrid-shell-plugin-model.md`

## Detailed Ordered Implementation Steps
1. Create package scaffold `packages/platform-adapters/platform-adapter-admin-bff`.
2. Implement BFF routing layer for admin shell operations:
   - plugin discovery route
   - permission-aware action route
   - health and diagnostics route
3. Implement discovery aggregation service:
   - pull plugin manifests from registry/catalog sources
   - validate manifests using `platform-admin-contracts`
   - evaluate compatibility against shell and contract versions
4. Integrate policy checks:
   - allowlist validation
   - trust class filtering
   - integrity and review-state checks
5. Implement permission mapping:
   - map operator roles to allowed extension actions
   - filter response payload by permission scope
6. Implement diagnostics payloads:
   - accepted plugins
   - rejected plugins with `reasonCode` and `remediationHint`
   - correlation metadata for incident triage
7. Add integration tests for:
   - compliant plugin discovery
   - rejected plugin diagnostics
   - role-based filtering outcomes
8. Add observability instrumentation for admin API and discovery lifecycle.

## Code Examples
### Example: discovery aggregation flow
```typescript
const manifests = await pluginCatalog.fetchUIPluginManifests();
const validated = manifests.map(validateAdminPluginManifest);
const compatibility = validated.map(checkAdminPluginCompatibility);
const policyResults = compatibility.map(applyAdminPluginPolicy);

return buildAdminDiscoveryPayload(policyResults, operatorContext);
```

### Example: policy-aware filtering
```typescript
if (!allowlist.isApproved(plugin.id, plugin.version)) {
  reject(plugin, 'ALLOWLIST_REJECTED', 'approve plugin in policy catalog');
}

if (!permissions.canAccess(operatorRoles, plugin.requiredPermissions)) {
  hide(plugin, 'PERMISSION_FILTERED');
}
```

### Example: diagnostics entry
```json
{
  "pluginId": "catalog-admin-ui",
  "status": "rejected",
  "reasonCode": "CONTRACT_VERSION_MISMATCH",
  "remediationHint": "upgrade plugin contract to supported range",
  "correlationId": "adm-req-001"
}
```

## Affected Modules or Files
### Existing files likely updated
- `packages/platform-adapters/platform-adapter-http/*`
- `.context/02-architecture-design/c4/02-container-view.md`

### New files expected
- `packages/platform-adapters/platform-adapter-admin-bff/package.json`
- `packages/platform-adapters/platform-adapter-admin-bff/tsconfig.json`
- `packages/platform-adapters/platform-adapter-admin-bff/src/index.ts`
- `packages/platform-adapters/platform-adapter-admin-bff/src/routes/*.ts`
- `packages/platform-adapters/platform-adapter-admin-bff/src/discovery/*.ts`
- `packages/platform-adapters/platform-adapter-admin-bff/src/permissions/*.ts`
- `packages/platform-adapters/platform-adapter-admin-bff/src/diagnostics/*.ts`
- `packages/platform-adapters/platform-adapter-admin-bff/test/integration/*.test.ts`

## Validation and Testing Approach
- Integration tests for discovery with mixed valid and invalid plugin sets.
- Policy tests for allowlist and trust class enforcement.
- Permission matrix tests for role-based filtering.
- Contract compatibility tests with `platform-admin-contracts`.
- Diagnostics schema validation and required-field checks.

## Data or Migration Impact
- No business data migration.
- Operational migration: admin discovery shifts from static configuration to policy-driven dynamic payloads.

## Risks and Mitigations
- Risk: BFF becomes a hidden core and accumulates domain logic.
  - Mitigation: keep domain behavior in modules and keep BFF as aggregation and policy layer only.
- Risk: diagnostics volume creates noisy operational signals.
  - Mitigation: structured reason taxonomy and bounded cardinality for labels.

## Rollback Approach
- Roll back BFF discovery feature behind route-level feature flags.
- Fall back to static plugin registry snapshot in staging if live discovery is unstable.
- Preserve rejected-plugin diagnostics for post-incident analysis.

## Completion Criteria
- `@prosto/platform-adapter-admin-bff` package exists and is integrated.
- Discovery payloads are contract-valid and permission-aware.
- Rejected plugins produce structured diagnostics with reason taxonomy.
- Integration and policy tests pass on protected branches.
