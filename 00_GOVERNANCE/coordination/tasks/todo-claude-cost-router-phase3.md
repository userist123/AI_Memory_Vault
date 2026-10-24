---
id: "a6adebe1-9965-5c0a-b4d9-de8ace48bba7"
title: "Checkpoint: Claude Code - cost-router phase 3"
type: "project"
lifecycle: "REVIEW"
created: "2026-10-21T08:00:00Z"
updated: "2026-10-22T00:00:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "00_GOVERNANCE/coordination/tasks/todo-claude-cost-router-phase3.md"
  created_by: "claude-code"
tags:
  - "checkpoint"
  - "cost-router"
  - "coordination"
---

# todo-claude-cost-router-phase3 — TOKEN ECONOMY OPTIMIZATION

Agent: Claude Code (phase 3). Predecessors: `todo-claude-cost-router-native.md` (phase 1), `todo-claude-cost-router-phase2.md` (phase 2).
Task: minimise total MODEL TOKENS per correctly completed task for the Claude Code cost-router, preserving correctness,
security, required verification and routing policy. API cost is secondary (report both). Do not rebuild the router.
Branch gemini/casa3d-nextgen-functionality-uiux, HEAD 2c72a7228, dirty with other agents' work. No commit/push/global
changes/activation. Never touch `.agents/skills/**`, `20_TESTS/test_hook_antigravity.py`, `03_IMPLEMENTATION/**`, Casa3D,
`~/.claude/**`, `~/.gemini/**`. One implementation agent at a time; vault-reviewer (Opus, own context) before DONE.
Spend cap for benchmarks: $30 total (print running spend; stop at cap).

## Read first (excerpts)
- `docs/reports/CLAUDE_CODE_COST_ROUTER_IMPLEMENTATION_REPORT.md` §6, §9, §10, §13 (what exists, what was measured, caveats)
- `30_SCRIPTS/evaluation/claude_cost_router_benchmark.py` (header + parse_stream + run_once + summarize), its 14 tests
- `.claude/skills/cost-router/SKILL.md` §2A/§4A/§4B; hooks `hook_prompt_route.py`, `hook_repo_map.py`, `hook_agent_model.py`,
  `hook_enforce_route.py`, `hook_agent_usage.py`; `lib/route_state.py`, `lib/transcript_usage.py`
- Artifacts (keep; new runs go to `07_EVALUATION/reports/cost_router_claude_ab_benchmark_2026-10-21_phase3_*`):
  `..._phase2_final/` (24 runs, privacy-reduced streams), `..._phase2_ablation/ABLATION.md`

## Verified facts to rely on (phase 1-2, Claude Code 2.1.304)
- stream-json: assistant events carry per-request `usage` and `parent_tool_use_id` (null = main session; set = that
  subagent's request); `result` events are per main turn (cumulative `total_cost_usd` on the last; `usage` per turn);
  `modelUsage` sums all models incl. subagents and equals `total_cost_usd`. UNVERIFIED and REQUIRED first: whether
  `result.usage` includes subagent tokens (compare sum of assistant usage split by parent_tool_use_id vs result usage on
  the phase2_final streams; document the finding and build the attribution on assistant events, not on result).
- Phase-2 final A/B (sonnet main, hooks before O-1): router +9..+74% cost, 0/5 tasks cheaper; extra cost = main session
  trajectory (explore, Edits blocked by enforce hook, 2nd turn after a same-tier worker), not injected text (repo map
  ~590 tok cached/session at the 2500 cap; route line 47 chars). Router arm also loaded project MCP `vault-memory` + 4
  project skills (+10.5k prefix tokens) because baseline used `--setting-sources user` -> the arms differ beyond hooks.
- O-1 (implemented, unmeasured): enforcement advisory when route tier == session tier; above/below enforced.
- In-hook Vault router (hook_agent_model) adds `vault-router: ...` context line (≤120 chars) on gated spawns.
- `claude -p` flags available: --model, --max-turns, --max-budget-usd, --setting-sources, --allowedTools,
  --output-format stream-json --verbose, --include-hook-events, --no-session-persistence, --mcp-config, --strict-mcp-config.

## Plan
P0 Measurement integrity (worker, harness only): (a) attribution from assistant events: per run compute, per
   (role=main|subagent:<agentType>, model): input, cache_read, cache_creation (split 5m/1h if present), output; verify no
   double counting vs result/modelUsage and document in the harness header which provider totals include subagents;
   (b) aggregate definition ONCE: `total_tokens = Σ_messages (input + cache_read + cache_creation + output)` over main +
   subagents; also report each category; `token_reduction_percent = 100*(base-opt)/base` per category and aggregate;
   cost from modelUsage (authoritative) and a recomputed cost from the policy card as a cross-check;
   (c) EQUAL ARMS: both arms `--setting-sources user,project`; baseline = hooks neutralised by a single env switch
   `COST_ROUTER_OFF=1` (new, read by every hook at entry: exit 0, no output, no state) so MCP/skills/CLAUDE.md load
   identically; optimized = hooks on; pass env via `--router-env` for BOTH arms (rename to `--env` for both + keep the
   old flag); `--order abba --warmup` default; record RUN_INFO; tests for (a)-(c).
P1 Waste audit (Explore, read-only, with numbers): token size (chars/4 and, if cheap, a real count via one `claude -p`
   "say ok" run with `--include-hook-events`? no — use chars/4 + the prefix size measured in streams) of: CLAUDE.md,
   AGENTS.md, `.claude/agents/*.md`, `.claude/skills/*/SKILL.md` (which are auto-loaded?), `.mcp.json` tool descriptions
   (vault-memory tool list size), repo map, route line, hook additionalContext per event, subagent briefs the hooks
   inject (SubagentStart? none), `~/.claude/CLAUDE.md` if present (read-only). Duplicated text across CLAUDE.md vs
   AGENTS.md vs SKILL.md (quote overlapping sections with line ranges). Which of these are injected per session vs per
   prompt vs per subagent. Rank by tokens × frequency in the phase2_final streams (count assistant requests per run).
P2 Optimizations (worker; each with an estimate and a measured delta from P3; do not add layers):
   candidates, in priority order: (1) O-1 already: no same-tier delegation; (2) enforce hook: never deny Read/Grep/Glob
   (reads are cheaper than the retry turn a deny causes; keep counting; deny only Edit/Write/Bash when enforced) —
   measure; (3) route line only when the class/model changes vs the previous prompt in the session (state has
   prompt_hash/epoch) — else emit nothing; (4) repo map: inject only on `startup` (not clear/compact) unless
   COST_ROUTER_REPO_MAP=always, and cap 1500 by default — measure whether tasks lose correctness; (5) `vault-router:`
   line only on BLOCKED/deny (not on allow); (6) subagent briefs: when hook_agent_model rewrites `updatedInput`, do NOT
   add text to the prompt; (7) duplicated instructions: PROPOSE (do not edit) a dedup of CLAUDE.md vs AGENTS.md vs
   SKILL.md with exact line ranges and estimated per-session savings — governance files need the owner;
   (8) hook_agent_usage: nothing injected (verify). Every change keeps tests green; add tests.
P3 Benchmark (worker runs harness; equal arms; tasks T1 explore, T2 diagnose (HEAD-valid), T3 small change+tests,
   T4 multi-file+tests, T5 security review, T6 long multi-step; `--order abba --warmup --repeats 3` (≈36 runs + warm-ups,
   sonnet; est. $12-15), then if budget allows `--model opus` on T3/T4/T6 `--repeats 2`). Report per task and overall:
   baseline vs optimized per category, per model and role, % change per category, aggregate token_reduction_percent,
   cost delta (separately), correctness/test parity, turns, delegations, CI95 + sd; router self-overhead = tokens of
   hook-injected context (route line, repo map, vault-router line, notes) counted from the streams.
P4 Independent review (vault-reviewer): measurement integrity/double counting, hidden arm differences (diff the init
   events and first-request prefix sizes between arms), savings obtained by skipping required work (diff the
   optimized-arm transcripts for skipped tests/verification), subagent attribution, routing/security regressions,
   reproducibility (re-run 2 cells). Fix material findings; re-run affected tests/benchmarks.
P5 Report: update `docs/reports/CLAUDE_CODE_COST_ROUTER_IMPLEMENTATION_REPORT.md` (new §15 Token economy) or a new
   `docs/reports/CLAUDE_CODE_COST_ROUTER_TOKEN_ECONOMY_REPORT.md` with: files changed, tests/security checks, baseline
   vs optimized tokens by category, reduction per task + overall, cost reduction separately, parity, CI/variance,
   router self-overhead, limitations, rollback (env switches, `install.py --uninstall`). Verdict: TOKEN OPTIMIZATION
   SUCCESSFUL only if aggregate tokens decrease reproducibly with parity; else NOT SUCCESSFUL (say why).

## Done
- [x] P0 (harness: attribute()/dedup_requests/policy_cost/summarize_tokens, --env, equal arms via COST_ROUTER_OFF=1
      [disabled_by_env() in every hook], --legacy-baseline, --include-hook-events; 25 harness tests, 59 hook tests;
      181 passed in the 5-file run). FINDINGS: result.usage = MAIN ONLY; modelUsage = all models incl. subagents and
      equals the deduplicated assistant-request sums for input/cache_read/cache_creation EXACTLY; assistant events repeat
      per content block (dedup by message.id; legacy streams lack it -> heuristic key); assistant output_tokens is a
      streaming lower bound -> `unattributed_residual` row so total_tokens == modelUsage; policy-card cost recompute is
      +5..+20% above modelUsage.costUSD (cause unknown; modelUsage authoritative). Legacy phase2_final recompute (unequal
      arms): router used 44-118% MORE total tokens than baseline on every task.
- [x] P1 audit (see Notes)
- [x] P2 (worker): enforce never denies Read/Grep/Glob (counted; reason once as context; COST_ROUTER_DENY_READS=1
      restores); route line only on route change / risk>=high / VERBOSE; repo map startup-only, cap 1500
      (COST_ROUTER_REPO_MAP=always); Agent hook allow path without additionalContext; read-only agent types clamped to
      explore class even on risky sessions (COST_ROUTER_READONLY_FLOOR=1 restores); prompt byte-identity test; SKILL.md
      env table. 8-file suite 281 passed; isolation 83/83. Savings are ESTIMATES until P3.
- [x] P3 (spend ≈$21.1 of $30): probe found 2 harness bugs (hook chars counted twice; --overlay applied to router arm
      only -> probe arms unequal; both fixed, probe numbers discarded). phase3_final (sonnet, 36 runs + 1 warm-up, ABBA,
      $10.93, 0 failures): pooled token_reduction_percent input -70.7%, cache_read -46.7%, cache_creation -20.3%,
      output -63.9%, TOTAL -43.5% (router used MORE), cost -21.5%; CI95 total-tokens delta [-68.7k, +416.8k] includes 0;
      per task total: T1 -1.2%, T2 +23.2% (CI incl. 0), T3 -86.2%, T4 -86.2%, T5 -57.9%, T6 -14.4%; correctness 16/18
      both arms (T6 1/3 both). Router self-overhead ≈1.0k tokens/run (overstated: includes a 3405-char user-level
      superpowers plugin hook present in BOTH arms). Policy-card cost recompute mismatches modelUsage on sonnet (31/36
      flagged, ≤26%) -> modelUsage authoritative. phase3_opus (T3 n=2, T4/T6 n=1, $9.15, cap hit): total -43%, 4/4 both,
      one T6 router outlier (134k output, $3.26, budget stop). VERDICT SO FAR: NO token reduction; router uses more.
- [x] DIAG: `session_model` was never set (headless SessionStart payload carries no `model`; hook_repo_map only stores
      it when present) -> `_advisory_note` returned None -> enforce denied the 4th call (194-char reason seen as soft
      context in streams) and the route line advertised "-> vault-worker subagent" -> compliant model spawned (1/run);
      baseline (hooks off) 0 spawns in 6/6. Token split router T3/T4: pre-spawn main 31-44%, subagent 8-15%, POST-spawn
      main 44-58% -> the post-delegation main context is the real cost, not the worker.
- [x] FIX+RERUN: strict read-only allowlist (`_frontmatter_tools`, `_read_only_agent`; Explore with Bash -> NOT
      read-only; clamp only role None, no risk rule, class ∉ {unclassified, review, design, frontier});
      `resolve_session_model` (payload > stored > transcript last assistant model > env ANTHROPIC_MODEL > settings
      local/project/user > None; headless payloads carry NO model, probe-verified) on SessionStart + every prompt;
      `same_tier` flag -> line "cost-router: <class> same tier (<model>): direct work ok", advisory honours it; harness:
      overlay on both arms incl. warm-up, reason_chars, residual_negative/attribution_mismatch flags. 9-file suite 332
      passed; isolation 106/106 both orders. RERUN `..._phase3_rerun/` (T3/T4 sonnet, 12 runs + warm-up, $3.14,
      `--env ANTHROPIC_MODEL=sonnet` both arms): 0 delegations in 12/12, 6/6 correct both arms; token_reduction_percent
      total T3 +24.2%, T4 -15.1%, ALL +6.0% (CI95 [-171k, +117k] includes 0); cost ALL -0.1% (CI incl. 0). Parity within
      noise; no demonstrated reduction. Phase-3 spend ≈ $24.5 of $30.
- [x] P4 review B (final): Part A REJECT on new N1 (equal-tier fallback in `_advisory_note` ignored class/risk ->
      opus session + review route not enforced) -> fixed by main session (class review/design/frontier or risk
      high/critical never relaxed) + 2 tests; L1 docstring fixed; L2 duplicate `tools:` key keeps first (residual);
      L3 stored session model sticky (documented). Part B SOUND WITH CAVEATS: equal arms verified (56 runs), no double
      counting, dedup present, ABBA real, warm-up excluded; T6 admission regex over-strict (bare "rămâne") -> fixed +
      test reconciled; router-only overhead ≈352 tok/run; sonnet cache_read card price $0.20 vs billed $0.10 explains
      the cross-check gap; paired bootstrap on phase3_final [-75.9%, -10.2%] excludes 0 (router used more).
- [x] P5 report: docs/reports/CLAUDE_CODE_COST_ROUTER_TOKEN_ECONOMY_REPORT.md. VERDICT: TOKEN OPTIMIZATION NOT
      SUCCESSFUL (harm removed, parity within noise on T3/T4, no reproducible reduction). Final: 11-file suite 359
      passed / 4 skipped / 1 failed (regex test) -> reconciled, affected files 118 passed; isolation 108/108.
- SIDE EFFECT (report §8): ~/.claude/skills/cost-router rewritten at 15:40 by the repo's own SessionStart self-update,
  triggered by a subagent's headless probe run from the repo root (session 60aef3dd…); settings untouched; `--verify`
  now reports DIFFERENT vs final source. Owner decides restore/refresh. Spend phase 3 ≈ $24.8.

## Next (owner)
- Governance dedup proposals; fix sonnet cache_read price on the card; decide global copy; optional full re-run
  (6 tasks, sonnet+opus, ≥10 reps) before any token claim; commit owned files on a dedicated branch.
- [x] P4 review A: REJECT (hooks), attribution SOUND (totals == modelUsage on 26 real streams; dedup keeps last usage;
      cache split consistent). HIGH-1 `hook_agent_model.py:214` treats type `Explore` as read-only without the frontmatter
      check, but `.claude/agents/Explore.md` has `tools: Read, Grep, Glob, Bash` -> critical implement brief sent to
      Explore gets haiku (was opus); explicit model=opus lowered. HIGH-2 `:228-232` frontmatter parser: YAML list
      (`- Edit`), `[Read, Edit]`, PowerShell, MultiEdit, mcp write tools all classified read-only -> haiku on critical
      work. MED-3 read-only auditor (Read/Grep/Glob) gets haiku for a review/verify-class brief (owner rule: high risk
      verification never below Opus). MED-4 phase3_probe baseline invalid (overlay applied to router arm only at that
      time; harness now overlays both arms at line ~1112, no regression test). LOW-5 residual can go negative silently;
      model-name mismatch between assistant events and modelUsage double-counts. Info: enforce matcher excludes
      PowerShell/MultiEdit (pre-existing); COST_ROUTER_OFF also disables policy controls (not memory boundaries).
      Probes passed: Bash write forms still denied; crafted names fall back to old behaviour; fail-open 144 cases; mutations caught.
- [ ] FIX (after P3 finishes; hooks must not change mid-benchmark because --overlay recopies per run): read-only =
      agent type whose frontmatter `tools` is parseable AND every tool ∈ {Read, Grep, Glob, (LS), WebFetch?no} — strict
      allowlist, any parse failure/unknown -> NOT read-only (old behaviour); drop the bare `explore` shortcut; never clamp
      when the brief class is review/verify or risk>=high and the agent is not read-only; tests for HIGH-1/2, MED-3;
      harness: regression test "overlay applies to both arms", flag negative residual, assert attributed models ⊆
      modelUsage keys. Then check phase3 streams for any `subagent:Explore` spawn: if none, data valid; else re-run those cells.
- [ ] P5

## Notes / blockers
- Session: new session refused again (Remote Control); phase 3 runs in the phase-1 session (~460k ctx), all work in
  fresh-context subagents.
- P1 audit (Explore, chars/4): first-request prefix baseline 62.9k vs router 74.6k (+11.7k) — PROJECT context loaded
  only in the router arm (old `--setting-sources user` baseline): CLAUDE.md 23.7 KB ≈5.9k tok (lines 182-460 routing
  contract ≈2.7k; lines 106-181 duplicate AGENTS.md:159-198, 16 identical lines), vault-memory MCP tools ≈1.5-2k,
  skill descriptions ≈570, agent descriptions ≈1.1k, repo map 2,575 B ≈640/session, route line ≈35/prompt, hook reasons
  <100 each; AGENTS.md not loaded (no @import); ~/.claude/CLAUDE.md absent; no SubagentStart hook. Router-attributable
  token waste is BEHAVIOURAL: denies -> retry turns; delegation -> 6-13 extra subagent requests each re-reading a prefix;
  2 main turns per delegated run. Governance proposals (owner only, not edited): move CLAUDE.md:182-460 on demand
  (≈27k cache-read/run at 10 requests), drop CLAUDE.md:106-181 (≈15k), shorten memory-retrieval text (≈5k), halve MCP
  docstrings (≈5k), trim skill descriptions (≈3k), cut "Model routing" to 5 lines (≈4k).
- Observed this session: hook_agent_model raised read-only `Explore` subagents to Opus via the session-route "risk
  floor" (class review/design + risk=high) — the floor must apply to verifier/implementer roles, never to explore.
- P2 targets (router scope): (1) enforce: never deny Read/Grep/Glob (count, deny only Edit/Write/Bash); (2) route line
  only when class/model changed vs previous prompt; (3) repo map only on `startup`, cap 1500; (4) `vault-router:` line
  only on deny; (5) risk floor not applied to Explore/read-only agent types; (6) no text added to subagent prompts.
