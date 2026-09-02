# AI Programming Agents Guidelines

## Project Overview

**prosto-platform** is a headless platform, expandable with plug-in modules and written in TypeScript. This document is the entry point for AI programming assistants. Detailed rules are in `.agents/rules/` directory.

## ⚠️ Current Project Status

**IMPORTANT**: Phase 01 through Phase 10 are fully implemented. Phase 10 delivered internal MVP validation and operability readiness evidence with staging pilot KPI/SLO trend, incident and exception registers, admin plugin readiness, and a formal `go` decision. Phase 09 delivered `@prosto/platform-admin-shell` as the admin UI runtime with plugin runtime, policy-gated rendering, and degraded-mode diagnostics. Phase 08 delivered `@prosto/platform-adapter-admin-bff` with policy-aware admin APIs, UI plugin discovery aggregation, permission mapping, compatibility filtering, diagnostics, and observability instrumentation.

### Current Tooling Availability
- Phase 01 governance workflows are active under `.github/workflows/`
- Phase 02 workspace baseline: `@prosto/platform-sdk`, `@prosto/platform-core`, `@prosto/platform-contract-tests`, `@prosto/platform-cli`, `@prosto/platform-adapter-http`
- Phase 07 admin contracts baseline: `@prosto/platform-admin-contracts`
- Phase 08 admin BFF adapter: `@prosto/platform-adapter-admin-bff` (discovery, permissions, diagnostics, observability)
- Phase 09 admin shell runtime: `@prosto/platform-admin-shell` (Vue 3 SPA, plugin runtime, permission guards, degraded mode)
- Persistence adapter: `@prosto/platform-adapter-typeorm` (shared DataSource, migration locks, descriptor registry)
- Phase 10 operations evidence: `docs/operations/internal-mvp-gate-report.md`, `docs/operations/incident-register.md`, `docs/operations/policy-exception-register.md`, `docs/operations/admin-plugin-readiness-report.md`
- Root/package TypeScript baselines: `packages/platform-utils/tsconfig/base.json`, `packages/*/tsconfig.json`, `packages/platform-adapters/*/tsconfig.json`, `packages/platform-modules/*/tsconfig.json`
- Build: Vite 8 (`vite.config.ts`) with `vite-plugin-dts`
- Governance scripts in root `package.json`: `lint`, `lint:fix`, `lint:architecture`, `validate:dependency-policy`, `validate:module-graph`, `validate:public-api-boundary`, `validate:runtime-policy`, `test:contracts`, `test:lifecycle-determinism`, `release:evidence`
- Test runner: Vitest (`packages/platform-sdk/vitest.config.ts`)
- Admin shell test runner: Vitest (`packages/platform-admin-shell/vitest.config.ts`)

### Architecture Documents Reference
Documents in `.context/` describe **target state**, not current code:
- `.context/01-research/` — Research and analysis
- `.context/02-architecture-design/` — Target architecture (C4, DFD, ADRs)
- `.context/03-work-plan/` — Work plan and recommendations
- `.context/04-implementation-plan/` — 10-phase implementation roadmap with Admin Enablement stream

## Detailed Rules by Topic

All detailed rules are in `.agents/rules/` directory:

| File | Topic |
|------|-------|
| `.agents/rules/architecture.md` | Micro-core architecture, package boundaries (ADR-0001), OOP/SOLID/Clean Architecture, error handling |
| `.agents/rules/contract-first.md` | Contract-first methodology, SDK contract priority, stability levels, breaking changes (ADR-0002), type safety |
| `.agents/rules/typescript.md` | TypeScript configuration, ESM imports, type definitions, import organization |
| `.agents/rules/testing.md` | Vitest, test pyramid, AAA pattern, contract testing, mocking strategy |
| `.agents/rules/security.md` | Module loading security, allowlist (ADR-0003), Zod validation, secret management, trust model |
| `.agents/rules/monorepo.md` | Package boundaries, dependency policy, Turborepo, build orchestration, versioning |
| `.agents/rules/observability.md` | Pino logging, structured logs, startup report, health/readiness, metrics (ADR-0007) |
| `.agents/rules/debugging.md` | Debug workflow, repository state awareness, common pitfalls |
| `.agents/rules/ai-behavior.md` | AI agent behavior, first-time scan, rule precedence, command policy |

## Key Rules Summary

### Architecture
- **Micro-core architecture**: minimal platform core, expansion through plug-in modules
- **Contract-first**: define types in `platform-sdk` BEFORE implementing in `platform-core`
- **Package boundaries**: `platform-core` MUST NOT import from adapters or modules; modules MUST NOT import from other modules
- **Current vs Target state**: `.context/` docs describe target architecture, not implemented code

### Code Style
- **TypeScript strict mode** with ESM (`"type": "module"`, `.js` extensions in relative imports)
- **Naming**: PascalCase classes/interfaces, camelCase variables/functions, UPPER_SNAKE_CASE constants, kebab-case files
- **OOP, SOLID, Clean Architecture** for all new code
- **No `any` type** — use union types and type guards

### Publishable Adapter, Module, and Package Layouts
- All adapter packages MUST live under `packages/platform-adapters/`; all module packages MUST live under `packages/platform-modules/`; other publishable packages remain direct children of `packages/`.
- Every new publishable adapter, module, or package MUST follow the `packages/platform-adapters/platform-adapter-auth-oidc`, `packages/platform-modules/platform-module-auth-local-session`, or `packages/platform-core` layouts: root `package.json`, `vite.config.ts`, `vitest.config.ts`, `tsconfig*.json`, root implementation files in `src/`, and `tests/`.
- Do not place constants, errors, interfaces, or utilities directly in `src/` when creating or modifying a publishable adapter or package.

### Security
- **Allowlist-only module loading** in production
- **Zod validation** at all boundaries
- **Secret redaction** from logs (Pino `redact` config)
- **Security classification**: `trusted` | `internal` | `third-party-reviewed`

### Testing
- **Vitest** as test runner (`turbo test`, `turbo test:contracts`)
- **Contract tests** mandatory for all modules before integration
- **AAA pattern**: Arrange, Act, Assert

### Git & Workflow
- Feature branches, conventional commits, PR review required
- Run `npm run lint:architecture` and `npm run validate:dependency-policy` before committing
- CI gates must pass before merge

## ⚠️ Critical Rules for AI Agents

### Rule Precedence
When guidance conflicts, use this precedence order:
1. **Repository reality** (source of truth): concrete files and scripts in repo
2. **`AGENTS.md`**: operational policy for all agents in this repository
3. **`.agents/rules/*.md`**: detailed topic-specific rules
4. **`.context/`**: target-state architecture docs, design intent and roadmap

### Repository Readiness Truth Table
**BEFORE making recommendations about commands, tooling, or process maturity, verify these artifacts:**
1. `packages/platform-utils/tsconfig/base.json`, `packages/*/tsconfig.json`, and `packages/platform-modules/*/tsconfig.json`
2. `packages/`
3. `.github/workflows/`
4. test runner config (`vitest.config.*`)
5. lint config (`eslint.config.*`)

### Command and Capability Claim Policy
- Only list commands present in current root `package.json`
- Do not claim commands unless scripts/configs exist in repository artifacts
- For unavailable capabilities, state the gap and map to relevant phase in `.context/04-implementation-plan/`

### Architecture Boundary Rules
**DO NOT:**
- Import from `platform-core` into adapters (boundary violation)
- Import directly between modules (coupling violation)
- Add framework dependencies to core packages
- Ignore ADR constraints when proposing changes
- Add admin shell runtime, frontend framework, or UI rendering dependencies to `platform-core`
- Bypass admin integration contracts with direct module-to-shell coupling

**DO:**
- Design implementation with object-oriented composition and explicit abstractions
- Keep Clean Architecture dependency direction toward stable inner policies
- Enforce SOLID trade-offs explicitly during design and code review
- Use contract-first approach (types before implementation)
- Follow micro-core boundary principles (ADR-0001)
- Validate dependencies against package boundaries
- Reference ADRs when proposing architecture changes
- Keep admin integration in hybrid model: separate `admin-shell`, contract package, and BFF adapter

### Documentation Requirements
**ALWAYS:**
- Update AGENTS.md if adding new commands or tools
- Reference architecture docs from `.context/`
- Document public APIs with JSDoc comments
- Include stability level (`@stable`/`@beta`/`@alpha`/`@experimental`/`@internal`)

## Development Environment

### Required Tools
- Node.js >= 22.12 (see `package.json` engines)
- npm >= 8
- TypeScript compiler (dependency)
- Vite 8 and `vite-plugin-dts` for publishable package builds
- Git for version control
- Turborepo (for monorepo task orchestration)

### Common Commands
```bash
turbo build          # Build all packages with dependency ordering
turbo test           # Run tests across all packages
turbo typecheck      # Type check all packages
turbo dev            # Start dev mode in all packages

npm run lint:architecture             # Verify module import rules (ADR-0001)
npm run validate:dependency-policy    # Enforce dependency layering
npm run validate:module-graph         # Check module dependency tree
npm run validate:public-api-boundary  # Verify SDK public API contracts
npm run validate:runtime-policy       # Check runtime module loading policies
npm run start:local                   # Start the local SQLite admin BFF host
npm run auth:bootstrap-local          # Bootstrap local auth from an interactive TTY

npm run bench:startup      # Run startup-sequence benchmark, JSON report -> bench-reports/startup.json
npm run bench:events       # Run event-dispatch benchmark, JSON report -> bench-reports/events.json
npm run bench:regression   # Compare latest bench reports against baseline (15% fail / 20% alert)
npm run bench:calibrate    # Recompute baseline.json from a fresh bench run (review the diff!)
```

### Architecture & Dependency Validation
These checks are enforced in CI via `.github/workflows/` gates and must pass before merge:
- `lint:architecture`: Module imports don't violate boundaries
- `validate:dependency-policy`: Strict dependency layering (no circular deps)
- `validate:module-graph`: Module interdependencies form a valid DAG
- `validate:public-api-boundary`: SDK public exports match API_REPORT.md
- `validate:runtime-policy`: Module manifests, security classes, startup policies
- `bench-regression` (job `FF-06 perf-regression` in `.github/workflows/quality-gates.yml`): fails when startup P95 or event-dispatch P95 drift > 15% vs `packages/platform-core/bench/baseline.json`

### Performance Regression Baseline
- The committed baseline lives in [`packages/platform-core/bench/baseline.json`](packages/platform-core/bench/baseline.json:1).
- The warmup/iteration counts live in [`packages/platform-core/bench/regression-budget.config.ts`](packages/platform-core/bench/regression-budget.config.ts:1).
- Use `npm run bench:calibrate` on the canonical CI runner when intentionally bumping the baseline. Always include the diff in a dedicated PR and link to the relevant risk-to-control evidence in `docs/security/risk-control-matrix.md` or `docs/performance/risk-control-matrix.md`.

## Additional Resources

- [TypeScript Documentation](https://www.typescriptlang.org/docs/)
- [Node.js Best Practices](https://github.com/goldbergyoni/nodebestpractices)
- [OWASP Security Guidelines](https://owasp.org/)
- [Clean Code Principles](https://www.amazon.com/Clean-Code-Handbook-Software-Craftsmanship/dp/0132350882)

---

**Last Updated**: 2026-08-12
**Version**: 0.6.0
