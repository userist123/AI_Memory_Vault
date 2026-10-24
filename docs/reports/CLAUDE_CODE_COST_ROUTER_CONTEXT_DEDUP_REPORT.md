# Phase 4 — Context Deduplication and Progressive Disclosure: Release Gate Report

Date: 2026-10-21. Claude Code 2.1.304, Windows 11. Branch `gemini/casa3d-nextgen-functionality-uiux`, HEAD `2c72a7228` (unchanged). Nothing committed, pushed, merged, installed globally or activated by this phase. Checkpoint: `00_GOVERNANCE/coordination/tasks/todo-claude-cost-router-phase4.md`.

## Verdict

**TOKEN SAVINGS NOT DEMONSTRATED.**

Demonstrated (TEST_VERIFIED from raw provider usage): the always-loaded context per request fell by **3,904 tokens (−6.4%, sd < 1 token)**, and tokens per request fell by about 6% (paired CI [−7.5%, −4.7%]) with correctness parity (15/15 vs 15/15). Not demonstrated: a reproducible fall in **total** tokens per completed task. The context-only arm's point estimate is −20.8% total tokens, but the paired CI [−2.9%, +37.3%] includes zero, because turn-count variance (2-3× per task) swamps a ~4k-per-request effect at n = 3. The combined arm (new context + router hooks on) regressed correctness (14/15) and cost on a Haiku session.

## 1. Repository state

- Start: HEAD `2c72a7228`. `CLAUDE.md`, `AGENTS.md`, `.mcp.json`, `.claude/agents`, `04_CONFIG` and the vault-memory server were clean. Dirty paths of other agents are listed in the checkpoint (Phase A) and stayed untouched.
- End: the same HEAD. Owned diff, from `git diff --stat -- CLAUDE.md .claude 04_CONFIG 30_SCRIPTS 20_TESTS` (phases 1-4 cumulative): 22 files, +1438/−458. Untracked additions are listed in §2.
- `AGENTS.md`, `.mcp.json`, `.claude/agents/*`, `VAULT_STATE.md` and `cognitive_core/` are unchanged.
- The benchmark worktree `../AI_Memory_Vault_bench_wt` was removed at the end.

## 2. Files changed in phase 4 and ownership

- `CLAUDE.md` (shared governance file, edited under this mission's owner authorization and reviewed twice): 23,692 B → 13,538 B, rewritten as a compact always-on core. Every security, authority, memory-trust, owner-privacy, protected-core, verification and fail-closed rule stays inline.
- `.claude/skills/vault-agent-contract/SKILL.md` (new): the moved detail, verbatim from HEAD lines 25-79, 114-169 and 182-454. Its path is named on line 3 of `CLAUDE.md`, so agents without the Skill tool can Read it.
- `04_CONFIG/claude_model_routing.json`: sonnet `cache_read` changed from 0.20 to 0.10 per MTok, with a `cache_read_source` note.
- `.claude/skills/cost-router/install.py`: `COST_ROUTER_NO_SELF_INSTALL=1` makes `--session-start` inert.
- `30_SCRIPTS/evaluation/claude_cost_router_benchmark.py`: repo-root guard, self-install env for every child, `--conditions-file`/`--probe`, overlay content archive plus MANIFEST, reconciliation table, T6 structural check first, `_safe_rel`/`_inside` path hardening, git return-code checks, `--rep-offset`, and an offset-0 overwrite refusal. The new `30_SCRIPTS/evaluation/phase4_pilot_analysis.py` produces the paired-bootstrap tables.
- Tests: `20_TESTS/test_claude_cost_router_benchmark.py`, `test_cost_router_self_update.py`, `test_cost_router_claude_hooks.py` (pricing). No governance test was edited.
- Evidence: `07_EVALUATION/reports/cost_router_claude_context_map_2026-10-21/`, `…context_dedup_2026-10-21/` (`DEDUP.md`, `RULES_CHECK.md`, `COMPLIANCE_ANSWERS.md`, `final_prefix/`), and `cost_router_claude_phase4_pilot_{haiku,sonnet}/`.
- Not touched: `AGENTS.md`, `GEMINI.md`, the vault-memory MCP server and tool descriptions, `.claude/agents/*`, other skills, `.agents/**`, and every other agent's files.

## 3. Source-to-context map

Provider-measured on Haiku with single-turn probes: first-request prefix (input + cache_read + cache_creation), mean of 2, sd ±2 tokens. Source: `CONTEXT_MAP.md`.

| Source | Loading mechanism (proved by ablation) | Measured tokens per request | Status after phase 4 |
|---|---|---|---|
| Built-in floor (system prompt + tools) | always | 49,194 | out of scope |
| `CLAUDE.md` | project memory, every request | 8,984 | **≈5,080** (−3,904), progressive disclosure |
| `AGENTS.md` | loaded **only as a fallback when CLAUDE.md is absent** (5,731) | 0 | untouched; `CLAUDE.md` must never be deleted |
| `vault-agent-contract` skill | name + description listed (≤300 chars); body on demand | included in the −3,904 net | new |
| Cost-router hooks (repo map + route line) | SessionStart / UserPromptSubmit | +657 | unchanged in phase 4 (≈352 router-only per run after phase-3 cuts) |
| vault-memory MCP (project) | `.mcp.json` | +411 | unchanged: not worth the risk to tool semantics |
| Project skills / agents (descriptions) | listing | ≈0 in a no-tool probe | unchanged |
| superpowers plugin (user scope) | user plugin | +1.3k | owner scope, not changed |
| User-level MCP connectors | user config | ≈10.2k, spread 8.7k-11.8k | owner scope; removed from both benchmark arms with `--strict-mcp-config` |

**Duplication:** `CLAUDE.md` duplicated 16 lines of `AGENTS.md`, which costs Claude nothing, because AGENTS.md is not loaded while CLAUDE.md exists. It also repeated the memory-retrieval instructions served by the MCP server, and the cost-router policy in SKILL.md. These were compressed in the core.

## 4. Footprint per source, pre and post

| Source | Pre | Post |
|---|---|---|
| `CLAUDE.md` | 23,692 B | 13,538 B |
| First-request prefix | 60,605.5 ± 0.7 | 56,701.5 ± 0.7 |

The prefix delta is **−3,904 tokens per request**, measured with the final review-restored text at 13,324 B. The last two clauses (HMAC detail, local-LLM caveat; 214 B) were added after that measurement, so their ≈+55 tokens are estimated, not measured. The skill listing cost is included in the delta.

## 5. Tests and independent review

- **Final suite (15 files):** cost-router, benchmark, contract and memory governance tests, plus the Antigravity suite in the same process. Result: **440 passed, 4 skipped, exit 0**. `git diff --check` shows no whitespace errors.
- **R1 (Opus), CLAUDE.md: REJECT.** The rule check missed several governance sentences, and the dispatch rule had become unconditional. The main session fixed both: every listed sentence was restored inline, the dispatch rule is conditional again, and the skill path is named. The 55 contract tests pass.
- **R1 (Opus), W1 code: APPROVE WITH NOTES.** The path-traversal finding (Medium) and git return-code finding were fixed in W4, and the pricing note was completed.
- **R2 (Opus, final), CLAUDE.md delta: APPROVE WITH NOTES.** All 12 restorations are verified. Six detail items lived only in the skill; the two that matter (HMAC secret handling, unverified local-LLM answers) were added inline afterwards.
- **R2 (Opus, final), harness: APPROVE WITH NOTES.** The `--rep-offset 0` overwrite (Medium) is fixed with a refusal and a regression test.
- **Evidence integrity:** all 55 pilot rows reconcile exactly with `modelUsage`, counting main and every subagent model once. Arms are equivalent apart from the intended difference, the ABBA rotation is real, and warm-ups are excluded.
- **Compliance probes** (8 critical-rule questions): old 8/8, new 8/8, run on the 12,114 B pre-review text. The restored text was not re-probed; it is covered by the tests and by both review rounds.

## 6. A/B results

Pilot setup: equal arms. Every arm used `--setting-sources user,project`, an identical empty strict MCP, working-tree hooks overlaid, `ANTHROPIC_MODEL` set, and ABBA order with warm-up.
- `base_old`: HEAD `CLAUDE.md`, hooks off.
- `ctx_new`: new `CLAUDE.md` plus the skill, hooks off.
- `combined`: new context, hooks on.

### Haiku main session (T1, T3, T4, T5, T6 × 3 repeats; 15 pairs per comparison)

| Arm | Correct | Total tokens, mean ± sd | Tokens per request | First-request prefix | Cost |
|---|---|---|---|---|---|
| base_old | 15/15 | 546,822 ± 451,615 | 69,399 | 60,713 | $0.0142 |
| ctx_new | 15/15 | 433,199 ± 358,338 | 65,216 | 56,711 | $0.0126 |
| combined | **14/15** | 517,516 ± 333,012 | 46,109 | 57,373 | **$0.1378** |

| Comparison (paired bootstrap, pairs = task × repeat) | Total tokens | Tokens per request | Prefix | Cost |
|---|---|---|---|---|
| ctx_new − base_old, all runs | −113.6k, CI [−236k, +11k]; ratio −20.8%, CI [−2.9%, +37.3%] | −6.1% [−7.5, −4.7] | −4,002 [−4,103, −3,925] | −10.9%, CI includes 0 |
| ctx_new, restored text only (n = 11) | −121.8k [−276k, +39k] | −5.7% [−7.3, −4.0] | −3,899 | — |
| combined − base_old | −29k [−206k, +126k] | distorted by small subagent prefixes | −3,340 | **+842%** [+419, +1339] |

- **Combined arm:** the Haiku session delegated upward. Implement work went to sonnet (vault-worker) and review to opus (vault-reviewer), because the route tier sits above the session tier. One T6 run failed: the main session stopped to ask, and the worker's Bash was refused. That is a correctness regression of the hooks-on configuration.
- **Run mix:** 8 early haiku runs used the pre-review 12,114 B text; the "restored only" row isolates the final text.

### Sonnet main session (pilot, incomplete; base_old vs combined)

T3 2 pairs, T1 2 pairs, T5 1 pair. Total tokens 185,320 vs 199,773: combined is +7.8%, paired CI [−25.7%, +5.2%] includes 0. Cost +5.2%, CI includes 0. Correctness 5/5 vs 5/5. No delegations; prefix −3.2k. T5 repeat 1 was not run because of the budget cap. Rows for T3 and T1 were reconstructed from run-time logs and raw streams after a harness overwrite; the values were verified by R2.

## 7. API cost (reported separately)

- Haiku `ctx_new` cost −10.9%, CI includes 0. Haiku `combined` +842% (subagent tier raising). Sonnet `combined` +5.2%, CI includes 0.
- Phase-4 pricing fix: sonnet cache-read is $0.10/MTok. The fit over 50 records is exact when cache writes are priced at the 1h rate of $4.0. `modelUsage.costUSD` is Claude Code's client-side list-price estimate, not provider billing.

## 8. Budget

- Phase-3 artifact spend was $24.24, plus about $0.2-0.3 of unrecorded probe spend.
- The phase-4 cap was $5.00. Phase-4 spend was **$4.4365**:

| Item | Spend |
|---|---|
| W1 | $0.3368 |
| W2 | $0.1331 |
| W3 haiku | $2.4744 |
| W3 sonnet | $1.4761 |
| W4 | $0.0161 |
| **Phase 4 total** | **$4.4365** |

- Phase 3 + 4 total: about $28.9 of the $30 authorization. **Remaining: about $1.1.**
- W3 overshot its soft haiku sub-cap by $0.17, because the cap is checked before each run launches; it stayed within its own $4.40 and the phase cap.

## 9. Global installation status and side effects

- `~/.claude/settings.json` has no hooks.
- `~/.claude/skills/cost-router/` was rewritten twice, both times by the repository's own SessionStart self-update (`install.py --session-start`), from sessions the owner started at the repository root:
  - 21:03:08, session `60aef3dd…`;
  - 21:41:51, the owner's desktop session `fd1a8a49…` ("Continua cu loganalyzer dfir").
- It now equals the working tree; `install.py --verify` reports OK.
- None of this phase's commands wrote it. Every paid run executed in the bench worktree with the self-install entry stripped and `COST_ROUTER_NO_SELF_INSTALL=1`. The VERSION mtime was unchanged across all of this phase's runs.
- The owner's live session `fd1a8a49` loaded the slim `CLAUDE.md` from before the review fix. The fix was applied at 21:53:48.

## 10. Rollback

- **`CLAUDE.md`:** `git show HEAD:CLAUDE.md > CLAUDE.md` restores the original. This file alone; the skill directory can stay or be removed.
- **Skill:** delete `.claude/skills/vault-agent-contract/`.
- **Pricing:** set `04_CONFIG/claude_model_routing.json` sonnet `cache_read` back to 0.2. Not recommended: the measured fit supports 0.10.
- **Self-install guard:** inert unless `COST_ROUTER_NO_SELF_INSTALL=1` is set; removing it is a one-line revert in `install.py`.
- **Hooks:** `COST_ROUTER_OFF=1`.
- **Global copy:** to return it to HEAD, run HEAD's installer from a clean worktree, or `install.py --uninstall`. Owner decision.

## 11. Unresolved risks

1. The new core relies on the skill being loaded for detailed procedures. Live auto-loading in a multi-turn task was not observed. The core stays safe without it: hard rules are inline. Compliance on the final text was not re-probed.
2. Combined hooks on a cheap main session raise subagent tiers. That produced a large cost increase and one correctness regression on Haiku. Recommendation: do not enable enforcement on Haiku sessions, or cap upward delegation by policy (owner decision).
3. Task-level token savings need more power: about 10-30 pairs per task, beyond the remaining $1.1.
4. Reconstructed sonnet rows: the values were verified, but byte-identity of the rewritten raw files cannot be proven. The haiku overlay manifests for the 8 pre-review runs were overwritten; their CLAUDE.md is archived under `context_dedup_2026-10-21/*/overlay` (sha `9c71473dc084`).
5. The repository SessionStart self-update still rewrites the global copy whenever the owner starts a session at the repo root. Proposed opt-in (not implemented): refresh only when `~/.claude/cost-router.autoupdate` exists, created by `install.py --enable-autoupdate` and removed by `--uninstall`.
6. The Antigravity policy copy still prices sonnet cache-read at 0.2 (another agent's file).
7. Owner-scope context is not touched: user MCP connectors (about 10k tokens) and the superpowers plugin (1.3k). These are the largest remaining levers for every session.
