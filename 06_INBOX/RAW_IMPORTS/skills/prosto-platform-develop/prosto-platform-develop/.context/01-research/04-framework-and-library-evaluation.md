# 04 - Framework And Library Evaluation

Date: 2026-03-23

## Selection Principle
Use the smallest set of dependencies that materially improves reliability, developer speed, and safety.

## Decision Matrix

| Concern | Option A | Option B | Option C | Recommendation |
|---|---|---|---|---|
| Core runtime | Node.js + TS only | NestJS | Fastify-first platform | A for core; keep framework outside kernel |
| HTTP adapter | Fastify | Express | Hono | Fastify adapter package (optional) |
| Validation | Zod | TypeBox + Ajv | Custom guards only | Zod for boundary validation in core; TypeBox+Ajv for high-throughput adapters |
| DI/Service container | Custom typed registry | Awilix | tsyringe | Custom typed registry in core; use library only if complexity grows |
| Logging | Pino | Winston | Console only | Pino |
| Testing | Node test runner | Vitest | Jest | Vitest or Node test runner; avoid adding both |
| Config | Native env parsing | Zod schema + env | Convict | Zod schema + env loader |
| Metrics/Tracing | OpenTelemetry API | Vendor-specific SDK only | none | OpenTelemetry API in adapters where needed |

## Recommended Baseline Stack
- Required:
  - TypeScript (strict)
  - `@prosto/platform-sdk` + `@prosto/platform-core` (project packages)
  - Zod (runtime schema validation)
  - Pino (structured logging)
- Optional adapters:
  - Fastify + related plugins in `@prosto/http-fastify`
  - Database libraries only in persistence modules/adapters
- Testing:
  - Choose one: Vitest or Node built-in test runner
  - Add contract test package for plugin compatibility

## Why Not Use A Heavy Framework In Core
- Core must remain neutral and long-lived.
- Framework lock-in in core reduces module portability.
- Framework-specific lifecycle and DI patterns can conflict with plugin model.

## Current package.json Notes
- Existing dependencies (`cookie-parser`, `cors`, `helmet`, `node-fetch`) imply web concerns.
- Recommendation: move web concerns to an HTTP adapter package unless core itself exposes an HTTP surface.
- Keep core dependency list close to zero except for cross-cutting essentials (validation, logging).

## Library Adoption Rules
- Add a dependency only if at least one is true:
  - significantly reduces defect risk
  - replaces non-trivial custom code that is hard to maintain
  - provides measurable performance benefit in critical paths
- Before adding a library:
  - evaluate maintenance quality and release activity
  - verify ESM/TypeScript compatibility
  - review security posture and transitive dependency size

Continue with: [05 - Quality Security Performance](./05-quality-security-performance.md).
