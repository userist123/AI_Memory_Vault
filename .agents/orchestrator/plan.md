# Master Orchestration Plan — OTP Flight Finder Rebuild

## Objective
Rebuild the OTP Flight Finder web application to strictly enforce Bucharest Henri Coandă (OTP) origin in all deep-links (especially Ryanair), eliminate any Băneasa (BBU) references or fallbacks, implement a polished responsive UI/UX (Tailwind, accessibility WCAG, 0 UTF-8 BOMs), achieve >=80% test coverage, and attain Lighthouse >=90 across Performance, Accessibility, Best Practices, and SEO.

## Work Breakdown & Phasing

### Phase 0: Survey & Discovery (Parallel)
- **Explorer 1 (Codebase & Architecture)**: Probe `C:\Users\user\teamwork_projects\otp_flight_finder` to map existing architecture, backend, frontend, build tools, and test harness.
- **Explorer 2 (Deep-links & Airline API Invariants)**: Analyze current deep-link generation logic, Ryanair URL schema (`origin`/`originIata=OTP`, `destination`/`destinationIata`), query params, and eliminate BBU leakages.
- **Spec Miner 3 (UI/UX, Assets & Test Requirements)**: Mine UI/UX specifications, Tailwind config, accessibility guidelines, asset BOM checks, Lighthouse criteria, and test fixtures.

### Phase 1: Global Project Decomposition & Architecture (`PROJECT.md`, `TEST_INFRA.md`)
- Synthesize findings into `PROJECT.md` (Feature Inventory, Milestone Mapping, Interface Contracts, Code Layout).
- Create `TEST_INFRA.md` with 4-tier requirement-driven test plan.

### Phase 2: Dual Track Dispatch
- **Track A (E2E Test Writer / Orchestrator)**: Build pytest E2E suite covering Tier 1 (Feature Isolation), Tier 2 (Boundary/Edge), Tier 3 (Cross-feature interactions), Tier 4 (Real-world scenarios) -> Output `TEST_READY.md`.
- **Track B (Implementation Milestones)**:
  - M1: Deep-link Engine & Strict OTP Invariants (Ryanair & others, 0 BBU).
  - M2: Responsive UI/UX, Tailwind System, Accessibility & Clean Assets (0 BOM).
  - M3: Web Server / API integration, Static Asset Serving, Performance Tuning.

### Phase 3: Verification & Integration Gate (Tiers 1-4)
- Run Reviewers, Challengers, and Forensic Auditors across all milestones.
- Validate 100% E2E test pass rate.

### Phase 4: Tier 5 Adversarial Coverage Hardening
- Adversarial Challenger tests edge cases, concurrency, malformed inputs, edge IATA codes.
- Fix any uncovered regressions.

### Phase 5: Final Acceptance & Sentinel Reporting
- Run full pytest suite with coverage (>=80%).
- Run Lighthouse audits (all >=90).
- Run Forensic Auditor (Clean verdict).
- Deliver final completion report to Sentinel.
