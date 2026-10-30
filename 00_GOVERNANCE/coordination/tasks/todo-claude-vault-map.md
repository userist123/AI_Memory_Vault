---
id: "bfbcb997-28eb-45a4-ae52-3954b8bce6fa"
title: "Checkpoint: Claude Code - vault map and critical review"
type: "project"
lifecycle: "ACTIVE"
created: "2026-10-30T00:00:00Z"
updated: "2026-10-30T15:30:00Z"
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
STATUS: IN_PROGRESS        UPDATED: 2026-10-30T17:30:00+02:00
TASK: Make vault navigation provably better than public patterns; phase 0 = critical-review protocol
BRANCH / PR: claude/critical-review (worktree ../AI_Memory_Vault_wt_critical) / none yet    BASE: d8f978fad
SPEC: this file; 00_GOVERNANCE/protocols/Critical_Review_Protocol_V1.md
DONE:
- Phase 0: Critical_Review_Protocol_V1.md, skill .claude/skills/critical-review, pointers in AGENTS/CLAUDE/GEMINI,
  00_GOVERNANCE/review/CORRECTIONS_LEDGER.jsonl (6 entries), 20_TESTS/test_critical_review_protocol.py.
- Draft challenged read-only by Codex/GPT and Gemini (agy); 9 objections accepted, 3 deferred (below).
- Main working copy: 538 changed files already on GitHub removed after full backup outside the repo;
  1223 changed files kept there for inspection (list in the backup folder, removed_list.txt / keep list).
- Gap 1 (branch claude/route-coverage-gate, worktree ../AI_Memory_Vault_wt_routes, base d8f978fad):
  per-note coverage gate in build_route_manifest.py --check (NOTE_WITHOUT_ROUTE, UNROUTED_GLOB_UNUSED,
  TRACKED_SYMLINK, fail closed without git); domains projects.casa3d, claude_config, entry, tests_docs,
  skills_nested (INTERNAL + unverified, templates excluded); `unrouted:` list with reasons.
  Challenged by Codex (read-only, bundle) + independent Opus reviewer; agy quota exhausted (resets ~2026-11-01).
  Accepted: keep `skills` (PUBLIC, exported) top-level only; narrow 20_TESTS; symlink + case fixes; export test.
NEXT (in order):
1. Owner approval to push claude/critical-review and claude/route-coverage-gate and open the PRs.
2. Owner decisions from gap 1 review (not changed, access_policy.yaml is owner-only):
   a. top-level imported skills (PROVENANCE.json -> external repos) are PUBLIC + verified in `skills` and exported;
   b. telegram.bot can read claude_config / entry (INTERNAL); add "!claude_config" if not wanted.
   Residual (not done): read-back of every route (only one per domain), uppercase .MD, prefix globs for
   future .agents/<prefix> dirs pass silently.
3. Gap 2: AGENTS.md canonical and lean; CLAUDE.md/GEMINI.md thin pointers; build on
   07_EVALUATION/reports/cost_router_claude_context_map_2026-10-21/CONTEXT_MAP.md (kept, uncommitted, main copy).
4. Gap 3: layered table of contents generated from vault_domains.yaml + link check (incl. wikilinks).
5. Gap 4: expose routes as MCP resource templates over vault_access (no new layer).
6. Benchmark: gold set by Fable (main session), cross-checked by GPT/Gemini + owner 20% sample; baselines
   grep / llms.txt / AGENTS+TOC / Karpathy index / RAG / current / new; also run on a local model.
   Include the seeded overreach benchmark for the critical-review protocol (effectiveness UNVERIFIED until then).
7. Inspect the 1223 kept files (Antigravity cost-router remediation, Casa3D audits, prompt-compiler-executor).
BLOCKERS / OWNER QUESTIONS:
- Deferred: code gate refusing VAULT_STATE/verified-memory writes without claim card + challenge receipt.
- Deferred: mirror skill into .agents/skills (promotion is the owner's human gate).
- prompt-compiler-executor (4 files, kept): still wanted?
KEY FILES:
- 00_GOVERNANCE/protocols/Critical_Review_Protocol_V1.md, .claude/skills/critical-review/SKILL.md
- 00_GOVERNANCE/review/CORRECTIONS_LEDGER.jsonl, 20_TESTS/test_critical_review_protocol.py
VERIFICATION SO FAR: targeted tests 74 passed before challenge edits; mutation check (bad ledger line) fails as expected.
Full suite on this branch: 5071 passed, 4 failed (test_owner_authority_gate.py, WSL bash ERROR_PATH_NOT_FOUND;
the same 4 fail on clean d8f978fad, so environmental, not this change).
Gap 3 (branch claude/link-check, worktree ../AI_Memory_Vault_wt_links): navigation_links.py + test, 14 dead paths
fixed, 16 deliberate mentions allowlisted; the TOC half is already vault_list("*") (ledger). Gap 2 premise stale
(ledger): CLAUDE.md is 13,479 chars now, low duplication, AGENTS.md not loaded beside it; owner to pick an option.
Gap 4 deferred: FastMCP 1.30 templates match one segment, slugs have several; agents already have vault_read.
