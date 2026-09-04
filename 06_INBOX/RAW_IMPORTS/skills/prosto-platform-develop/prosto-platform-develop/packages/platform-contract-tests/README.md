# @prosto/platform-contract-tests

Reusable Phase 04 contract conformance suite for Prosto modules.

## Scope
- Reusable entrypoint [`createModuleContractTests`](./src/create-module-contract-tests.ts)
- Programmatic runner [`runModuleContractConformance`](./src/create-module-contract-tests.ts)
- Standardized failure taxonomy via [`ContractFailureCodes`](src/constants/index.ts)
- Deterministic machine-readable report output via [`toConformanceReportJson`](src/utils/report.utils.ts)

## Conformance Checks
- Manifest conformance (schema + semantic)
- Lifecycle method behavior (`register/init/start/stop`)

## Commands
- `npm run --workspace @prosto/platform-contract-tests build`
- `npm run --workspace @prosto/platform-contract-tests typecheck`
- `npm run --workspace @prosto/platform-contract-tests test`

## Public API

### Entry Points
- `createModuleContractTests` — reusable test-entry helper for module repositories
- `runModuleContractConformance` — programmatic runner returning a machine-readable report

### Utilities
- `buildConformanceSummary` — builds deterministic conformance summary from check results
- `buildConformanceReport` — builds a complete conformance report with summary
- `toConformanceReportJson` — serializes report in deterministic JSON for CI consumers

### Utilities
- `DefaultModuleLifecycleContextFactory` — default factory for module lifecycle context in conformance tests

### Constants
- `ContractFailureCodes` — standardized failure code taxonomy for CI consumers

## Usage
```ts
import { describe, it } from 'vitest';
import { createModuleContractTests } from '@prosto/platform-contract-tests';

describe('MyModule contract', () => {
  createModuleContractTests(
    { module: myModuleInstance },
    { describe, it },
  );
});
```
