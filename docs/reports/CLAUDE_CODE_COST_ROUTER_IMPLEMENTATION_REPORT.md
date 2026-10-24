# Claude Code Cost Router: Native Integration Implementation Report

Date: 2026-10-21 (phase 1 + phase 2). Runtime: Claude Code 2.1.304 (Windows 11, Git Bash hooks). Branch `gemini/casa3d-nextgen-functionality-uiux`, HEAD `2c72a7228` (unchanged; nothing committed, pushed, installed globally or activated).

## 1. Executive summary and final verdict

**Final verdict: PARTIALLY READY.**

- The native adapter is complete, hardened and independently reviewed (phase 1: two Opus rounds; phase 2: three Opus passes — hooks, delta, benchmark methodology). All Critical/High findings are fixed and re-tested; residuals are listed in §13.
- Test evidence: final run of 11 files **306 passed, 4 skipped, exit 0**, including the Antigravity suite in the same process (the phase-1 "pre-existing failure" was a test-isolation collision, now fixed on the Claude side only; §7).
- Runtime integrity: `install.py --verify` (manifest + VERSION on every install) closes the silent-divergence gap; no global change was made (§12).
- Cost: with the hooks as they were, the router was **more expensive on every benchmarked task** (sonnet main session, 24 runs, +9..+74% mean cost, no per-task CI excludes zero at n=2; sign test 0/5 cheaper, p≈0.06). The extra cost is the main session's own trajectory (exploration, Edits blocked by the enforce hook, then a second turn after a same-tier worker returns), not the injected context (repo map ≈590 tokens, route line 47 chars) and not the subagent's fresh context. The lower-overhead default implemented from that evidence (O-1): enforcement is advisory when the route tier equals the session tier; a route above the session (risk floor, review -> opus) or below it (opus session -> sonnet worker) stays enforced. **Savings are not demonstrated**; the only configuration where a cheaper tier can save (Opus/Fable main session) was measured once at n=1 (+40%) before phase-2 changes and must be re-measured before any claim.
- Why not READY FOR CONTROLLED ACTIVATION: no measured cost benefit, T6 correctness 1/2 in both arms, benchmark confounders (§9), the session-model rule is blind to `/model` switches, and the Antigravity fork is not ported. Why not NOT READY: the adapter is safe (fail-open, no authorization bypass, no privacy leak), per-project activation is already live and tested, and the owner can activate the installed copy under the documented procedure with rollback.

## 2. Repository state

Start of phase 1: HEAD `2c72a7228`, 23 dirty paths from other agents (Antigravity fork `.agents/skills/cost-router/**` + its uncommitted test, Casa3D docs, prompt-compiler-executor). End of phase 2: same HEAD, no commit. Owned changes (`git diff --stat -- .claude 20_TESTS 30_SCRIPTS`: 19 files, +1023/−75) plus untracked files listed in §5. Unrelated dirty files untouched. `git diff --check` clean on owned paths. Benchmark worktree `../AI_Memory_Vault_bench_wt` removed after each phase (`git worktree remove --force`, `prune`). Nothing written under `~/.claude` or `~/.gemini` by this mission (tests use temp homes; the benchmark worktree's settings had the self-update entry stripped); `~/.claude/skills/cost-router` still has no VERSION, which proves it was not rewritten by the mission.

## 3. Agent roles and work completed

| Role | Agent (model) | Phase-2 work |
|---|---|---|
| Orchestrator / architect | main session (Fable 5.1; ~420k context, so all work ran in fresh subagents) | gap decisions, P3 design, O-1 final fix, report |
| Archaeology / runtime specialist | `Explore` (Haiku→Sonnet by hook), `claude-code-guide` | phase 1 |
| P1 attribution, P5 analysis | `Explore` | test-isolation root cause; self-update/precedence analysis |
| Implementation | `vault-worker` (Sonnet), 5 rounds, one at a time | P2 ablation + overhead cuts; P1 isolation + P5 `--verify`; P3 in-hook router; remediation (H-1..S-1, O-1); harness fixes + artifact privacy strip |
| Independent review | `vault-reviewer` (Opus), 3 passes | hooks (REJECT→fixed), delta (REJECT on O-1→fixed by main), methodology (SOUND WITH CAVEATS) |

## 4. Architecture and native contract (unchanged from phase 1, verified empirically)

Shared core by copy (`lib/route.py`, `lib/local_llm.py` identical to the Antigravity fork; `route_state`, `transcript_usage`, `repo_map` diverged before this mission); policy `04_CONFIG/claude_model_routing.json`; Claude adapter = hooks in `.claude/skills/cost-router/` wired by `.claude/settings.json` (UserPromptSubmit, SessionStart ×2, PreToolUse ×3, PostToolUse). Verified on 2.1.304: PreToolUse `updatedInput.model` on Agent is honored; PostToolUse Agent carries `resolvedModel` + 4-way usage; main-session payloads have `agent_id: null`; hooks of one event run in parallel; identical command strings are deduplicated across settings files, different paths are not. Artifacts: `07_EVALUATION/reports/cost_router_claude_native_probe_2026-10-21/`.

## 5. Exact changed files

Modified: `.claude/settings.json`; `.claude/skills/cost-router/{SKILL.md, hook_agent_model.py, hook_enforce_route.py, hook_prompt_route.py, hook_repo_map.py, hook_resume_guard.py, install.py, lib/route_state.py, lib/transcript_usage.py}`; `20_TESTS/{test_cost_router_enforce.py, test_cost_router_enforcement.py, test_cost_router_repo_map.py, test_cost_router_self_update.py, test_cost_router_skill.py, test_cost_router_token_waste.py, test_install_design_stack.py}`; `30_SCRIPTS/setup/install_design_stack.py` (marketplace fix, unrelated to routing).
New: `.claude/skills/cost-router/hook_agent_usage.py`; `20_TESTS/{cost_router_isolation.py, test_cost_router_claude_hooks.py, test_claude_cost_router_benchmark.py}`; `30_SCRIPTS/evaluation/claude_cost_router_benchmark.py`; `07_EVALUATION/reports/cost_router_claude_{native_probe_2026-10-21, ab_benchmark_2026-10-21_head, _overlay, _opus, _phase2_ablation, _phase2_final}/` (raw artifacts, home paths redacted, init events reduced); `00_GOVERNANCE/coordination/tasks/todo-claude-cost-router-{native,phase2}.md`; this report.
Not touched: `.agents/skills/**`, `20_TESTS/test_hook_antigravity.py`, `03_IMPLEMENTATION/**`, Casa3D, global config.

## 6. What phase 2 changed in behaviour

- **P1 isolation** (`20_TESTS/cost_router_isolation.py`): Claude-side tests purge bare `route_state`/`transcript_usage`/… from `sys.modules` and drop the `.claude` lib path on teardown, so both forks' test families run in one process in either order.
- **P2 overhead**: `COST_ROUTER_REPO_MAP_MAX_CHARS` (default 2500, floor 500), `COST_ROUTER_NO_REPO_MAP=1`; compact route line by default (`class -> target, model [risk=high] [lang=ro]`, ≤120 chars), long form under `COST_ROUTER_VERBOSE_ROUTE=1`; harness `--router-env`.
- **P3 Vault-router gate** (`hook_agent_model.py`): where the gate used to deny and ask the model to run `route_cli` in Bash, the hook runs the Vault router itself (`--goal=<first 300 chars of the subagent prompt>`, argv list, stdin DEVNULL, stdout temp file, 5 s timeout with process-tree kill; 0.2 s measured). ROUTED/PLANNED with `execute_via == claude_code_agent_tool` → receipt + allow + `vault-router: <status> -> <subagent>`; BLOCKED → deny quoting the deduplicated `rejected` list + warnings; non-Claude `execute_via` (dispatcher/local_llm) → deny naming the runtime, no receipt; router unavailable → deny with cause. Each deny sets `delegation_denied`, after which `hook_enforce_route` allows at most FREE_MAIN_CALLS+6 direct calls per prompt epoch (capped breaker), then denies naming the blocker. `#direct` unchanged. Verified against the real router for all three paths.
- **O-1 tier rule** (`hook_repo_map.py` stores the SessionStart `model`; `hook_enforce_route.py`): when the route tier equals the session tier the 4th direct call is allowed with a one-time advisory note; above/below or unknown → enforced as before (final form after the round-2 review).
- **P5 integrity** (`install.py`): `manifest()`, `--verify [--json]` (repo manifest vs installed manifest vs VERSION; exit 1 on any difference, missing VERSION, bad ref format or line-2 hash mismatch), VERSION (`<HEAD>[+dirty]` / manifest hash) written on every install including `--session-start`.
- Phase-1 items retained: PostToolUse telemetry (`hook_agent_usage.py --report`), per-event idempotency, atomic writes with retry, append-only telemetry with torn-line guard, 1h/5m cache pricing from the policy card.

## 7. Test commands and results

- Final (phase 2): `python -m pytest 20_TESTS/test_cost_router_claude_hooks.py 20_TESTS/test_cost_router_enforce.py 20_TESTS/test_cost_router_enforcement.py 20_TESTS/test_cost_router_repo_map.py 20_TESTS/test_cost_router_self_update.py 20_TESTS/test_cost_router_skill.py 20_TESTS/test_cost_router_token_waste.py 20_TESTS/test_claude_model_router.py 20_TESTS/test_claude_cost_router_benchmark.py 20_TESTS/test_install_design_stack.py 20_TESTS/test_hook_antigravity.py -q -p no:cacheprovider` → **306 passed, 4 skipped, exit 0** (91.9 s).
- Isolation in both orders: `test_hook_antigravity.py` + `test_cost_router_claude_hooks.py` → 69 passed (and 68/68 before the last test was added); Claude-only 8 files → 265 passed (reviewer-verified).
- Baseline vs current, Antigravity test: alone 18/18 at HEAD-content and in the worktree; combined with Claude tests it failed before phase 2 (both orders) and passes now.
- `git diff --check` on owned paths: exit 0.

**Root cause of the baseline failure** (`test_hook_antigravity.py::test_send_message_resume_idempotent_on_same_step`, TypeError on `current_step_idx`): both forks insert their own `lib/` on `sys.path` and import bare module names; `sys.modules` keeps the first copy, so the Antigravity hook (uncommitted work of the Antigravity session, checkpoint 2026-10-20T12:52Z, which adds `current_step_idx` to its own `route_state`) called the `.claude` copy's older signature when the Claude tests loaded first. Not a defect of the Antigravity work; a test-isolation defect, resolved on the Claude side without touching their files.

## 8. Review findings and remediation

Phase 1: M1 lost parallel writes/leaked tmp, L1 1h cache price, L2 prefix match, L3 test gaps, I3 cap → fixed; torn-line swallow → fixed.
Phase 2 hooks pass (REJECT): H-1 env-dependent test (`test_cost_router_token_waste.py`) → rewritten on a fake Vault with deny and allow cases; M-1 BLOCKED reported as "unavailable" (route_cli exits 1) → JSON status parsed first, `rejected` quoted; M-2 non-Claude route unlocked the spawn → deny; L-1 dash-leading goal, L-2 timeout not hard, L-3 VERSION content unchecked, I-3 risk tag, S-1 no-shell guard test → fixed.
Delta pass (REJECT on O-1 only): `>=` relaxed the risk floor for sonnet/haiku sessions → fixed by the main session to `==` with two new tests (haiku session now enforced; sonnet session + review/opus route enforced). LOW: duplicate `rejected` entries → deduped; session model stale after `/model` → documented (§13).
Methodology pass: see §9. Round 2 was the last allowed review round; residual items are recorded in §13 for the owner.

## 9. Benchmark methodology (final harness)

`30_SCRIPTS/evaluation/claude_cost_router_benchmark.py`: detached worktree at HEAD, reset per run; baseline `claude -p --setting-sources user` vs router `user,project` with `--overlay` (working-tree hooks; self-update entry stripped); `--output-format stream-json --verbose` with a whitelist of stored events (init reduced to type/subtype/model/version/`cwd=<WT>`; no plugin/MCP inventories, no session ids); metrics summed over all `result` events (cost from the last, cumulative; verified equal to the sum of `modelUsage.costUSD` in all 24 runs); `tool_calls`/`delegations` from `tool_use` blocks; `latency_s` = wall; Welch CI95 (scipy) + sign test across tasks; `--order abba` (default) + `--warmup`; per-task `max_turns`/budget; `--max-total-usd`; `RUN_INFO.json` (Claude version, argv, overlay manifest hash, platform, timestamps); `--recompute` to re-derive summaries from raw files. Tasks T1 explore, T2 diagnose (rewritten against HEAD), T3 small change + pytest, T4 multi-file rename, T5 review, T6 long multi-step (`--list` JSON vs `plan()`, pytest, and an "unfixed issue admitted" regex). 14 unit tests.

Caveats found by the methodology review and carried into the numbers below: the `phase2_final` run used the pre-fix harness (fixed order, no warm-up; T6 baseline run 1 had a cold cache) and the pre-fix T2/T6 checks (T2 excluded post hoc: its premise did not exist at HEAD; T6 re-scored 1/2 in both arms); the router arm also loads the project MCP server and four project skills (+10.5k prefix tokens, of which the repo map is ~0.7k), so the conditions differ by more than the hook layer; whether the baseline arm loads CLAUDE.md is unverified. Token/latency columns were recomputed on 2026-10-21 after the multi-turn fix.

## 10. Measured results, overhead by feature, optimization achieved

Final A/B `07_EVALUATION/reports/cost_router_claude_ab_benchmark_2026-10-21_phase2_final/` (sonnet main, hooks before O-1, 2 repeats, $7.06):

| task | base USD | router USD | delta | CI95 (USD) | correct base / router |
|---|---|---|---|---|---|
| T1 explore | 0.183 | 0.204 | +11.4% | [−0.003, +0.045] | 2/2 / 2/2 |
| T3 small change | 0.243 | 0.399 | +64.5% | [−0.018, +0.331] | 2/2 / 2/2 |
| T4 multi-file | 0.230 | 0.400 | +73.8% | [−0.284, +0.624] | 2/2 / 2/2 |
| T5 review | 0.229 | 0.249 | +9.0% | [−0.130, +0.171] | 2/2 / 2/2 |
| T6 long multi-step | 0.417 | 0.535 | +28.3% | [−0.897, +1.133] | 1/2 / 1/2 |

No CI excludes zero (n=2, df≈1); the direction is consistent (router costlier in 5/5 scored tasks and in 11 of 12 repeat pairs; sign test p≈0.06 over 5 tasks, ≈0.03 over the original 6). Corrected latency: router 33–102 s vs baseline 10–52 s (the earlier "router faster" reading was a last-turn artifact). Router output tokens ≈2× baseline. Delegations: exactly 1 per router run on T3/T4/T6 (vault-worker, sonnet, background), 0 on T1/T5, 0 in every baseline run although Agent was allowed.

Overhead by feature (ablation `..._phase2_ablation/ABLATION.md`, n=1, T1/T3/T5, $4.64; and the stream decomposition): repo map ≈590–990 tokens cached per session and route line 47–650 chars → a fraction of a cent, within noise; hook process start + state I/O not measurable in cost; **delegation on the same tier** → first main turn ≈ the whole baseline cost (explore, Edits blocked by the enforce hook, then delegate), worker $0.09–0.16, second main turn $0.03–0.07. Earlier runs: phase-1 `_head`/`_overlay` (sonnet, n=1) +26/+41%, `_opus` (opus main, n=1, T1/T3/T5) +40%.

Optimization achieved: O-1 removes the same-tier delegation requirement that caused the measured overhead (advisory note instead of deny), repo-map cap and compact route line reduce injected context by ~40% and ~90% respectively; the in-hook router removes the Bash round-trip the gate used to require. **Effect on cost after these changes has not been re-measured** (budget stop; a re-run needs ≥15 runs/arm in ABBA order with warm-up, plus an Opus main-session arm).

Recommended default: keep the hooks active per project (as today) with enforcement effective only when the session tier is above the route tier (O-1); treat the router on Sonnet sessions as a discipline/quality tool (receipts, telemetry, model clamps), not a cost saver; do not enable enforcement globally until an Opus-arm benchmark shows a saving.

## 11. Security and failure semantics

All cost-router hooks fail open (exit 0, no stdout) on malformed/empty input, missing fields, unknown events, state dir replaced by a file, missing policy (reviewer-verified for every changed hook × 8 bad inputs). The in-hook router uses an argv list (no shell; guard test), a hard 5 s timeout with tree kill, temp-file stdout, never stores or prints the goal; it executes the opened project's `03_IMPLEMENTATION/packages/routing` package, so it requires the same trusted-workspace bar as project hooks (relevant only if the global copy is ever installed). Receipts are per (session, class), as before. The session model can only be written by `hook_repo_map` from the harness payload (validated, capped). Memory Vault protections are untouched. Privacy: all artifacts redacted (`<HOME>`), init-event inventories stripped, grep clean.

## 12. Installation status and rollback

Live: project hooks active for sessions started in this repo (as before); PostToolUse telemetry included. Global: `~/.claude/settings.json` has no hooks; the global skill copy is stale (9 files) and has no VERSION; it was not rewritten by this mission. The project SessionStart self-update rewrites `~/.claude/skills/cost-router` unconditionally from the repo copy on each session start in this repo (pre-existing; now also writes VERSION); `hook_self_update.py` (global only, hourly) installs from GitHub `main`, never the local tree.
Controlled activation (owner, separate authorization): `python .claude/skills/cost-router/install.py --verify` (expect exit 1 now: stale copy) → `python .claude/skills/cost-router/install.py` (installs the copy and registers global hooks; project entries then become shadowed by `is_shadowed`, a substring guard on `cost-router/<script>` in the global settings text) → `--verify` (expect 0) → one Agent call, then `python .claude/skills/cost-router/hook_agent_usage.py --report`. Rollback: `install.py --uninstall` (removes owned entries, writes the disabled marker) or restore the backup install.py writes. Duplicate-execution check: no global and project entry may both reference cost-router scripts unless the global copy is the one answering (`is_shadowed`).

## 13. Remaining risks (owner decisions)

- R1 (closed by design (iii) + cap): after a BLOCKED/unavailable/non-Claude route the main session gets FREE_MAIN_CALLS+6 direct calls; beyond that it is denied with the blocker named.
- R2: Antigravity fork lacks the retry, markers, in-hook router and tier rule; port with its owner.
- R3: cost benefit unmeasured after phase-2 changes; Opus-arm measurement outstanding; T6 quality parity unmeasured (1/2 both arms).
- R4: session model stale after `/model` (stored on startup/clear/compact only); `resume` not verified to keep the session id.
- R5: benchmark arms differ by project MCP/skills loading; CLAUDE.md loading in the baseline arm unverified.
- R6: `is_shadowed` is a substring match; a wrapper/renamed global command would double-fire.
- R7: pre-existing: `install.py --session-start` prints "skipped…" and can traceback when `~/.claude` is a file (wrapper exits 0).

## 14. Next steps

1. Owner: accept the recommended default (§10) or choose another; decide global activation (§12).
2. Re-run the benchmark on the final hooks: `--order abba --warmup --repeats 15` on T3/T4/T6 (≈$20–25) plus `--model opus` arm; only then make or retract any savings claim.
3. Port M1/F-DEADLOCK/in-hook router/tier rule to the Antigravity fork with that session; mirror `step_idx` into the Claude `route_state` if the owner wants one shared core.
4. Commit the §5 files on a dedicated branch (artifacts are redacted; keep the raw dirs or move them under `08_OBSERVABILITY/artifacts`).
