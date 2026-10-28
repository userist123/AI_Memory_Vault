---
id: "a12d077f-f7a6-5b21-8450-e9bc93118286"
title: "Checkpoint: Claude Code - cost-router native"
type: "project"
lifecycle: "REVIEW"
created: "2026-10-21T08:00:00Z"
updated: "2026-10-22T00:00:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "00_GOVERNANCE/coordination/tasks/todo-claude-cost-router-native.md"
  created_by: "claude-code"
tags:
  - "checkpoint"
  - "cost-router"
  - "coordination"
---

# todo-claude-cost-router-native

Agent: Claude Code (main session, Fable 5.1) + subagents (Explore/haiku, claude-code-guide, vault-worker/sonnet, vault-reviewer/opus)
Task: Claude Code Cost Router native integration: verify, complete, harden, test, benchmark, independent review, report.
Branch: gemini/casa3d-nextgen-functionality-uiux (shared with unrelated Casa3D + Antigravity work; see Ownership)
Head at start: 2c72a7228
Started: 2026-10-21

## Ownership / do-not-touch
- `.agents/skills/cost-router/**`, `20_TESTS/test_hook_antigravity.py`, `03_IMPLEMENTATION/packages/interfaces/skill_router.py`,
  `todo-antigravity-*.md`: uncommitted in-flight work of the Antigravity session. NOT mine. Baseline failure
  `test_send_message_resume_idempotent_on_same_step` (hook_antigravity.py:262 TypeError) belongs to that work.
- Casa3D files, prompt-compiler-executor: unrelated, untouched.
- Mine: `.claude/skills/cost-router/**`, `20_TESTS/test_cost_router_*.py` (new/extended), `04_CONFIG/claude_model_routing.json`
  (only if policy change is justified), `docs/reports/CLAUDE_CODE_COST_ROUTER_IMPLEMENTATION_REPORT.md`, this file.

## Baseline (2026-10-21, before any change)
- `python -m pytest 20_TESTS/test_cost_router_enforce.py test_cost_router_enforcement.py test_cost_router_repo_map.py
  test_cost_router_self_update.py test_cost_router_skill.py test_cost_router_token_waste.py test_hook_antigravity.py
  test_claude_model_router.py -q` -> 225 passed, 1 failed (Antigravity in-flight), 57s.
- Native adapter ALREADY EXISTS: `.claude/skills/cost-router/` with project hooks in `.claude/settings.json`
  (UserPromptSubmit -> hook_prompt_route; SessionStart startup|resume -> install.py --session-start, startup|clear|compact
  -> hook_repo_map; PreToolUse Agent|Task -> hook_agent_model; PreToolUse Edit|Write|...|Read|Grep|Glob -> hook_enforce_route;
  PreToolUse SendMessage -> hook_resume_guard). `~/.claude/settings.json` has NO hooks (verified 2026-10-21).
- Mission therefore = verify + close gaps, not build a second router (Global Production-Consumer Rule).

## Done
- [x] Orientation, baseline tests, chapter marked.
- [x] Agent A (Explore/haiku): archaeology. Findings: lib shared BY COPY (route.py, local_llm.py identical; repo_map,
      route_state, transcript_usage diverged in the Antigravity fork); no PostToolUse/SubagentStop/PreCompact use;
      transcript_usage sums cache tokens (no split), no direct test; duplicate-hook guard is a text match; ~/.claude has no hooks.
- [x] Agent B (claude-code-guide): native contract from docs (2026-10-21). Docs silent on updatedInput.model for Agent.
- [x] Empirical probe on Claude Code 2.1.304 (artifacts: 07_EVALUATION/reports/cost_router_claude_native_probe_2026-10-21/):
      PreToolUse updatedInput.model on Agent IS honored (opus -> claude-haiku-5-5); PostToolUse Agent tool_response carries
      resolvedModel + usage split + totalTokens/DurationMs/ToolUseCount; UserPromptSubmit has prompt+prompt_id; main
      session agent_id null; `claude -p --output-format json` has num_turns, duration_ms, total_cost_usd, modelUsage.
- [x] Benchmark harness: 30_SCRIPTS/evaluation/claude_cost_router_benchmark.py (A/B via --setting-sources, isolated
      worktree ../AI_Memory_Vault_bench_wt, --overlay for working-tree hooks).
- [x] vault-worker round 1 (sonnet): hook_agent_usage.py (PostToolUse Agent|Task), seen_event/record_agent_usage in
      route_state (sidecars route-<sid>.seen / .agent_usage), usage_split + estimate_cost_usd (policy rate card),
      idempotency in enforce_route/resume_guard/prompt_route, compact-state line in hook_repo_map, SKILL.md §4B,
      20_TESTS/test_cost_router_claude_hooks.py (20 tests). Main session added PostToolUse to install.py HOOK_SPECS +
      test_cost_router_self_update expectations. Suite: 228 passed (8 files), git diff --check clean.
- [x] Benchmark run 1 on HEAD hooks (07_EVALUATION/reports/cost_router_claude_ab_benchmark_2026-10-21_head/, 10 runs,
      sonnet, $2.77 total): INVALID for T3-T5 and a REAL FINDING. Router condition cost +26%, 3/5 tasks failed, 0 subagents.
      Cause 1 (harness): allowlist lacked Task + Bash(route_cli) so the router's own policy (delegate; run Vault router
      gate) was denied -> fixed in TOOLS. Cause 2 (product, F-DEADLOCK): hook_agent_model denies Agent until route_cli ran
      AND hook_enforce_route denies Read/Edit after 3 calls -> both paths closed, headless session cannot finish.
- [x] vault-reviewer (opus) round 1: APPROVE WITH NOTES. M1 lost parallel writes + leaked .tmp (_write_json, Windows
      PermissionError on os.replace); L1 1h cache writes priced at 5m; L2 first-prefix model match; L3 price test on haiku
      masks card use, no prompt_id replay test; I1-I4 info (500-key eviction cannot re-allow; corrupt .seen -> allow;
      requested_model uncapped; resolve_subagent traversal-safe). No auth bypass, no privacy leak, all hooks fail open.
- [x] vault-worker round 2 (sonnet): M1 (retry os.replace 8x backoff ≤0.3s, unlink tmp, agent usage append-only
      route-<sid>.agent_usage.jsonl), L1 (1h/5m split + cache_write_1h), L2 (exact then longest prefix), L3 (opus card
      test, prompt_id replay test), I3 (64-char cap), F-DEADLOCK (marker `delegation_denied` set by hook_agent_model
      before its gate deny; hook_enforce_route then allows with additionalContext). Tests: 30 new + 208 regression green.
- [x] vault-reviewer round 2: APPROVE WITH NOTES; all round-1 + F-DEADLOCK fixed; new LOW torn-line swallow, nit wording,
      uncapped agent_id -> fixed by main session (verified by repro + tests); design residual R1 recorded for the owner.
- [x] Benchmark run 2 (_overlay, sonnet main, router only): 5/5 correct, $1.728 vs baseline $1.225 (+41%).
- [x] Benchmark run 3 (_opus, T1/T3/T5, both conditions): 3/3 both, $2.247 vs $1.604 (+40%). No savings claim.
- [x] Final suite (10 files): 263 passed, 4 skipped, 1 failed (pre-existing Antigravity in-flight). diff --check clean.
      Benchmark worktree removed. Raw benchmark artifacts redacted (<HOME>).
- [x] Report: docs/reports/CLAUDE_CODE_COST_ROUTER_IMPLEMENTATION_REPORT.md. Verdict PARTIALLY COMPLETE (repo work done +
      reviewed; no global activation by design; no measured savings on small tasks).

## Next (owner decisions)
- R1 deadlock-breaker scope; global activation (install.py dry-run first); commit the owned files on a dedicated branch.
- Optional long-session benchmark before any savings claim. Port M1/F-DEADLOCK to the Antigravity fork with its owner.

## Protected operations (NOT authorized, do not perform)
- Editing `~/.claude/settings.json`, installing global hooks, enabling adapter globally, pushing, committing without owner ask.

## Key files
- `.claude/skills/cost-router/SKILL.md`, `install.py`, `hook_*.py`, `lib/*.py`
- `04_CONFIG/claude_model_routing.json`, `00_GOVERNANCE/protocols/Claude_Model_Routing_Policy_V1.md`
- `docs/reports/COST_ROUTER_*.md` (12 prior reports)
