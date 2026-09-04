# 01 Recommendations by Horizon

## Prioritization Method

Impact and effort are scored on a 1 to 5 scale.
Priority label uses impact over effort ratio:
- P1: high impact and low to medium effort
- P2: high impact and medium to high effort
- P3: medium impact and strategic effort

Effort values below represent execution effort for implementation work and governance rollout.

## Status Sync (2026-06-02)
- Phases 01-06 are completed, including architecture gates, contract conformance suite, runtime lifecycle foundation, and security/performance hardening.
- `MT-01` and `MT-02` are now satisfied in repository reality.
- Remaining execution-critical items center on Phase 07-09 admin enablement stream and Phase 10 internal MVP validation.

## Quick Wins

## QW-01 Establish executable architecture gates in CI
- Area: architecture, CI CD, reliability
- Problem: architecture policy exists mostly in documentation and can drift.
- Recommendation: implement automated checks for boundary violations, module coupling, manifest validation, and contract compliance.
- Why now: prevents early-stage entropy and expensive refactoring.
- Expected effect: lower architecture drift, higher release confidence.
- Impact: 5
- Effort: 2
- Priority: P1

## QW-02 Create and enforce test runner and quality baseline
- Area: testing, development process
- Problem: no enforced standard test stack in current repository setup.
- Recommendation: standardize one test runner and minimal required suites for SDK and core packages.
- Expected effect: consistent quality signal across packages and future module repositories.
- Impact: 5
- Effort: 2
- Priority: P1

## QW-03 Separate root dependencies by package responsibility
- Area: architecture, performance, security
- Problem: root dependencies include adapter-level libraries before package boundaries are implemented.
- Recommendation: move transport/security middleware dependencies to adapter package scope as soon as package layout is introduced.
- Expected effect: cleaner core boundary, reduced attack surface and bundle overhead in core path.
- Impact: 4
- Effort: 2
- Priority: P1

## QW-04 Define internal MVP north-star metrics and dashboard contract
- Area: product, observability
- Problem: no explicit KPI hierarchy for mixed strategy execution.
- Recommendation: publish KPI contract for internal MVP loop: time-to-first-module, startup reliability, module integration lead time, policy violation rate.
- Expected effect: objective product-learning loop and clearer go no-go for ecosystem expansion.
- Impact: 5
- Effort: 2
- Priority: P1

## QW-05 Create release readiness checklist as pipeline gate
- Area: CI CD, governance
- Problem: release readiness workflow exists, but evidence consistency and exception-expiration discipline still require stronger operational enforcement.
- Recommendation: turn release checklist into required gate artifact with automated evidence links.
- Expected effect: predictable release quality and lower compliance variance.
- Impact: 4
- Effort: 2
- Priority: P1

## Mid-Term Improvements

## MT-01 Deliver SDK contract package plus contract test package first
- Area: architecture, testing, ecosystem readiness
- Status: Completed in Phase 04.
- Problem: ecosystem success depends on stable contracts and conformance automation.
- Recommendation: prioritize SDK and contract-tests implementation before expanding runtime features.
- Expected effect: reduced contract drift and easier module onboarding.
- Impact: 5
- Effort: 3
- Priority: P1

## MT-02 Implement runtime policy engine for strict and best-effort behavior
- Area: reliability, operability
- Status: Completed in Phase 05.
- Problem: lifecycle policy baseline is implemented and hardened in Phase 06.
- Recommendation: proceed with admin enablement contracts and BFF stream (Phases 07-08).
- Expected effect: reliable startup behavior and incident triage quality.
- Impact: 5
- Effort: 3
- Priority: P1

## MT-03 Build baseline observability schema and startup report standard
- Area: observability, operations
- Problem: required telemetry fields are not formalized as contracts in code artifacts.
- Recommendation: define telemetry event schema and startup report payload contract for core and adapters.
- Expected effect: consistent signals for reliability and compatibility operations.
- Impact: 4
- Effort: 3
- Priority: P2

## MT-04 Implement module repository template and CI blueprint
- Area: ecosystem, security, process
- Problem: hidden dependency on external module hygiene.
- Recommendation: publish canonical module template repository with contract tests, security checks, and semantic release conventions.
- Expected effect: faster and safer external ecosystem growth.
- Impact: 5
- Effort: 4
- Priority: P2

## MT-05 Introduce performance benchmark gates for startup and event dispatch
- Area: performance, scalability
- Problem: no baseline performance thresholds enforced.
- Recommendation: add benchmark suite and regression budget checks in protected branches.
- Expected effect: controlled performance profile as module count increases.
- Impact: 4
- Effort: 3
- Priority: P2

## Strategic Changes

## ST-01 Formal product operating model for internal-to-ecosystem transition
- Area: product strategy, governance
- Problem: transition criteria are architecture-centric but product-market evidence criteria are underdefined.
- Recommendation: define stage gate model combining technical readiness and ecosystem adoption thresholds.
- Expected effect: lower strategic execution risk and clearer investment sequencing.
- Impact: 5
- Effort: 4
- Priority: P2

## ST-02 Progressive trust and security class model for third-party modules
- Area: security, ecosystem
- Problem: trust classes are defined conceptually but need operational lifecycle.
- Recommendation: establish security maturity levels, mandatory controls per level, and onboarding pathways.
- Expected effect: scalable security governance without blocking ecosystem growth.
- Impact: 5
- Effort: 4
- Priority: P2

## ST-03 Platform reliability program with error budget governance
- Area: reliability, operations
- Problem: SLO model exists in docs but requires operating rhythm and incident economics.
- Recommendation: launch reliability review cycle with budget policy actions tied to release permissions.
- Expected effect: fewer high-severity incidents and controlled change velocity.
- Impact: 4
- Effort: 4
- Priority: P3

## ST-04 Architecture compliance as code across all repositories
- Area: architecture, process
- Problem: boundary and policy checks must scale beyond monorepo.
- Recommendation: package architecture policy checks as reusable workflow components for platform and modules.
- Expected effect: consistent governance across first-party and third-party repos.
- Impact: 5
- Effort: 4
- Priority: P2

## Dependency and Sequencing Notes

1. QW-01 and QW-02 must precede most implementation-heavy items.
2. MT-01 is a prerequisite for safe ecosystem scaling.
3. MT-04 depends on MT-01 and baseline CI contracts.
4. ST-02 depends on MT-04 and observability maturity from MT-03.
5. ST-03 effectiveness depends on MT-03 telemetry contract stability.
