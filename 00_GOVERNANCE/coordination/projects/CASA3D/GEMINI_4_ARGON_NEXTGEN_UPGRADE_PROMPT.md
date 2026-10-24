---
id: "b4a9b0e1-0a83-45cb-b5b4-aec95d2fa2f1"
type: task_prompt
lifecycle: REVIEW
category: casa3d
tags: [casa3d, gemini, implementation, ui_ux, digital_twin, geometry, testing]
created: "2026-10-17"
updated: "2026-10-17"
provenance:
  source_type: user
  source_ref: "User request to upgrade Casa3D functionality and UI/UX using Gemini 4 Argon"
  source_date: "2026-10-17"
  redaction: not_applicable
  provenance_status: complete
confidence: high
verification: unverified
relations:
  - type: relates_to
    target_id: "4e17d26a-74a6-408a-b0f4-069dfd20ac95"
---

# Casa3D Next-Gen Upgrade — Gemini 4 Argon Execution Prompt

> **Use:** Give this full prompt to Gemini 4 Argon with repository access. It is intentionally implementation-oriented: inspect the real repo, then improve both core behavior and UI/UX, test every meaningful change, and open a reviewable PR. User-facing handoff must be in Romanian.

## Mission

Work on Casa3D in this repository as a senior product engineer, 3D interaction designer, frontend/backend architect, geometry-engine engineer, and QA engineer. Deliver a substantiated upgrade to both real product behavior and UI/UX. Aim for a polished, premium, technically credible 3D home-design product—not a cosmetic dashboard redesign and not a speculative rewrite.

- Repository: https://github.com/userist123/AI_Memory_Vault
- Canonical project context: `02_PRODUCT/projects/Casa3D.md`
- Implementation ledger: `02_PRODUCT/projects/Casa3D/CORE_IMPLEMENTATION_v8.md`
- Related work to inspect first: PR #257 (Claude Casa3D state/reconciliation) and PR #258 (Codex Casa3D memory/history).

## 1. Inspect before implementing

1. Establish the checked-out commit, branch, working-tree state, package manager, installed versions, app entry points, routes, components, geometry engine, persistence model, API routes, tests and current design system. Use code and executable results as truth, not summaries.
2. Inspect PRs #257 and #258, related branches, Casa3D project notes, F0–F3 constitutional contracts, F4–F8 phase documents if they exist, and the v8 implementation ledger. Do not assume an unmerged PR is in main or that a manifest proves runtime behavior. Reconcile differences explicitly.
3. Build a baseline inventory: **REAL** (implemented and demonstrated), **PARTIAL** (some code/coverage), **UNKNOWN** (not demonstrated), **BROKEN** (reproducible failure). Attach exact files and evidence.
4. Run the existing focused tests, typecheck, lint, build and current browser/smoke checks before edits where the environment permits. Record exact commands, exit codes and existing failures. Do not hide failures or rewrite baselines to make results look green.
5. Make a short plan with small deliverable slices and regression gates, then implement. Do not stop after a report or mockup unless blocked by unavailable dependencies/credentials; complete independent safe work even if another slice is blocked.

## 2. Product vision

Casa3D should feel like a professional, approachable home-design studio:

- A reliable Digital Twin that remains the source of truth for dimensions and geometry.
- Fluid 2D + 3D editing with synchronized selection, measurements, room/object properties, camera controls and clear editing states.
- An excellent workflow from floor plan → furnished layout → AI design alternatives → quantities/BOQ → budget → materials/offers → share/export.
- A distinctive premium visual system, not generic AI-generated SaaS cards. Prioritize clarity, usable canvas space, hierarchy, fine typography, restrained color, consistent spacing and precise interaction feedback.
- Fast, usable desktop-first, with responsive/tablet/mobile behavior where the architecture supports it.
- Trustworthy: no fabricated dimensions, products, prices, certifications, availability or building-code claims.

## 3. Non-negotiable product constitution

Preserve and test these invariants; do not weaken them to ship a feature.

1. Digital Twin / Geometry Engine is the geometric source of truth; internal units are meters.
2. AI proposes semantic intent only. It must not supply authoritative coordinates or override geometry validation.
3. Every proposed layout/modification is validated by the Geometry Engine and server-side contracts.
4. Preview and Reject do not mutate the project. Apply is revalidated against the current project fingerprint/version and creates a revision.
5. Stale proposals/approvals fail closed. Public decisions bind to the exact version the user saw; reject stale input with a clear conflict (for example HTTP 409).
6. ERROR blocks. WARNING is visible and requires explicit confirmation.
7. AI-selected products/materials must be IDs actually supplied by the server/catalog. Never invent product IDs, dimensions, price, stock, certification, supplier or availability.
8. Unknown commercial data remains UNKNOWN and shows provenance/freshness/confidence where known.
9. Preserve auth, authorization, validation, persistence, revision history, audit and privacy boundaries. Never expose secrets, private identifiers, local machine details or personal case data in code, docs, tests or PR text.
10. No silent fallback that disguises a broken integration as a successful live AI run. Mark mock/rules-engine/demo behavior honestly.
11. Do not modify unrelated LogAnalyzer, Memory Vault cognition/retrieval, security policy or governance scopes.
12. No force push, auto-merge, direct merge, destructive database migration, production data mutation, live paid API usage or secret insertion. Never ask for or print API keys. Keep work on a feature branch and propose it as a PR unless the owner explicitly approves otherwise.

## 4. Workstream A — real functional engineering

Audit the actual source and implement the highest-value verified fixes/gaps, prioritizing:

- **Geometry engine:** room/wall/door/window relationships; placement bounds; clearance/circulation; overlapping objects; dimension validity; stable IDs; deterministic placement; units and precision. Reproduce relevant existing regression issues rather than assuming they remain open.
- **Digital Twin integrity:** one canonical persisted model; consistent revision/fingerprint; validated migrations and backward compatibility; synchronized 2D/3D viewer state; reliable undo/redo where it is part of the contract; no hidden mutation during preview/reject.
- **AI Design DSL + solver:** schema-validated semantic operations; explicit constraint failures; deterministic solver; revalidation of all candidates; robust malformed-response, timeout and provider-unavailable paths; alternatives independently measured rather than arbitrarily declared winners.
- **Proposal lifecycle:** draft → preview → accept/reject → revision, with stale-state checks, optimistic concurrency, idempotency where appropriate and auditable results. Include a race-condition test where a proposal changes after the client loads it.
- **BOQ and budget:** derive quantities from the validated Digital Twin; expose formulas, units and waste assumptions; separate known from unknown costs; show totals/deltas/target budget. Include VAT, reserves or labor only where existing rules actually support them. Disclose when estimates are incomplete.
- **Catalog/materials/offers:** verify phase status from code and test output. Use valid IDs only; retain price provenance and timestamps; do not invent live prices or stock. Ensure redirects are safe and analytics privacy-conscious. Keep affiliate/retailer integrations disabled or clearly mock-labelled when credentials/contracts are absent.
- **Persistence/API:** validate server-side inputs; handle errors and partial failures explicitly; enforce access controls; preserve revision/audit data; avoid duplicate writes; use transactions where appropriate.
- **Workflow:** reduce friction between design, fit checking, materials calculation, variant comparison, revision save and sharing/export. Implement only when compatible with the current code and testable.

Do not rewrite the geometry engine or database because a new architecture seems fashionable. Prefer minimal, well-factored, compatible changes with measurable correctness.

## 5. Workstream B — premium UI/UX

Audit the current screens and implement a coherent experience, not an isolated landing-page polish.

### Main workspace and information architecture
- Make the current project, room/scene, editor, 2D/3D mode, properties, variants, validation, BOQ and budget easy to find.
- Maximize canvas/model space. Use a clear command bar and compact contextual panels. Avoid excessive cards, giant headings, noisy gradients, decorative badges and duplicated primary actions.
- Make hierarchy, spacing, typography, color, focus and action states consistent.

### 2D/3D editing
- Synchronize camera/selection where appropriate.
- Provide obvious orbit/pan/zoom/reset controls and view presets.
- Make selection/hover states, grid/axis/scale and real measurements legible.
- Render floor, walls and openings correctly.
- Expose drag handles only where the action is truly implemented.

### Contextual object editing
- Provide contextual properties for dimensions, orientation, catalog/material, constraints and cost.
- Numeric input must be precise, unit-aware, keyboard-friendly and validated.
- On geometry errors, select/focus the affected object where possible and explain a correction path.

### AI design and validation
- Provide a real Design Brief form with supported fields.
- Explain what changes and why.
- Compare up to three variants using identical metrics: geometry errors, warnings, room usage, known/unknown costs and budget delta.
- Offer Preview, Apply and Reject with explicit consequences and stale-proposal messaging.
- Group validation errors/warnings by severity and location. Never render a false success state.

### States and responsiveness
- Deliberately design empty, loading, error, offline, provider-unavailable, saving, saved and conflict states. They must recover; no endless spinners or swallowed exceptions.
- On supported small screens collapse panels into usable drawers; maintain touch targets and camera gestures; prevent horizontal overflow. Preserve desktop precision.
- Every visible control must work or be visibly disabled with a reason. No fake features just for screenshots.

### Accessibility, system and motion
- Semantic controls, keyboard navigation, visible focus, contrast, labels, reduced motion and screen-reader descriptions for controls/errors.
- Canvas must not be the only way to access essential project information.
- Consolidate design tokens for typography, spacing, surfaces, borders, focus rings, semantic status, shadows and motion.
- Use restrained functional colors. Support dark/light mode if the architecture already supports it or it can be added safely.
- Keep transitions short and purposeful and honor reduced-motion preferences.
- Follow existing localization conventions and avoid arbitrary language mixing or duplicated hard-coded copy.

## 6. Workstream C — 3D rendering and performance

Inspect the current rendering stack first and change only where justified.

- Ensure stable scene lifecycle and resource cleanup. Dispose geometries/materials/textures correctly without accidentally disposing shared resources.
- Avoid unnecessary scene rebuilds/rerenders; memoize expensive geometry safely; use demand rendering when appropriate; avoid unbounded animation loops.
- Show loading progress and clear recoverable errors when 3D initialization/assets fail.
- Improve camera, lighting, shadows, materials and selection outlines for clarity—not flashiness.
- Use progressive quality/LOD or reduced effects on weaker devices only if the stack supports them; honor reduced motion and performance constraints.
- Test context loss/unmount/re-mount and resource lifecycle where feasible.
- Record baseline and after metrics for startup, bundle size or render responsiveness only if tooling allows. Never claim FPS/performance improvement without measurements.

## 7. Workstream D — reliability and regression testing

Use a test-first loop for bug fixes:

1. Add/adjust a regression test that fails on the reproduced bug.
2. Implement the minimum correct fix.
3. Run focused tests.
4. Run typecheck, lint, build, full suite and browser smoke tests where feasible.
5. Repeat the regression suite after UI refactors.

Cover these cases where corresponding functionality exists:

- Invalid geometry, boundaries, overlaps, clearances, door/window fit and deterministic solver.
- AI returns unknown catalog ID, raw coordinates, malformed JSON, timeout, missing key or provider 4xx/5xx.
- Preview/reject leaves canonical data unchanged.
- Apply revalidates and creates exactly one revision.
- Stale proposal/approval returns conflict and cannot apply an unseen newer amount/layout.
- Unknown prices stay UNKNOWN and totals do not pretend estimates are complete.
- Invalid/unauthorized API requests, persistence failures, migrations and revision concurrency.
- Primary UI actions have functional test coverage or a documented reason browser testing is blocked.
- Responsive layout, keyboard focus and reduced-motion behavior where tooling permits.

Never weaken, delete, skip, xfail or dilute a security/regression test to obtain green CI. Never use a grep pipeline's exit status as the test gate.

## 8. Required deliverables

Create concise, useful documentation alongside code:

- `02_PRODUCT/projects/Casa3D/NEXTGEN_AUDIT.md`: baseline architecture/findings, marked REAL / PARTIAL / UNKNOWN / BROKEN, each with file/test evidence and impact.
- `02_PRODUCT/projects/Casa3D/NEXTGEN_IMPLEMENTATION.md`: implemented changes, decisions, compatibility notes, residual gaps and exact verification commands/results.
- Update `02_PRODUCT/projects/Casa3D/CORE_IMPLEMENTATION_v8.md` only if actual source/runtime evidence warrants it. Preserve earlier evidence and trace dates.
- Update canonical `02_PRODUCT/projects/Casa3D.md` only when the new committed artifacts and test outputs justify a status change. Source-file presence alone does not make a phase DONE.

All newly created Markdown files under the Vault content trees must include the repository's strict YAML front matter (id, type, lifecycle REVIEW, provenance, verification unverified, relations) and pass the strict frontmatter test.

## 9. Delivery strategy

1. Establish baseline and identify at most five highest-impact slices.
2. Prioritize: correctness/security and stale-state bugs; core editing workflow; 3D usability/performance; coherent design system/UI polish; secondary enhancements.
3. Implement meaningful end-to-end vertical slices, not a disconnected component dump.
4. Preserve public API/data-schema compatibility unless a verified defect requires a migration; document and test migration strategy.
5. Keep changes scoped to Casa3D and related tests/docs. Explain and avoid unrelated modifications unless required for safety.
6. Capture screenshots/browser artifacts only from a running application; never create mock screenshots as implementation evidence.
7. Preserve uncommitted user work. Never use basename-only backups that can overwrite files with the same basename.

## 10. Verification contract

For every command, report its exact command, actual exit code and result:

- **PASS** — command completed and checks passed.
- **FAIL** — check failed; include relevant output.
- **BLOCKED** — couldn't run; give the reason.
- **NOT RUN** — not attempted.

Do not claim the full suite passed if only focused tests ran. Separate pre-existing failures from newly introduced regressions. Do not claim production readiness, full functionality, live AI verification, real-time pricing or universal responsive support without direct evidence.

Before commit:
- Inspect `git diff --check`, `git status --short`, the full diff and changed-file list.
- Run tests independently and rely on each command's true exit status, not a grep/pipeline status.
- Run the privacy check with a reliable checker; fail closed if the checker itself errors.
- Search docs, fixtures, logs, generated artifacts and commit/PR text for secrets, personal identifiers, local paths and hostnames.
- Never overwrite uncommitted user work.

## 11. PR and final handoff

Open a reviewable PR from the feature branch; do not merge it. The PR description and final handoff must include:

1. Executive summary of user-visible behavior changes.
2. Before/after screenshots only if captured from the running build.
3. File-level changes grouped by functionality, UI/UX, tests and docs.
4. Verification matrix: command, exit code, status and evidence path.
5. REAL / PARTIAL / UNKNOWN / BROKEN status for each promised capability.
6. Performance measurements only if actually captured.
7. Known limitations, tests not run, blockers and exact next steps.
8. Confirmation that no secrets/private machine identifiers were committed and nothing was merged.
9. Owner-review checklist for usability, geometry correctness, proposal approval and cost transparency.

## Definition of done

Deliver the best safe, evidence-backed improvement possible in the available environment—not merely a plan. All changes must be scoped, tested, reviewable and proposed through a PR. If a larger feature cannot be completed, state exactly what was and was not done and why. Do not ask the owner to repeat context already in the repository.

User-facing explanations and final handoff must be in Romanian; code identifiers and technical notes may follow the project's existing conventions.
