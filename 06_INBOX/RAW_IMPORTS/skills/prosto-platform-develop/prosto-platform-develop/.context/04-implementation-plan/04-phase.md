# Phase 04 - Contract Conformance Test Package and Reference Module Validation

## Execution Status
- Status: Completed
- Completed on: 2026-03-31
- Validation date: 2026-03-31
- Repository evidence:
  - `packages/platform-contract-tests/src/create-module-contract-tests.ts`
  - `packages/platform-contract-tests/src/checks/manifest.check.ts`
  - `packages/platform-contract-tests/src/checks/lifecycle.check.ts`
  - `packages/platform-contract-tests/src/interfaces/index.ts`
  - `packages/platform-contract-tests/src/utils/report.utils.ts`
  - `packages/platform-contract-tests/tests/module-contract-conformance.test.ts`
  - `examples/module-health/tests/contracts.test.ts`
  - `examples/module-auth/tests/contracts.test.ts`
  - `docs/compatibility/compatibility-matrix.md`
  - `package.json` (`test:contracts`)

## Phase Objective
Implement `@prosto/platform-contract-tests` as a reusable conformance suite and validate it against at least two internal reference modules.

## Scope Boundaries
### In Scope
- Build contract test harness package.
- Provide reusable test entry for module repositories.
- Define compatibility matrix baseline and failure taxonomy.
- Validate internal reference modules against SDK contract.

### Out of Scope
- Full ecosystem rollout to third-party modules.
- Production runtime deployment.
- Advanced chaos/performance testing.

## Prerequisites and Dependencies
- Phase 03 SDK contract baseline completed and versioned.
- Architecture testing guidance from `.context/02-architecture-design/adr/ADR-0008-test-strategy-contract-testing-and-quality-gates.md`.
- Domain lifecycle model from `.context/02-architecture-design/02-domain-and-capability-model.md`.

## Detailed Ordered Implementation Steps
1. Implement `createModuleContractTests` entry in `platform-contract-tests`.
 2. Add suites for:
    - manifest conformance
    - lifecycle method behavior
3. Define standardized failure codes and test output format for CI consumers.
4. Create two internal reference modules in `examples/`:
   - `module-health`
   - `module-auth`
5. Integrate contract tests in each reference module CI flow.
6. Publish compatibility matrix document tying SDK version to module test result.
7. Add migration template for future contract-breaking changes.

## Code Examples
### Example: reusable contract test entry
```typescript
import { createModuleContractTests } from '@prosto/platform-contract-tests';
import { HealthModule } from './health.module.js';

describe('HealthModule contract', () => {
  createModuleContractTests(new HealthModule());
});
```

### Example: conformance suite shape
```typescript
export function createModuleContractTests(module: PlatformModule): void {
  describe('manifest', () => {
    it('should satisfy schema', () => { /* ... */ });
  });

  describe('lifecycle', () => {
    it('should expose register/init/start/stop', () => { /* ... */ });
  });
};
```

### Example: compatibility matrix record
```yaml
sdk_version: 0.1.x
modules:
  - id: module-health
    result: pass
  - id: module-auth
    result: pass
```

## Affected Modules or Files
### Existing files likely updated
- `packages/platform-contract-tests/package.json`
- `packages/platform-sdk/README.md`

### New files expected
- `packages/platform-contract-tests/src/index.ts`
- `packages/platform-contract-tests/src/manifest/*.ts`
- `packages/platform-contract-tests/src/lifecycle/*.ts`
- `packages/platform-contract-tests/src/security/*.ts`
- `examples/module-health/*`
- `examples/module-auth/*`
- `docs/compatibility/compatibility-matrix.md`

## Validation and Testing Approach
- Run contract suite against compliant and intentionally broken fixtures.
- Ensure CI fails on any contract violation.
- Validate test output is deterministic and machine-readable.
- Confirm reference modules pass full suite on protected branches.

## Data or Migration Impact
- No runtime data migration.
- Contract migration support introduced via compatibility matrix and migration template.

## Risks and Mitigations
- Risk: contract tests become too strict and block harmless evolution.
  - Mitigation: classify checks into mandatory versus advisory with explicit policy.
- Risk: module teams bypass suite due to setup friction.
  - Mitigation: provide single-command test bootstrap and template integration.

## Rollback Approach
- If strict checks block critical release, temporarily downgrade selected checks to warning with documented exception and expiry.
- Restore strict mode after patching failing module or contract mismatch.

## Completion Criteria
- Contract test package is reusable and published internally.
- Two reference modules pass mandatory conformance checks.
- Compatibility matrix exists and is referenced by CI/release process.
- Failure taxonomy is documented and actionable.
