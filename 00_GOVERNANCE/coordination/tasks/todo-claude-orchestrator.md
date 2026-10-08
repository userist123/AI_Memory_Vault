# todo-claude-orchestrator
STATUS: IN_PROGRESS        UPDATED: 2026-10-08T21:20Z
TASK: LogAnalyzer stage 2 (contract-driven build) — orchestrate Sonnet agents, review, merge in order.
BRANCH / PR: working notes on `claude/wonderful-bohr-iifqfn` (not for main); code via one PR per work package.
BASE: main @ 94020777 (WP0 #227, WP1 #228, WP12 #229 merged).
SPEC (read first, in this order):
- `docs/dfir/CONTRACT_AUDIT_STAGE1.md` (on main; decisions 1–12) + latest decisions 13–15 on branch
  `claude/wonderful-bohr-iifqfn`: `tasks/loganalyzer/CONTRACT_AUDIT_STAGE1.md` §8.
- `tasks/todo.md` on that branch (full plan, app requirements, security fixes, parked items).
- `tasks/loganalyzer/LESSONS_LEARNED_MAPPING.md` (owner's 107-section lessons doc → new WP13–WP17).
- `tasks/loganalyzer/WINDOWS_TOOLING_COMPAT.md` (WP-PKG fix list).
DONE:
- Stage-1 audit + 15 owner decisions; WP0, WP1, WP12 merged; Book-to-Memory B03–B10 tooling merged (#226).
IN FLIGHT (agents, own branches; each must keep its own todo-<name>.md here):
- WP-ED → WP-PKG: branch `loganalyzer/wp-ed-editions` (P1 classified build without network/AI/host actions;
  P2/P3 one app with signed-policy modes; self-contained publish; no wmic; AuditCollector.ps1 shipped).
- WP2: branch `loganalyzer/wp2-finding-contract` (finding contract, 10 states, severity≠confidence,
  anti-overclaim invariants, parser can/cannot-prove descriptors, parser-health + source-availability enums).
NEXT (in order):
1. Review + merge WP-ED/WP-PKG and WP2 (trial-merge with main, both Python test roots, .NET build, Windows CI).
2. Send WP2 addendum from the mapping if not already in (graph relations, timestamp-reliability → confidence).
3. WP3, WP4; then WP1b (relabel legacy after-hours scoring, "Conform on absence", LOG-TAMPER = every clear High).
4. WP15 → WP14 (∥ WP11 Tier 1) → WP5/6/7 → WP13 → WP16 → WP17.
5. After the app: licensing on asymmetric signatures (explain key handling to owner first).
OWNER QUESTIONS OPEN:
- Mapping Q1–Q10 (authorized-media inventory source, clearance/need-to-know source, working hours & rotation
  procedure, zones/transfer channels, station/domain/email results into DFIR case?, classification sources,
  Tier-3 availability, case purpose/scope fields, WP1b approval, Exchange scripts in P1).
- WP12: waiver signing key; findings register (recommended: CodeQL + manual file).
PARKED: HG 585 accreditation (`tasks/loganalyzer/HG585_ACCREDITATION_REQUIREMENTS.md`) until owner supplies data.
RULES: one agent at a time unless independent; checkpoints per README here; no wmic; P1 has no network/AI/host
actions; never present a facade as production-ready; don't remove existing functionality.
