---
id: "da78ac14-bb8d-561f-8636-4875b8a7cb38"
title: "Checkpoint: Claude Code - cost-router phase 4"
type: "project"
lifecycle: "REVIEW"
created: "2026-10-21T08:00:00Z"
updated: "2026-10-22T00:00:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "00_GOVERNANCE/coordination/tasks/todo-claude-cost-router-phase4.md"
  created_by: "claude-code"
tags:
  - "checkpoint"
  - "cost-router"
  - "coordination"
---

# todo-claude-cost-router-phase4 — CONTEXT DEDUPLICATION AND PROGRESSIVE DISCLOSURE

Agent: Claude Code main session (orchestrator) + subagents (vault-worker sonnet, Explore, vault-reviewer opus).
Predecessor: `todo-claude-cost-router-phase3.md`, `docs/reports/CLAUDE_CODE_COST_ROUTER_TOKEN_ECONOMY_REPORT.md`.
Goal: fewer TOTAL model tokens per correctly completed task (main + subagents), via persistent-context reduction.
Branch gemini/casa3d-nextgen-functionality-uiux, HEAD 2c72a7228. No commit/push/merge/branch switch/global changes.

## Phase A baseline (2026-10-21, main session)
- Dirty paths owned by OTHERS (never touch): `.agents/skills/cost-router/**`, `.agents/skills/prompt-compiler-executor/`,
  `20_TESTS/test_hook_antigravity.py`, `20_TESTS/test_prompt_compiler_executor.py`, `30_SCRIPTS/prompt/**`,
  `03_IMPLEMENTATION/packages/interfaces/skill_router.py`, `todo-antigravity-*.md`, `02_PRODUCT/projects/Casa3D/**`,
  `docs/reports/COST_ROUTER_{FINAL_RELEASE_READINESS,ADVERSARIAL_RELEASE_AUDIT,DUPLICATE_HOOK_REMEDIATION,GLOBAL_RUNTIME_VERIFICATION,PRODUCTION_BENCHMARK}.md`.
- Dirty paths owned by this mission line (phases 1-3): `.claude/settings.json`, `.claude/skills/cost-router/**`,
  `20_TESTS/test_cost_router_*.py`, `20_TESTS/test_claude_cost_router_benchmark.py`, `20_TESTS/cost_router_isolation.py`,
  `20_TESTS/test_install_design_stack.py`, `30_SCRIPTS/setup/install_design_stack.py`,
  `30_SCRIPTS/evaluation/claude_cost_router_benchmark.py`, `07_EVALUATION/reports/cost_router_claude_*`, `docs/reports/CLAUDE_CODE_*`,
  `todo-claude-cost-router-*.md`.
- Clean at HEAD (phase-4 may edit, reviewed): `CLAUDE.md` (23,692 B), `AGENTS.md` (14,696 B, NOT loaded by Claude Code:
  no @import), `.claude/agents/*.md`, `.claude/skills/{creative-web-director,graph-orchestrator,motion-director,security-lockdown}/SKILL.md`,
  `.mcp.json`, `04_CONFIG/claude_model_routing.json`. Tests asserting instruction files: `20_TESTS/test_claude_contract.py`,
  `20_TESTS/test_agent_docs_memory_interfaces.py`, `20_TESTS/test_memory_access.py`, `20_TESTS/memory_vault_fixture.py`,
  `20_TESTS/test_export_residue_exclusion.py` -> their assertions are governance requirements: keep asserted text.
- BUDGET: phase-3 artifact spend $24.24 (+≈$0.2-0.3 unrecorded hook-payload probe) of $30 -> remaining ≈$5.46.
  PHASE-4 PAID CAP: $5.00 total (every harness call passes --max-total-usd; keep a running ledger below).
- Global copy `~/.claude/skills/cost-router`: VERSION `2c72a7228+dirty / f844d4c8…`; `install.py --verify` says it now
  MATCHES the working tree -> it was rewritten again by the repo's SessionStart self-update after the phase-3 report
  (find when/which session: VERSION mtime vs session jsonl mtimes). `~/.claude/settings.json` has no hooks.
  User-level `superpowers` plugin (installed this conversation on owner request) injects a 3405-char SessionStart hook +
  skill listing into every session in both arms.

## Safety rules for every paid run
- `claude -p` ONLY inside the detached bench worktree (never repo root); harness strips the `install.py --session-start`
  entry AND sets `COST_ROUTER_NO_SELF_INSTALL=1`; refuse to run if cwd == repo root. Probes use `--model haiku`
  (≈$0.01 per 70k-token prefix) unless a sonnet number is required.

## Plan (one implementer at a time)
W1 (Phase E + B): harness/safety fixes + provider-measured source-to-context map (haiku single-turn ablations).
W2 (Phase C): progressive-disclosure batches on CLAUDE.md (+ agent/skill descriptions, MCP descriptions only if safe),
   each batch with pre/post prefix measured by W1's probe mode; tests green.
W3 (Phase D): pilot A/B within remaining budget (haiku main, hooks off both arms, old vs new context, ABBA, n≈4;
   then sonnet pilot baseline-old-context-hooks-off vs combined new-context-hooks-on, n=2).
R  (Phase F): vault-reviewer on final diff + raw evidence. Then report (Phase G) with one verdict.

## Ledger (USD, phase 4)
- W1 (sonnet worker, 2026-10-21): $0.3368 total = bare attempt $0 (auth refused) + run1 0.1266 (HEAD hooks ignore OFF, kept as negative) + 0.1248 + 0.0173 + 0.0254 + 0.0256 + 0.0171; cap was $0.60. Phase-4 running total $0.3368 of $5.00.
- W2 batch 1 (opus worker, 2026-10-21T21:50+03:00): $0.1331 = prefix 0.0234 + tools pair 0.0074 + compliance 0.0864 + skill listing 0.0159; cap $0.40. Phase-4 running total $0.4699 of $5.00.
- W3 pilot (sonnet worker, 2026-10-21): haiku block $2.4744 (rep0 incl. warmup 0.7761 + reps1-2 1.6983; soft cap 2.30 overshot by 0.17: pre-launch cap check, last T6 combined run 0.43) + sonnet block $1.4761 (warmup 0.2750 + T3 r0/r1 + T1 r0/r1 + T5 r0) = $3.9505 of the $4.40 W3 cap. Phase-4 running total $4.4204 of $5.00.

## Done
- [x] A  - [ ] W1  - [ ] W2  - [ ] W3  - [ ] R  - [ ] report

### W1 done (2026-10-21, vault-worker sonnet)
- E1 install.py --session-start inert under COST_ROUTER_NO_SELF_INSTALL=1; harness sets it for every child + refuses cwd == repo root. VERSION mtime unchanged (21:03:08) across all paid runs.
- E2 sonnet cache_read 0.2 -> 0.1 in 04_CONFIG/claude_model_routing.json (fit of 50 sonnet modelUsage records: exactly 0.10, error 1e-14%); haiku/opus/fable NOT covered by evidence, unchanged.
- E3 T6: structural check first (t6_structural_ok), admission (t6_admission); E4 overlay archive + MANIFEST sha256 per condition; E5 Reconciliation table in SUMMARY; E6 --conditions-file + --probe.
- B: CONTEXT_MAP.md in 07_EVALUATION/reports/cost_router_claude_context_map_2026-10-21/. Key: CLAUDE.md = 8,984 measured tokens per prefix; AGENTS.md (5,731) loads as a fallback ONLY if CLAUDE.md is absent; router hooks +657; MCP vault-memory +411; skills/agents ~0 in a no-tool probe; user-level MCP connectors ~10k unstable (owner scope).
- W2 constraint: never delete/rename CLAUDE.md; keep asserted strings (test_claude_contract.py etc.).
- Tests: 181 passed (benchmark 35, self_update 14, claude_hooks 91, model_router 41).

### W2 batch 1 done (2026-10-21T21:50+03:00, worker)
- CLAUDE.md rewritten in place as compact core (23,692 B -> 12,114 B); moved text verbatim to NEW `.claude/skills/vault-agent-contract/SKILL.md`.
- RULES_CHECK.md: 55 rule sentences, 52 in core, 3 descriptive detail. All asserted strings kept; tests 55 passed before and after; no test edited.
- Prefix (haiku, hooks off, strict empty MCP, ABBA n=3): 60,606 +-2.1 -> 56,321 +-1.7 = -4,285 (-7.1%); tools pair -4,289; skill is listed.
- Compliance 8 questions: OLD 8/8, NEW 8/8. VERSION mtime unchanged. Report: `07_EVALUATION/reports/cost_router_claude_context_dedup_2026-10-21/DEDUP.md`.
- Next: vault-reviewer on CLAUDE.md diff + RULES_CHECK; W3 pilot.

### R1 done (vault-reviewer opus) + main-session fix
- Review 1 (CLAUDE.md): REJECT -> fixed by main session at the time printed above this block: restored inline the lost/skill-only
  rules (lifecycle/verification/provenance authoritative; multi-agent skill routing to ranked candidates; threat model
  BEFORE implementation + crypto/PDF/signature trust model; residual recorded with the owner; use actual Vault APIs, no
  parallel memory mechanism; keep commits attributable; preserve existing contracts; verifier exception clause; repo is
  the canonical memory source; state-card "this file needs fixing"; checkpoint README pointer; main session checks CI
  once); dispatch sentence made conditional again (router selects another runtime / dispatch authorized; Claude
  subagents via Agent tool); skill PATH named at line 3 for agents without the Skill tool. Pilot runs after this edit
  carry a slightly larger CLAUDE.md (identify by first-request prefix, sd≈2 tokens).
- Review 2 (W1 code): APPROVE WITH NOTES: M `_safe_rel` accepts `D:evil.txt`, `.`, `.git` (write/delete outside or the
  whole worktree) -> fix after W3 finishes; L reset_worktree ignores git return codes; L cache_read_source must state
  1h-write assumption + costUSD is Claude Code's client estimate; L Antigravity policy copy still 0.2 (others' file);
  L NO_SELF_INSTALL does not cover --self-update (user-scope only). INFO: global copy rewritten again 21:41:51 by the
  owner's desktop session fd1a8a49 started at repo root (SessionStart install.py --session-start); now equals the dirty tree.

### W3 pilot done (2026-10-21, vault-worker sonnet)
- Harness: added `--rep-offset N` (continue an --out dir; keeps results.jsonl rows only when N>0; warmup only at 0). 35 benchmark tests pass. Analysis: `30_SCRIPTS/evaluation/phase4_pilot_analysis.py` -> ANALYSIS.md per dir.
- Haiku block: 3 repeats (projection after rep0 ~3.1 > 2.30 -> reduced 4 -> 3 for all tasks). 45 runs, correctness base 15/15, ctx_new 15/15, combined 14/15 (T6 rep1).
- Sonnet block: incomplete: T3 2 pairs, T1 2 pairs, T5 1 pair (T5 rep1 not run, cap). Rows of T3/T1 were rebuilt from stdout logs after a --rep-offset 0 invocation overwrote results.jsonl (my error; metrics recomputed from raw streams, checks marked RECONSTRUCTED).
- VERSION mtime of ~/.claude/skills/cost-router was 21:41:51 at my start (not 21:03:08: changed before this task) and unchanged through all runs.
- Dirs: 07_EVALUATION/reports/cost_router_claude_phase4_pilot_{haiku,sonnet}/ (SUMMARY.md, ANALYSIS.md).
- R1 fixes (2026-10-21, vault-worker sonnet): $0.0161 = final-prefix probe (haiku, 4 runs). Phase-4 running total $4.4365 of $5.00.

### R1 fixes done (2026-10-21)
- [x] `_safe_rel` hardened (drive, ':', abs, '..', empty/'.', '.git' first component) + `_inside` apply-time resolve check for overlay/remove/files; reset_worktree aborts on git checkout/clean rc != 0; cache_read_source states 1h-write assumption and client-side costUSD; tests added (benchmark+hooks+model_router 182 passed).
- [x] Final prefix (CLAUDE.md 13,324 B, haiku, hooks off, strict empty MCP, ABBA n=2): OLD 60,605.5 +-0.7 -> NEW 56,701.5 +-0.7 = -3,904 (-6.4%). Dir: 07_EVALUATION/reports/cost_router_claude_context_dedup_2026-10-21/final_prefix/.

### Final (main session, 2026-10-21)
- R2 (opus, final): A CLAUDE.md APPROVE WITH NOTES, B harness APPROVE WITH NOTES; M `--rep-offset 0` overwrite -> fixed
  (refusal + regression test); L HMAC + local-LLM clauses -> added inline; M manifest provenance of 8 pre-review haiku
  runs -> documented residual. Evidence reconciles on 55 rows. Single verdict: TOKEN SAVINGS NOT DEMONSTRATED
  (prefix −3,904 tok/request and tokens/request −6% demonstrated; total-token paired CI includes 0; combined arm
  regressed on haiku: 14/15, cost +842%).
- Final suite 15 files: 440 passed, 4 skipped, exit 0. Bench worktree removed. Global copy not written by phase 4.
- Spend phase 4 $4.4365 of $5.00; phase 3+4 ≈ $28.9 of $30.
- Report: docs/reports/CLAUDE_CODE_COST_ROUTER_CONTEXT_DEDUP_REPORT.md. Nothing committed.
