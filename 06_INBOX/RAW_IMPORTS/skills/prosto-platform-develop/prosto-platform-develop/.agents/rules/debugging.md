# Debugging Rules

## Repository State Awareness

- In current repository state, lint and architecture-policy commands plus `test:contracts`, `validate:runtime-policy` and `test:lifecycle-determinism` are available in root `package.json`.
- If a task asks to run a single test, check package-level scripts first (for example `@prosto/platform-sdk`), because there is no root-level single-test entrypoint.
- Many architecture constraints are documented under `.context/02-architecture-design/*` and may not map to real runtime code paths yet.
- Treat failures due to non-existent `packages/*` paths as repository-state mismatch unless those directories are actually added.
- If import/runtime issues appear after adding JS/TS files, first validate ESM assumptions because root package is `"type": "module"`.

## Debugging Workflow

1. **Check repository state first** — verify which files and scripts actually exist before assuming capabilities
2. **Validate ESM** — if import errors appear, check that `.js` extensions are used in relative imports
3. **Check package boundaries** — if cross-package imports fail, verify the dependency is allowed per architecture rules
4. **Use architecture validation scripts** — run `npm run lint:architecture` and `npm run validate:dependency-policy` to catch boundary violations
5. **Check test isolation** — if tests fail, verify mock setup and test data cleanup

## Common Pitfalls

- Confusing `.context/` architecture docs (target state) with implemented code (current state)
- Using `npm install` instead of `npm ci` in CI environments
- Forgetting `.js` extension in ESM relative imports
- Importing from `platform-core` in adapters (boundary violation)
- Importing between modules (coupling violation)
- Assuming commands exist without checking `package.json`
