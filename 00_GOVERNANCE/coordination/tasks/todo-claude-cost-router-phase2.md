---
id: "4b8c67a5-8296-5ab3-8bd0-ea26d989db3b"
title: "Checkpoint: Claude Code - cost-router phase 2"
type: "project"
lifecycle: "REVIEW"
created: "2026-10-21T08:00:00Z"
updated: "2026-10-22T00:00:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "00_GOVERNANCE/coordination/tasks/todo-claude-cost-router-phase2.md"
  created_by: "claude-code"
tags:
  - "checkpoint"
  - "cost-router"
  - "coordination"
---

# todo-claude-cost-router-phase2

Agent: Claude Code (new session, phase 2). Predecessor checkpoint: `todo-claude-cost-router-native.md` (phase 1, done).
Task: FINAL COST OPTIMIZATION AND RELEASE GATE for the Claude Code cost-router. Do not rebuild the adapter.
Branch: gemini/casa3d-nextgen-functionality-uiux (shared, dirty with other agents' work). HEAD 2c72a7228. Nothing committed in phase 1.
Started: 2026-10-21

## Read first (excerpts, not whole files)
- `docs/reports/CLAUDE_CODE_COST_ROUTER_IMPLEMENTATION_REPORT.md` (phase-1 report: §10 benchmark table, §13 risks R1-R5)
- `todo-claude-cost-router-native.md` (what was verified, probe facts, review findings)
- `.claude/skills/cost-router/SKILL.md` §2A, §4B; hooks `hook_prompt_route.py`, `hook_repo_map.py`, `hook_agent_model.py`,
  `hook_enforce_route.py`, `hook_agent_usage.py`; `lib/route_state.py`, `lib/repo_map.py`, `lib/transcript_usage.py`
- Harness: `30_SCRIPTS/evaluation/claude_cost_router_benchmark.py` (A/B via `--setting-sources user` vs `user,project`,
  `--overlay` = working-tree hooks, strips the `install.py --session-start` entry in the bench worktree, local paths must
  be redacted to `<HOME>` in artifacts before any commit). Artifacts: `07_EVALUATION/reports/cost_router_claude_ab_benchmark_2026-10-21_{head,overlay,opus}/`
  (keep; do not overwrite), probe: `07_EVALUATION/reports/cost_router_claude_native_probe_2026-10-21/`.

## Ownership (do not touch)
- `.agents/skills/cost-router/**`, `20_TESTS/test_hook_antigravity.py`, `03_IMPLEMENTATION/packages/interfaces/skill_router.py`,
  `todo-antigravity-*.md`: Antigravity session's uncommitted work. Read-only for Priority 1 (reproduce + attribute), never edit.
- Casa3D, prompt-compiler-executor: unrelated.
- `~/.claude/**`, `~/.gemini/**`: never write. No commit, no push, no global activation.

## Phase-1 facts you can rely on (RUNTIME_VERIFIED on Claude Code 2.1.304)
- PreToolUse `updatedInput.model` on Agent is honored; PostToolUse Agent carries resolvedModel + usage split; main session
  payload agent_id null; `claude -p --output-format json` gives total_cost_usd, usage, modelUsage, num_turns, duration_ms.
- Baseline failure `test_hook_antigravity.py::test_send_message_resume_idempotent_on_same_step` (TypeError at
  `.agents/skills/cost-router/hook_antigravity.py:262`) exists before and after phase 1; Explore agent inferred a stale
  route_state copy is imported (hook passes `current_step_idx=` but the loaded `resume_count` lacks it). UNVERIFIED.
- Benchmarks: router condition +41% (sonnet main, 5/5 correct) and +40% (opus main, 3/3) on 10-75 s tasks; T3 opus:
  main $0.73 + sonnet worker $0.09 vs baseline $0.51. Overhead suspects: repo map injected at SessionStart (~10-20k
  cache-write tokens), route lines on every prompt, delegation round-trip (fresh subagent re-reads), enforce denials.
- Review residual R1: after an Agent-gate refusal (`delegation_denied` marker) the main session may finish the prompt
  directly; only `hook_enforce_route.py` reads the marker; only the gate branch of `hook_agent_model.py` writes it.
- R5: project SessionStart runs `install.py --session-start`, which rewrites `~/.claude/skills/cost-router` from the repo
  copy (pre-existing). ~/.claude/settings.json has no hooks (so no duplicate execution today); different-path copies
  would NOT be deduplicated by Claude Code.

## Priorities (execute all, in order; one implementation agent at a time; vault-reviewer before DONE)
P1 Reconcile the failing test: reproduce at recorded baseline (`git stash` is FORBIDDEN; instead run the test against
   HEAD content via `git show HEAD:<path>` into a scratch dir, and against the worktree) ; attribute the defect (which
   file/copy, whose change); DO NOT edit the test or the Antigravity files. Run Claude-Code-specific tests separately:
   `python -m pytest 20_TESTS/test_cost_router_claude_hooks.py 20_TESTS/test_cost_router_enforce.py 20_TESTS/test_cost_router_enforcement.py 20_TESTS/test_cost_router_repo_map.py 20_TESTS/test_cost_router_self_update.py 20_TESTS/test_cost_router_skill.py 20_TESTS/test_cost_router_token_waste.py 20_TESTS/test_claude_model_router.py -q -p no:cacheprovider`
   (phase-1 result: 238 passed). Report baseline vs current separately.
P2 Profile and reduce fixed overhead: measure per feature by toggling in the bench worktree (overlay + env/flags you add):
   (a) no repo map, (b) compact route line, (c) no enforce/delegation, (d) full. Candidates: lazy/conditional repo map
   (only when the prompt is non-trivial or on `source=startup` of long sessions; cache by HEAD already exists), shorter
   route context, skip delegation when the task class is explore/summarize and the prompt is short (expected benefit <
   round-trip cost). Never override an explicit user `model`; never touch Memory Vault protections. Add tests.
P3 Resolve R1: evaluate (i) N extra direct calls after a gate refusal, (ii) breaker only when Bash is not permitted /
   route_cli unavailable, (iii) route_cli invoked by the hook itself (hook_agent_model could call the Vault router in-process
   and record the receipt, removing the need for the model to run Bash). Pick the safest practical one, add tests for
   successful routing AND legitimate fallback, document trade-offs in SKILL.md.
P4 Long-session benchmark: extend the harness with T6 (multi-step, several agents, repeated handoffs, >= 10 turns) and
   repeats (>= 2 where affordable; budget ~$15 total, report the spend). Report variance, correctness, total cost,
   uncached input, cached input, output, tool calls, delegation count, latency. Preserve all historical artifacts in new
   dirs `07_EVALUATION/reports/cost_router_claude_ab_benchmark_2026-10-21_phase2_*`. No savings claim unless measured;
   else state the conditions where the router should be enabled and recommend a lower-overhead default.
P5 Runtime integrity: document exactly what `install.py --session-start` does to the global copy (read install.py:298-350),
   precedence/duplicate risks for Claude Code and Antigravity, and add a parity check (hash/version of repo copy vs
   installed copy vs documented version) that cannot silently diverge: e.g. `install.py --verify` printing both hashes and
   a test. Do not modify global config.
Final: vault-reviewer (opus, own context) on the final diff + benchmark methodology; fix Critical/High; re-run tests;
   update `docs/reports/CLAUDE_CODE_COST_ROUTER_IMPLEMENTATION_REPORT.md` (changed files, commands+results, P1 root cause,
   overhead by feature and optimization achieved, long-session results + variability, risks, installation status) and
   this checkpoint. Verdict: READY FOR CONTROLLED ACTIVATION / PARTIALLY READY / NOT READY.

## Done
- [x] P1 analysis  - [x] P1 fix (20_TESTS/cost_router_isolation.py; both orders 52/52; 9 files 265 passed)
- [x] P2 (ablation + repo-map cap + compact route; 193 green)
- [x] P3 (in-hook Vault router in hook_agent_model: ROUTED/PLANNED -> receipt + allow, BLOCKED/unavailable -> deny +
      delegation_denied; breaker capped FREE_MAIN_CALLS+6 in hook_enforce_route; real router through the hook:
      "vault-router: ROUTED -> vault-worker"; 136 green)
- [x] P4: harness extended (T6 long-multistep max_turns 40, stream-json tool_calls/delegations, mean±sd, Welch CI95,
      --max-total-usd, redaction; 7 unit tests). Final A/B `..._phase2_final/` (sonnet, 6 tasks x 2 cond x 2 reps =
      24 runs, all correct, $7.06): router mean cost higher on every task (+1.6% T2 .. +73.8% T4; T6 +28.3%); NO CI
      excludes zero (n=2). Router delegated exactly once on T3/T4/T6 (implement class -> vault-worker on the SAME
      model as the main session) and never on T1/T2/T5 -> the extra cost is the delegation round-trip without a cheaper
      tier. Router latency lower on T3/T4/T6. Recommendation implemented as O-1 (see remediation): enforce delegation
      only when the session model tier is above the route tier; otherwise advisory.
- [x] hook remediation: H-1, M-1, M-2, L-1, L-2, L-3, I-3, S-1, O-1 (session_model via hook_repo_map ->
      route-<sid>.session_model; enforce allows with advisory note when route tier not below session tier). 8-file suite
      265 passed; both-order isolation 68/68.
- [x] harness remediation: stream whitelist (init reduced to type/subtype/model/version/cwd=<WT>; result minus ids);
      metrics summed over all result events; latency=wall; T6 `--list`+plan()+admission regex; T2 rewritten vs HEAD;
      `--order abba` default + `--warmup`; stricter T1/T3/T4/T5; RUN_INFO.json (version, argv, overlay hash); 14 tests;
      `--recompute` applied to phase2_final: T2 excluded, T6 1/2 in BOTH arms, cost deltas unchanged (+9..+74%, no CI
      excludes zero, sign test 0/5 cheaper p=0.062); privacy grep clean (benign hits only).
- [x] round-2 delta review of hooks: all round-1 findings fixed (verified against the real router: BLOCKED quotes
      `rejected`; dispatcher/local_llm route -> M-2 deny, no receipt). NEW HIGH in O-1: `>=` made the advisory fire
      when the route is ABOVE the session (sonnet session doing a risk=high review routed to opus) -> main session
      fixed to `==` (advisory only on equal tiers) + tests (haiku session now enforced; sonnet session + review/opus
      route enforced). LOW: session model stale after `/model` (documented in SKILL.md); duplicate `rejected`
      entries (deduped + warnings appended). Pre-existing: install.py --session-start traceback when ~/.claude is a
      file (wrapper exits 0; left). Round 2 was the last review round: no further review; residuals recorded.
- [x] final: 11-file suite 306 passed / 4 skipped / exit 0; both-order isolation 69/69; diff --check clean; privacy
      grep clean; bench worktree removed; global copy untouched (no VERSION there). Report updated:
      docs/reports/CLAUDE_CODE_COST_ROUTER_IMPLEMENTATION_REPORT.md. VERDICT: PARTIALLY READY (safe + reviewed, no
      measured savings, T6 parity unknown, Opus-arm re-run outstanding, Antigravity fork not ported). No commit/push.

## Next (owner)
- Decide default (advisory on equal tier) + global activation per report §12; re-run benchmark (abba, warmup, ≥15 reps,
  opus arm); port to Antigravity fork; commit owned files on a dedicated branch.
- [x] methodology review: SOUND WITH CAVEATS (cost headline only). Cost complete (total = sum modelUsage). Findings:
      H-A parse_stream keeps only the LAST `result` event; delegated router runs have 2 main turns -> usage/num_turns/
      latency are per-turn artifacts ("router faster" is false; real wall T3 70-78 s vs 20-33 s). H-B T6_router_1 admits
      an unfixed Windows-path issue -> T6 router 1/2; check too weak (run `--list`, json.loads, compare plan()).
      H-C T2 premise (PLUGINS_DIRECTORY/@anthropic-plugin-directory) does not exist at HEAD -> rewrite. M-A conditions
      differ beyond hooks: router arm loads project MCP `vault-memory` + 4 project skills (+10.5k prefix tokens; map ~0.7k);
      CLAUDE.md loading in baseline unknown -> canary. M-B fixed baseline-then-router order, cold cache on first run
      -> warm-up + ABBA. M-C weak checks T1/T3/T4/T5. M-D T6 has 1 delegation, not repeated handoffs. M-E cost
      decomposition: extra cost = main session (explore + Edits blocked by hook before delegating + 2nd turn), worker
      ~$0.09-0.16, same tier. M-F PRIVACY: raw/*.stream.jsonl `system/init` events list the owner's 25 plugins, 89 MCP
      connectors + auth status, synced-plugin UUIDs, session ids, pipe name -> strip before any commit (untracked now).
      L stats: n=2 -> per-task "no conclusion"; 6/6 direction (sign p≈0.03); 15-30 runs/arm needed; opus arm is the
      only setting where a cheaper tier can save. L report §9/§10 stale; L reproducibility (version, argv, overlay hash).
- [ ] harness remediation worker (after hook remediation): strip init fields in harness at write time AND in the existing
      phase2_final/phase2_ablation stream files; sum all result events; latency = wall; T6 strong check; T2 rewritten vs
      HEAD; ABBA + warm-up option; record version/argv/overlay manifest hash; stronger T1/T4 checks; tests.
- [x] review round 1 of phase-2 hooks/install/isolation diff: REJECT. H-1 `test_cost_router_token_waste.py:162-173
      ::test_cached_route_receipt_is_accepted_for_the_same_task_class` expects deny, now allow (in-hook router runs the
      real route_cli; env-dependent). M-1 route_cli exits 1 on BLOCKED -> hook says "unavailable"; `policy_gates` is a
      fixed list, real cause is in `rejected`; fake router in tests exits 0 on BLOCKED (wrong contract). M-2 any
      ROUTED/PLANNED unlocks the Claude spawn even when `execution_contract.execute_via` != `claude_code_agent_tool`
      (e.g. dispatcher/local_llm). L-1 `--goal <goal>` breaks on dash-leading goals -> use `--goal=`. L-2 5 s timeout
      not a hard bound when route_cli spawns a child inheriting pipes (12 s measured) -> stdin DEVNULL, stdout to temp
      file, kill on timeout. L-3 verify() only checks VERSION non-empty -> validate line 1 format and line 2 == installed
      manifest hash. I-1 global hook would execute the opened project's routing package (trusted-workspace bar) -> doc.
      I-3 compact line lost "(high)" risk tag -> add `risk=high` token when risk >= high. Passed: P1 argv/no shell,
      P2, P5 cap sequences, P6 fail-open, P7 verify, P8 both orders 114/114.
- [ ] REMEDIATION worker (dispatch AFTER P4 finishes: one implementer at a time): fix H-1 (update the test to the new
      semantics with the fake vault, keep a deny case), M-1, M-2 (deny with reason "Vault router routed to <runtime>
      (<execute_via>); dispatch via `python -m routing.route_cli dispatch` or do it directly" + delegation_denied,
      capped), L-1, L-2, L-3, I-3; fake router exits 1 on BLOCKED; add a test asserting no `shell=True` (inspect
      source or monkeypatch subprocess.run); re-run 8-file Claude suite + both-order isolation run; then delta review.
- [x] P5 analysis + fix (install.py --verify/--json, VERSION on every install incl. session-start; 12 tests)
- [ ] review  - [ ] report

## P1 root cause (Explore, 2026-10-21) — CORRECTS phase-1 attribution
- `test_hook_antigravity.py::test_send_message_resume_idempotent_on_same_step` PASSES alone (file alone: 18 passed).
  It fails only when the Antigravity suite and the Claude-side cost-router tests run in ONE pytest process: both forks
  `sys.path.insert` their own `lib/` and import bare `route_state` / `transcript_usage`; `sys.modules` keeps whichever
  loaded first. `.claude/.../lib/route_state.py:206 resume_count(session_id, agent_id)` lacks `current_step_idx`
  (added uncommitted in `.agents/.../lib/route_state.py:187` by the Antigravity session, checkpoint 2026-10-20T12:52Z);
  reverse order breaks `test_cost_router_claude_hooks.py` (`transcript_usage.is_parent_session` missing). Not an
  Antigravity defect; a test-isolation defect on BOTH sides. Claude-only suite: 238 passed; Antigravity alone: 18 passed.
- Fix (Claude side only, next worker): in every Claude-side test module that inserts `.claude/skills/cost-router/lib`,
  add a module-scoped autouse fixture that pops `route_state`, `transcript_usage`, `route`, `repo_map`, `local_llm`
  from `sys.modules` at setup and teardown (or load via importlib under unique names). Never edit the Antigravity files.
  Prove: combined run of both families in one process, both orders, green.

## Notes / blockers
- Remote Control session: a new session could not be started; phase 2 runs in the phase-1 session (~340k ctx) with
  all work in fresh-context subagents. Route receipt for class `design` recorded via route_cli (gate otherwise denies).
- P5 analysis (Explore, 2026-10-21): `install.py --session-start` = no_hook+no_context, skips on `~/.claude/cost-router.disabled`,
  copytree SKILL_SRC -> staging (ignores __pycache__, chat) + lib/claude_model_router.py + policy.json, agents/*.md with
  agents.sha256.json, atomic swap; rewrites UNCONDITIONALLY, no hash compare, VERSION written only with --source-ref
  (installed copy has NO VERSION). Global hooks written only by plain `install.py` / `--self-update` (merge_hook) ->
  then project + global entries would both fire; `lib/route.py:41-56 is_shadowed` guards by substring
  `cost-router/<script>` in ~/.claude/settings.json text (wrapper/renamed command double-fires). hook_self_update fetches
  GitHub main (throttle 3600 s), never the local tree. Today: ~/.claude/settings.json has no hooks; installed copy stale
  in 9 files + lacks hook_agent_usage.py; ~/.gemini copy is a partial fork (own hook_antigravity.py, no install.py),
  hooks in ~/.gemini/config/hooks.json. TODO (worker, after P2): `install.py --verify` (manifest repo vs installed vs
  VERSION, exit 1 on divergence; write VERSION from `git rev-parse HEAD` + dirty flag on every install incl. session-start)
  + pytest with tmp --home.
- P2 result (worker, $4.64, n=1, T1/T3/T5 sonnet, overlay): injected text is negligible (map 2361 chars ≈590 tok at the new
  2500 cap; compact route line 47 chars); cost is trajectory-driven (turns, cache writes); variants within noise (~2x
  per task). Applied: COST_ROUTER_REPO_MAP_MAX_CHARS (default 2500), COST_ROUTER_NO_REPO_MAP=1, compact route line
  default (COST_ROUTER_VERBOSE_ROUTE=1 restores long form), harness `--router-env`. Artifacts:
  07_EVALUATION/reports/cost_router_claude_ab_benchmark_2026-10-21_phase2_ablation/ABLATION.md. 193 tests green.
- P3 DECISION (main session): option (iii). `hook_agent_model.py` runs the Vault router itself
  (`python -m routing.route_cli route --goal "<first 300 chars of tool_input.prompt or description>"`, PYTHONPATH=
  03_IMPLEMENTATION/packages, subprocess, timeout 5 s; measured wall 0.2 s) when the gate would otherwise deny, records
  the receipt (`add_routed_class`, plus status/subagent_type in state) and ALLOWS, adding one additionalContext line
  "vault-router: <status> -> <subagent_type>". Deny only when status is BLOCKED (reason = gates) or the router fails /
  times out; in those cases keep the `delegation_denied` breaker but cap it: at most FREE_MAIN_CALLS+N (N=6) further
  direct calls per epoch, then deny again with a reason that names the blocker. Explicit `#direct` unchanged. Rationale:
  the canonical router still runs before every dispatch (stronger than relying on the model to run Bash), no deadlock,
  bounded fallback. Trade-off: +0.2 s per first spawn of a class; the hook depends on the routing package being present
  (fail -> current behaviour). Tests: successful in-hook route -> allow + receipt; BLOCKED -> deny + breaker capped;
  router missing -> deny + breaker; cap reached -> deny; new epoch resets.
- Observation: PreToolUse Agent hook raised Explore (read-only) subagents to sonnet because it derives the model from the
  session's route class (design->implement), not from the agent type -> include in P2/P3 overhead notes.
