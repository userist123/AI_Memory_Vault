# Compatibility Matrix

## Scope

This matrix tracks Phase 04 contract conformance between the SDK baseline and internal reference modules.

## Baseline Record

```yaml
sdk_version: 0.0.0
contract_suite_package: @prosto/platform-contract-tests@0.0.0
generated_at: 2026-03-31T00:00:00.000Z
modules:
  - id: module-health
    version: 1.0.0
    result: pass
    mandatory_failures: 0
    advisory_failures: 0
  - id: module-auth
    version: 1.0.0
    result: pass
    mandatory_failures: 0
    advisory_failures: 0
```

## Phase 10 Internal MVP Locked Baseline

```yaml
pilot_window: 2026-q3-internal-mvp
decision: go
runtime:
  core_package: @prosto/platform-core@0.0.0
  sdk_package: @prosto/platform-sdk@0.0.0
  contract_suite_package: @prosto/platform-contract-tests@0.0.0
  persistence_adapter_package: @prosto/platform-adapter-typeorm@0.0.0
modules:
  - id: module-health
    package: @examples/module-health
    version: 0.0.0
    contract_result: pass
    startup_modes:
      strict: pass
      best_effort: pass
  - id: module-auth
    package: @examples/module-auth
    version: 0.0.0
    contract_result: pass
    startup_modes:
      strict: pass
      best_effort: pass
admin:
  contracts_package: @prosto/platform-admin-contracts@0.0.0
  bff_package: @prosto/platform-adapter-admin-bff@0.0.0
  shell_package: @prosto/platform-admin-shell@0.0.0
  plugin_discovery_success_ratio: 0.96
  compatibility_rejection_accuracy: 1.00
evidence:
  gate_report: docs/operations/internal-mvp-gate-report.md
  admin_readiness: docs/operations/admin-plugin-readiness-report.md
  incidents: docs/operations/incident-register.md
  exceptions: docs/operations/policy-exception-register.md
```

## Failure Taxonomy Source

Failure codes are defined by `@prosto/platform-contract-tests` and exported as `ContractFailureCodes`.

Current baseline codes:
- `CT_MANIFEST_SCHEMA_INVALID`
- `CT_MANIFEST_SEMANTIC_INVALID`
- `CT_LIFECYCLE_METHOD_MISSING`
- `CT_LIFECYCLE_METHOD_FAILED`

## Update Rule

Update this matrix whenever one of the following changes:
1. SDK version used by contract suite.
2. Contract suite behavior or failure taxonomy.
3. Reference module manifest/lifecycle behavior.
4. CI contract gate result on protected branches.
5. Phase 10 locked pilot module or admin plugin set changes.
