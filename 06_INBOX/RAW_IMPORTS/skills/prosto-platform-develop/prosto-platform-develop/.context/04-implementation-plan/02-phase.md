# Phase 02 - Monorepo Package Skeleton and Contract Surface Setup

## Execution Status
- Status: Completed
- Completed on: 2026-03-29
- Repository evidence:
  - `packages/platform-sdk/package.json`
  - `packages/platform-core/package.json`
  - `packages/platform-contract-tests/package.json`
  - `packages/platform-cli/package.json`
   - `packages/platform-adapters/platform-adapter-http/package.json`
  - `tsconfig.base.json`
  - `docs/architecture/dependency-map.md`
  - `scripts/lint-architecture.mjs`
  - `scripts/validate-dependency-policy.mjs`
  - `scripts/validate-module-graph.mjs`
  - `scripts/validate-public-api-boundary.mjs`

## Phase Objective
Create the concrete repository structure and package scaffolding required by the architecture blueprint so implementation can proceed with enforced boundaries instead of root-level coupling.

## Scope Boundaries
### In Scope
- Introduce `packages/` workspace layout.
- Create minimal package skeletons for:
  - `@prosto/platform-sdk`
  - `@prosto/platform-core`
  - `@prosto/platform-contract-tests`
  - `@prosto/platform-cli`
  - `@prosto/platform-adapter-http`
- Add shared TypeScript and lint configuration baseline.
- Move dependency responsibilities away from root where boundaries require it.

### Out of Scope
- Full runtime feature implementation.
- Full CLI command implementation.
- Full adapter routing implementation.

## Prerequisites and Dependencies
- Phase 01 governance gates and CI skeleton are in place.
- Package topology defined in `.context/02-architecture-design/04-package-structure-blueprint.md`.
- Branching and release process from `.context/02-architecture-design/05-git-branching-strategy.md`.

## Detailed Ordered Implementation Steps
1. Convert root project to workspace monorepo model:
   - add `workspaces` in root `package.json`
   - keep root scripts for orchestration only
2. Create directory skeleton:
   - `packages/platform-sdk/src`
   - `packages/platform-core/src`
   - `packages/platform-contract-tests/src`
   - `packages/platform-cli/src`
    - `packages/platform-adapters/platform-adapter-http/src`
3. Add per-package `package.json` with explicit package names and private/public settings.
4. Add `tsconfig.base.json` at root and per-package `tsconfig.json` that extends base.
5. Move HTTP/security middleware dependencies from root to adapter package scope where applicable.
6. Add placeholder exports and entry points for each package.
7. Add architecture dependency map document and wire boundary validation scripts:
   - `validate:module-graph`
   - `validate:public-api-boundary`
8. Ensure root build and typecheck scripts target all workspaces consistently.

## Code Examples
### Example: root workspace package configuration
```json
{
  "private": true,
  "workspaces": [
    "packages/*"
  ],
  "scripts": {
    "build": "npm run -ws build",
    "typecheck": "npm run -ws typecheck"
  }
}
```

### Example: SDK package entry point
```typescript
export * from './types/manifest.types.js';
export * from './interfaces/platform-module.interface.js';
export * from './tokens/service.tokens.js';
```

### Example: base TS config
```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "strict": true,
    "declaration": true,
    "sourceMap": true
  }
}
```

## Affected Modules or Files
### Existing files likely updated
- `package.json`
- `.gitignore`
- `.editorconfig`

### New files expected
- `tsconfig.base.json`
- `packages/platform-sdk/package.json`
- `packages/platform-core/package.json`
- `packages/platform-contract-tests/package.json`
- `packages/platform-cli/package.json`
- `packages/platform-adapters/platform-adapter-http/package.json`
- per-package `src/index.ts`

## Validation and Testing Approach
- Validate workspace install and lockfile integrity.
- Run root typecheck and build across workspaces.
- Verify dependency boundary rules with architecture validation scripts.
- Validate each package resolves ESM imports correctly.

## Data or Migration Impact
- No runtime business data migration.
- Repository structure migration from single-package root to multi-package workspace.

## Risks and Mitigations
- Risk: package boundary drift due to root scripts still compiling local assumptions.
  - Mitigation: enforce workspace-level build and ban deep relative imports across packages.
- Risk: dependency move causes temporary build breakages.
  - Mitigation: migrate one dependency group at a time and validate per package.

## Rollback Approach
- Keep migration in isolated branch.
- If workspace migration destabilizes build pipeline, revert to pre-workspace root package commit and reapply incrementally.
- Preserve package skeleton as non-default branch reference.

## Completion Criteria
- `packages/` structure exists and is buildable.
- Root project orchestrates workspace build/typecheck successfully.
- Dependency ownership between core, SDK, adapter is documented and enforceable.
- Root-level dependency scope no longer mixes adapter responsibilities into core baseline.
