# Claude Code Cost Router: Token Economy Optimization Report (phase 3)

Date: 2026-10-21. Runtime: Claude Code 2.1.304 (Windows 11). Branch `gemini/casa3d-nextgen-functionality-uiux`, HEAD `2c72a7228`. Nothing committed, pushed, installed globally or activated by this mission. Predecessors: `CLAUDE_CODE_COST_ROUTER_IMPLEMENTATION_REPORT.md` (phases 1-2); checkpoint `00_GOVERNANCE/coordination/tasks/todo-claude-cost-router-phase3.md`.

## 1. Verdict

**TOKEN OPTIMIZATION: NOT SUCCESSFUL.** No reproducible decrease in total model tokens per correctly completed task was demonstrated.

- Before the phase-3 fixes (equal arms, sonnet, 6 tasks × 2 arms × 3 repeats = 36 runs + warm-up): the router used **43.5% more total tokens** and cost 21.5% more than the same session with hooks neutralised; a bootstrap CI paired by (task, repeat) is [−75.9%, −10.2%], i.e. the increase is significant. Cause: same-tier delegation on implement tasks (T3/T4: +86% tokens; post-delegation main-session context ≈ half of all tokens).
- After the fixes (T3/T4 only, 12 runs + warm-up): **+6.0% token reduction, CI [−19.9%, +28.7%] includes zero; cost −0.1%; 0 delegations; correctness 6/6 vs 6/6.** Parity within noise: the router no longer adds tokens on same-tier work, but it does not remove any.
- What was achieved: the measured harm was removed (same-tier delegation, denied reads causing retry turns, context lines on allow paths, repo-map re-injection on clear/compact, over-routing read-only agents), measurement integrity was established (per-role attribution without double counting, equal arms), and two policy regressions introduced on the way were caught by review and fixed.
- Supportable claim: "no measurable token change after the fix (neutral on T3/T4, sonnet)". Not supportable: any token or cost saving.

## 2. Exact files changed (phase 3, on top of phases 1-2; `git diff --stat -- .claude 20_TESTS 30_SCRIPTS`: 20 files, +1356/−90, plus untracked)

Modified: `.claude/skills/cost-router/{hook_prompt_route.py, hook_repo_map.py, hook_agent_model.py, hook_enforce_route.py, hook_resume_guard.py, hook_agent_usage.py, hook_self_update.py, lib/route_state.py, lib/transcript_usage.py, SKILL.md}`; `20_TESTS/{test_cost_router_claude_hooks.py, test_cost_router_enforcement.py, test_cost_router_enforce.py, test_cost_router_token_waste.py, test_cost_router_repo_map.py, test_claude_cost_router_benchmark.py}`; `30_SCRIPTS/evaluation/claude_cost_router_benchmark.py`.
New artifacts: `07_EVALUATION/reports/cost_router_claude_ab_benchmark_2026-10-21_phase3_{probe, probe_rerun, final, opus, rerun}/` (privacy-reduced streams, results.jsonl, SUMMARY.md, RUN_INFO.json; home paths redacted; init inventories stripped). Checkpoint `todo-claude-cost-router-phase3.md`; this report.
Not touched: `.agents/skills/**`, `test_hook_antigravity.py`, `03_IMPLEMENTATION/**`, CLAUDE.md/AGENTS.md (proposals only, §5), Casa3D, global settings.

## 3. Measurement (how tokens were counted)

Authoritative provider fields only, from `claude -p --output-format stream-json --verbose --include-hook-events`:
- Per request: assistant events carry `usage` (input_tokens, cache_read_input_tokens, cache_creation_input_tokens with 5m/1h split, output_tokens), `message.id`, `message.model`, `parent_tool_use_id` (null = main session, set = that subagent's request). Assistant events repeat per content block → deduplicated by `message.id` (last usage wins).
- **`result.usage` covers the main session only; `modelUsage` covers all models including subagents** (verified on 26 real streams: deduplicated assistant sums equal `modelUsage` exactly for input, cache_read, cache_creation). Assistant `output_tokens` is a streaming lower bound → an `unattributed_residual` row per model reconciles to `modelUsage`; a negative residual or a model-key mismatch is flagged (none occurred).
- Aggregate, defined once: `total_tokens = Σ over deduplicated requests (main + subagents) of input + cache_read + cache_creation + output`; categories reported separately; cache-read tokens are never presented as equivalent to uncached input. `token_reduction_percent = 100 × (baseline − optimized) / baseline` per category and for the aggregate (positive = fewer tokens with the router).
- Cost: authoritative = Σ `modelUsage.costUSD`; a policy-card recompute is printed as a cross-check and mismatched on sonnet by ≤26% (the card prices sonnet cache reads at $0.20/MTok; the provider bills $0.10; main-session cache writes are 1h, subagent writes 5m) → the card needs correction, modelUsage is used.
- Equal arms: both arms `--setting-sources user,project` (CLAUDE.md, project MCP `vault-memory`, project skills and agents load identically; init model identical on all 56 runs); baseline = every hook neutralised by `COST_ROUTER_OFF=1` (exit 0, no output, no state); optimized = hooks on. Only the user-level `superpowers` plugin SessionStart hook (3405 chars) runs in both arms. First-request prefix: baseline 70.0-71.1k vs router 70.7-71.8k tokens. Warm-up run excluded; ABBA order verified from timestamps; correctness from pytest run inside the worktree.

## 4. Token waste found (audit, chars/4 estimates where no provider field exists)

Per-request prefix in this repository (identical in both arms): CLAUDE.md ≈5.9k tokens (lines 182-460 routing contract ≈2.7k; lines 106-181 duplicate AGENTS.md:159-198, 16 identical lines), vault-memory MCP tool descriptions ≈1.5-2k, skill descriptions ≈0.6k, agent descriptions ≈1.1k. AGENTS.md is not loaded (no `@import`). Router-only injected text: repo map 2,575 chars at the 2500 cap (now 1500, startup only), route line 47-76 chars per prompt (now only when the route changes), hook reasons <200 chars. **Router-only self-overhead measured: ≈352 tokens per run** (SessionStart 1260 + UserPromptSubmit 61-76 + one note ≈76 chars), re-read on ≈5 requests ≈1.8k cache-read tokens ≈0.4% of a run. The measured waste was therefore behavioural: a denied 4th tool call costs a full retry request; a same-tier delegation adds 4-5 worker requests plus 4-7 post-spawn main-session requests (31-44% pre-spawn / 8-15% worker / 44-58% post-spawn of total tokens on T3/T4).

## 5. Optimizations implemented (each env-switchable; estimates were replaced by the measurements in §6)

1. Same-tier rule made effective: `resolve_session_model` (SessionStart payload → stored → last assistant `message.model` in the transcript → `ANTHROPIC_MODEL` → settings `model` local/project/user → None), evaluated at SessionStart and every prompt; when session tier == route tier and the class is not review/design/frontier and risk < high, the state carries `same_tier`, the route line says `cost-router: <class> same tier (<model>): direct work ok` and the enforce hook never denies (one advisory note). Headless payloads carry no `model` (probe-verified), so `-p` runs need the env var or settings. The equal-tier fallback applies the same class/risk exclusions (final-review finding N1, fixed).
2. Read-only tools (Read/Grep/Glob) are never denied by the enforce hook (counted; reason delivered once as context); Edit/Write/NotebookEdit/non-exempt Bash still denied (`COST_ROUTER_DENY_READS=1` restores).
3. Route line only when the route changes, or risk ≥ high, or `COST_ROUTER_VERBOSE_ROUTE=1`.
4. Repo map on `startup` only, default cap 1500 chars (`COST_ROUTER_REPO_MAP=always`, `COST_ROUTER_REPO_MAP_MAX_CHARS`).
5. Agent hook allow path emits no `additionalContext` (reason only); deny paths unchanged.
6. Read-only agent types clamped to the explore class (haiku) — strict allowlist (Read, Grep, Glob, LS, WebFetch, WebSearch, vault-memory read tools; parse failure or unknown tool = not read-only; the project's `Explore` has Bash and is not read-only); never for review/design/frontier briefs or risk ≥ high (`COST_ROUTER_READONLY_FLOOR=1` restores).
7. `COST_ROUTER_OFF=1` kill switch for all hooks (equal-arm baseline and rollback).
8. Prompt byte-identity: no hook alters a subagent's prompt (test).
Governance proposals NOT applied (owner decision; they would cut the shared per-request prefix in both arms): move CLAUDE.md:182-460 to an on-demand document (≈27k cache-read tokens per 10-request run), drop CLAUDE.md:106-181 (duplicate of AGENTS.md; ≈15k), shorten the memory-retrieval section (≈5k), halve the vault-memory tool docstrings (≈5k), trim skill descriptions (≈3k), cut the "Model routing" section to 5 lines (≈4k).

## 6. Benchmark results

### 6.1 `phase3_final` (sonnet, hooks BEFORE items 1 and 6 above; equal arms; 36 runs + 1 warm-up; $10.93; 0 failures)

Pooled (18 vs 18), token_reduction_percent (negative = router used more): input −70.7%, cache_read −46.7%, cache_creation −20.3%, output −63.9%, **total −43.5%**, cost −21.5%. Welch CI95 of the pooled total-token delta [−68.7k, +416.8k] includes 0 (unpaired, heterogeneous tasks); paired bootstrap by (task, repeat) **[−75.9%, −10.2%] excludes 0**. Per task (total tokens): T1 −1.2%, T2 +23.2% (CI incl. 0), T3 −86.2%, T4 −86.2%, T5 −57.9%, T6 −14.4%. Delegations: 1 per router run on T3/T4/T6 (2 on one T4 run), 0 on T1/T2/T5, 0 in every baseline run. Correctness: T1-T5 3/3 both arms; T6 stored score 1/3 both arms, but the T6 admission regex produced 3 false negatives (bare "rămâne" matching harmless text) and one router run failed for real (Edit denied, Agent gate refused, model stopped) → likely true T6 score baseline 3/3, router 2/3 (not parity). Router self-overhead ≈331-381 tokens/run (the SUMMARY's 1023 includes the plugin hook present in both arms).

### 6.2 `phase3_opus` (opus main, T3 n=2, T4/T6 n=1; $9.15, cap hit): total −43.0%, cost −114.6% (one T6 router outlier: 134k output tokens, $3.26, budget stop), 4/4 both arms; underpowered, no conclusion.

### 6.3 `phase3_rerun` (sonnet, hooks AFTER all fixes; T3/T4; 3 repeats, ABBA, warm-up; `--env ANTHROPIC_MODEL=sonnet` in both arms; $3.14; 0 failures; overlay manifest hash in RUN_INFO equals the reviewed working tree)

| task | arm | n | correct | input | cache_read | cache_creation | output | total_tokens | cost USD | turns | deleg. |
|---|---|---|---|---|---|---|---|---|---|---|---|
| T3 | baseline | 3 | 3/3 | 11±2 | 441,328±107,739 | 46,399±295 | 1,935±84 | 489,673±108,049 | 0.2491±0.0124 | 8.3±0.6 | 0 |
| T3 | router | 3 | 3/3 | 9±1 | 322,381±53,352 | 46,873±864 | 1,837±276 | 371,099±53,888 | 0.2381±0.0097 | 7.7±0.6 | 0 |
| T4 | baseline | 3 | 3/3 | 10±2 | 377,179±83,809 | 45,013±706 | 1,682±284 | 423,884±84,649 | 0.2346±0.0132 | 9.3±1.5 | 0 |
| T4 | router | 3 | 3/3 | 11±2 | 440,031±107,341 | 45,858±833 | 1,846±553 | 487,747±108,730 | 0.2459±0.0196 | 10.0±3.5 | 0 |

| task | input | cache_read | cache_creation | output | total | CI95 total delta (router−base) | cost | CI95 cost delta |
|---|---|---|---|---|---|---|---|---|
| T3 | +23.5% | +27.0% | −1.0% | +5.1% | +24.2% | [−418.5k, +181.4k] incl. 0 | +4.4% | [−0.050, +0.028] incl. 0 |
| T4 | −13.3% | −16.7% | −1.9% | −9.8% | −15.1% | [−278.4k, +406.2k] incl. 0 | −4.8% | [−0.047, +0.070] incl. 0 |
| ALL | +6.2% | +6.9% | −1.4% | −1.8% | **+6.0%** | [−171.3k, +116.6k] incl. 0; paired bootstrap [−19.9%, +28.7%] | −0.1% | [−0.021, +0.021] incl. 0 |

Correctness parity 6/6 vs 6/6; 0 delegations in 12/12 router runs; router-only overhead ≈352 tokens/run. T1/T2/T5/T6 and opus sessions were not re-measured after the fixes (budget: phase-3 spend ≈ $24.8 of the $30 cap).

## 7. Tests and security checks

- Final suite, 11 files: `python -m pytest 20_TESTS/test_cost_router_claude_hooks.py …_enforce.py …_enforcement.py …_repo_map.py …_skill.py …_token_waste.py …_self_update.py test_claude_model_router.py test_claude_cost_router_benchmark.py test_install_design_stack.py test_hook_antigravity.py -q -p no:cacheprovider` → **359 passed, 4 skipped, 1 failed** (the T6 admission-regex test, which encoded the over-strict regex; regex and test reconciled, then `test_claude_cost_router_benchmark.py` + `test_cost_router_claude_hooks.py` → **118 passed, exit 0**). Both-order isolation with the Antigravity suite: 108 passed. `git diff --check` on owned paths: no whitespace errors (only LF/CRLF warnings).
- Independent reviews (Opus, own context, read-only): review A (hooks + attribution) REJECT → H1 (Explore treated as read-only despite Bash), H2 (frontmatter parser mis-classified write-capable agents), M3 (review briefs clamped), M4 (probe overlay on one arm), L5 (residual/model-key flags) — all fixed and re-verified; final review: all prior findings fixed, new N1 (equal-tier fallback ignored class/risk on opus sessions) fixed by the main session with two tests; attribution integrity confirmed on 56 streams; fail-open confirmed (hooks × bad inputs); Bash write forms still denied; crafted agent names fall back safely; `COST_ROUTER_OFF` disables only efficiency/policy hooks, no Memory Vault boundary depends on hooks.
- Not gamed: optimized-arm correctness came from pytest inside the worktree; no required test or verification was skipped; the only behavioural change on the optimized arm in the rerun was "do not delegate on the same tier".

## 8. Remaining limitations

- No post-fix measurement on T1/T2/T5/T6 or on an Opus main session; n=3 per cell on two tasks cannot detect <≈25% effects (power estimate: 10-30 runs per arm).
- Session model resolution: headless payloads carry no `model`; interactive `/model` switches are not seen until the next prompt reads the transcript; a `.claude/settings.json` `model` that differs from the real model misleads the resolver (use `ANTHROPIC_MODEL`).
- The policy rate card over-prices sonnet cache reads (card $0.20 vs billed $0.10/MTok) → fix the card before using `estimate_cost_usd` for reporting.
- T6 correctness check needs the structural `--list` check to run before the admission regex (regex short-circuits); three phase3_final T6 runs were never structurally scored.
- Benchmark arms share a bimodal first-request prefix (≈71k vs ≈75-78k) of unknown origin; the user-level plugin hook runs in both arms; overlay contents are hashed but not archived; the harness is untracked.
- **Global copy side effect:** at 15:40 a headless probe launched by a subagent from the repository root (not the benchmark worktree) triggered the repository's own `SessionStart` → `install.py --session-start`, which rewrote `~/.claude/skills/cost-router/` with the working-tree hooks (skill files only; `~/.claude/settings.json` untouched; VERSION `2c72a7228+dirty`). This is the pre-existing self-update mechanism (phase-2 risk R5), not a deliberate install, and `install.py --verify` now reports the copy DIFFERENT from the final source. The Antigravity fork is unchanged.

## 9. Rollback

- Per feature (no reinstall): `COST_ROUTER_DENY_READS=1`, `COST_ROUTER_VERBOSE_ROUTE=1`, `COST_ROUTER_REPO_MAP=always`, `COST_ROUTER_REPO_MAP_MAX_CHARS=2500`, `COST_ROUTER_READONLY_FLOOR=1`; everything off: `COST_ROUTER_OFF=1`.
- Repository: the owned files in §2 are uncommitted; `git checkout -- <path>` restores HEAD (never run on the other agents' dirty files).
- Global skill copy: to return it to HEAD's content, run HEAD's installer from a clean checkout (`git worktree add ../vault-head HEAD && python ../vault-head/.claude/skills/cost-router/install.py --session-start`, skill files only), or `python .claude/skills/cost-router/install.py --uninstall` to disable; verify with `install.py --verify`.

## 10. Next steps (owner)

1. Decide the governance dedup proposals (§5) — the only lever that reduces the shared per-request prefix measurably.
2. Fix the sonnet cache-read price in `04_CONFIG/claude_model_routing.json`; archive overlay contents per run; track the harness.
3. If a token claim is wanted: re-run all six tasks after the fixes, sonnet and opus, ABBA, ≥10 repeats per arm, with the structural T6 check first.
4. Decide what to do with the rewritten global copy (§8) and whether the repository SessionStart self-update should require explicit opt-in.
