---
id: "bfbcb997-28eb-45a4-ae52-3954b8bce6fa"
title: "Checkpoint: Claude Code - vault map and critical review"
type: "project"
lifecycle: "REVIEW"
created: "2026-10-30T00:00:00Z"
updated: "2026-10-30T17:45:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "00_GOVERNANCE/coordination/tasks/todo-claude-vault-map.md"
  created_by: "claude-code"
tags:
  - "checkpoint"
  - "critical-review"
  - "coordination"
---

# todo-claude-vault-map
STATUS: IN_PROGRESS (navigation benchmark running)        UPDATED: 2026-10-30T19:45:00+02:00
TASK: Make vault navigation provably better than public patterns; critical-review protocol; close gaps.
BASE: d8f978fad (origin/main). Nothing pushed; every branch below is local, waiting for the owner.

BRANCHES (worktrees next to the main copy):
- claude/critical-review (wt_critical): protocol, skill, ledger, pointers, test.
- claude/entry-contract-sync (on top of it): CLAUDE.md carries the binding AGENTS.md rules; frozen-core list
  reconciled; test_entry_contract_sync.py; protocol Tier 1 narrowed; ledger 10 entries.
- claude/route-coverage-gate (wt_routes): per-note coverage gate, 5 new domains, VAULT_STATE re-measured.
- claude/link-check (wt_links): navigation_links.py (resolves wikilinks like VaultIndex.resolve), 6 dead
  paths fixed, Confidence_Model.md newline artifacts fixed.
- claude/critical-review-benchmark (wt_bench): pre-registered seeded benchmark + results + exploratory re-run.
- claude/navigation-benchmark (wt_nav, on route-coverage-gate): pre-registration, Codex-validated gold set
  (24), harness; full run in progress (144 runs).
- claude/privacy-redaction (wt_privacy, on origin/main): profile path and machine name -> placeholders
  (21 replacements, 6 files); private_identifier_guard.py + test.

RESULTS:
- Critical-review benchmark (pre-registered): not discriminating (0/27 overclaims with and without the
  protocol; several traps had the answer in plain sight). Exploratory: caveats outside the question 8/18
  with the protocol, 0/18 without; after narrowing Tier 1, 0/18 (not pre-registered). Effect: UNVERIFIED.
- Gap 2 premise was stale; gap 3 TOC half already exists (vault_list); gap 4 deferred (FastMCP 1.30 templates
  match one segment). All in the ledger.
- Public repo: 113 files on origin/main carry an identifier from the owner's private list (case-insensitive);
  after the redaction branch, 107 remain, all the owner's name (prose, personal imported files, a few third-party
  false positives). History still holds every version.
- Kept files in the main copy (1223): ~1150 raw cost-router benchmark logs (no private identifiers, several dirs
  cited by committed docs: phase2_final, phase3_final, context_map, native_probe); the Antigravity cost-router
  remediation (hook_antigravity +137, test +219) applies cleanly on origin/main and passes 60 + 480 dependent
  tests; its self_update/test_cost_router_skill/install_design_stack/creative-web-director edits are older
  than origin/main (4 failures when applied); COST_ROUTER_* reports, overnight_reliability and one Casa3D
  audit carry private identifiers and need redaction before any commit; prompt-compiler-executor (4 files)
  still waits for the owner.

NEXT:
1. Finish the navigation benchmark: report, owner 20% sample, cross-family review of the conclusion.
2. Owner decisions (see BLOCKERS), then push and PRs.
3. A discriminating critical-review benchmark (answers not in plain sight, second model).
BLOCKERS / OWNER QUESTIONS:
- Push the branches and open PRs?
- Personal files in 02_PRODUCT/projects/imported/bot/trade and the owner's name across reports: delete or
  keep; history rewrite (owner only); enable private_identifier_guard as a pre-commit hook?
- access_policy.yaml (owner only): imported skills exported as verified; telegram.bot reads claude_config.
- cost-router SUMMARY.md: commit or drop the citation; commit the Antigravity remediation?
- prompt-compiler-executor: still wanted?
- Deferred: code gate on VAULT_STATE/verified-memory writes; mirroring the skill into .agents/skills.
VERIFICATION: per-branch targeted tests green; full suites on critical-review, route-coverage-gate and
link-check: only the 4 WSL owner_authority_gate failures that also fail on clean d8f978fad (plus one audit-log
flake under three parallel suites, green alone). Trial merge of the four gap branches: gates and tests green.
