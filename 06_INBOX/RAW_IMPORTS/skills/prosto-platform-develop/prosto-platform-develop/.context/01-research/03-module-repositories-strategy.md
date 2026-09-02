# 03 - Module Repositories Strategy

Date: 2026-03-23

## Goal
Support plugin modules in separate GitHub repositories while keeping the core stable, secure, and maintainable.

## Recommended Repository Model

1. Platform repositories:
   - `prosto-platform` (monorepo: sdk, core, cli, etc)
2. Module repositories:
   - one repo per module (feature or integration focused)
     - `prosto-module-catalog` (product catalog module)
     - `prosto-module-cart` (shopping cart module)
     - `prosto-module-order` (order module)
     - `prosto-module-{name}` (other modules)
   - semantic versioning and independent release cadence

## Distribution Options
- Primary: npm packages (public or private registry).
- Secondary: GitHub Packages for controlled private module distribution.
- Avoid runtime loading directly from Git URLs in production.

## Module Manifest Policy
Each module repo ships a manifest (in package export or sidecar file):
- module id
- module title
- module version
- compatible platform version
- optional dependencies on other modules
- security classification (`trusted`, `internal`, `third-party-reviewed`)

## Compatibility And Versioning
- Core and SDK should follow semver strictly.
- Module should define:
  - `peerDependencies` on SDK
  - tested core version range
- Breaking contract changes require:
  - major version bump in SDK/core
  - migration notes and compatibility matrix update

## Module Loading Security
- Default to explicit allowlist in platform config.
- Require checksum/signature verification for production module artifacts.
- Keep module execution sandbox policy explicit (trusted vs isolated).
- Block modules with missing compatibility metadata.

## Recommended CI For Module Repos
- Typecheck, unit tests, integration contract tests.
- Verify manifest shape and schema.
- Build output validation for ESM/CJS policy (if dual support is needed).
- Publish only from tagged releases.

## Contract Testing Between Core And Module Repos
- Provide shared contract test package:
  - `@prosto/platform-contract-tests`
- Module CI runs these tests against declared core compatibility versions.
- Prevent release if contract tests fail.

## Suggested Module Repository Template
- `src/`
- `tests/`
- `module.manifest.json` (or equivalent exported manifest)
- `README.md` (capabilities, config, compatibility)
- `CHANGELOG.md`
- `.github/workflows/release.yml`

## Operational Registry Document
Maintain a central module catalog in core repo:
- module id
- current stable version
- support status
- compatibility matrix
- security review status

Continue with: [04 - Framework And Library Evaluation](./04-framework-and-library-evaluation.md).
