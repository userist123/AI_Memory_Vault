# Checkpoint: cost-router complete (global, specialist registry, Agent Router on demand)

**Branch:** `claude/cost-router-complete`, stacked on PR #404 (`claude/cost-router-opus-advisor-fable52`, 99bcaa8ab).
**Owner asks (2026-11-22):** every task, new or resumed, in every project goes through cost-router; Haiku/Sonnet execute, Opus advises and reviews, Opus or Fable execute only after a failed check; a specialist registry by domain with metadata so the AI decides fast; the vault Agent Router when Opus asks; Antigravity's work merged.
**Design source:** fable-consultant synthesis of Codex + Gemini research (bridge task 732987cf), adopted by Opus with one decision: Fable stays consultant-only in this release; `fable-executor` is M11, default off.
Paths below: `S` = `.claude/skills/cost-router`.

## Milestones (one Sonnet worker each; Opus vault-reviewer after M4 and M9)
- [x] M1 `install.py --verify` works from the installed copy (today `REPO = SKILL_SRC.parents[2]` resolves to HOME, `S/install.py:74-77,361-362`); add `--source-root`; also verify hook registration + `onFailure`. Test: temp-HOME install, installed `--verify` exit 0, tamper -> 1.
- [x] M2 Pinned vault roots (`config.json` written at install) replace the layout check `route_state.in_vault` (`S/lib/route_state.py:447-451`) before `hook_agent_model` runs `routing.route_cli` with the project as cwd (`S/hook_agent_model.py:70-111`). Test: fake project whose `route_cli.py` writes a sentinel -> no sentinel.
- [x] M3 Shadow check by release-pin `seq`, not byte equality (`S/hook_agent_model.py:243-266,751-758`): equal seq + other bytes defers; higher project seq exits 2.
- [x] M4 `COST_ROUTER_OFF` must not skip the Fable guard (`:716-721`); privileged agent names (`fable-consultant`, reviewers) resolved from a project `.claude/agents` must match a pinned sha256 (`:341-392`). `#escalate`/`#direct` count only on line 1 of the owner prompt (`S/hook_prompt_route.py:104-123`).
- [ ] M5 Metadata: sidecar `route.meta.json` per skill + `S/roles/<name>.json` per agent (never frontmatter: the consultant validator needs an exact key set). Compiler `S/lib/registry_compile.py` (stdlib, install time, never in a hook) -> `registry.json` with sha256 per artifact; unknown key / duplicate id fail; skip `*.bak-*`.
- [ ] M6 Specialist selection in the route line (`spec=<id>`); text picks only an id from the closed set and can only raise risk/tier; golden set ~60 RO/EN prompts >= 90%; 20 injection prompts change nothing.
- [ ] M7 `S/lib/vault_router.py`: on-demand Agent Router for Opus from any project; receipts recognised by `hook_enforce_route`.
- [ ] M8 `S/lib/check.py` acceptance receipts + `check-runner` agent (haiku, no write).
- [ ] M9 Global install on the PC: backup `~/.claude/settings.json`, pinned user-scope hooks, `--verify` 0, smoke in 2 non-vault projects + 1 vault worktree + 1 resumed session; `--uninstall` restores.
- [ ] M10 Roles: `dfir-analyst` (local_only), `test-author`, `repro-debugger` template; registry entries for analist-juridic, aplicatie-securizata, surse-web, agent-bridge, animatie-3d, clips-kitty, prompt-director (overlay).
- [ ] M11 `fable-executor`, default off, own threat model + review.
- [ ] Antigravity branches: Explore inventory with diffstat; port only what passes `20_TESTS/test_cost_router_*.py`.

## Not to build
LLM calls or embeddings in hooks; metadata in frontmatter; an automatic Agent Router gate outside pinned vault roots; human-confirmation prompts; self-update following `main`.

## Recorded conflict
Owner memory says "worker = general-purpose + model fable"; the hook rewrites any non-consultant Fable spawn to Opus (`S/hook_agent_model.py:663-664`). Kept until M11.

## M1-M4 done (2026-11-22, branch claude/cost-router-complete; tests 20_TESTS/test_cost_router_global_boundary.py, 16 tests)
- Pins: `~/.claude/skills/cost-router/config.json` (vault_roots, written by install.py; `--pin-root` for worktrees) and `agents.pinned.json` (source sha256 of the agents). Only the installed copy is ever read for trust data.
- Residuals: (a) an opus agent whose project definition differs from the pinned one is NOT swapped for the installed one (Claude Code resolves project before user scope; the hook can only flag it and keep the Opus floor); (b) an attacker project with a higher release seq makes spawns in THAT project exit 2 (stale-install signal, by design); (c) an install made before M4 has no agents.pinned.json, so P4 is inactive until `install.py` is re-run; (d) `release_pin.json` was not advanced (owner action): it still lists the pre-M1 file hashes.
- `.agents/skills/cost-router/hook_antigravity.py`: `#direct` first-line rule ported; its route_state/in_vault only denies (runs nothing), not changed.

## Review round 1 fixes (2026-11-22, same branch; tests in 20_TESTS/test_cost_router_global_boundary.py, now 28)
- P3: OFF no longer skips the guard for CLAUDE_CODE_SUBAGENT_MODEL=fable, `inherit` (input or frontmatter) in a Fable session (759cd0cd3).
- P4: tolerant scan of every project `.claude/agents/**/*.md` for a privileged name/stem (comment, BOM, zero-width, NFKC, casefold); missing `agents.pinned.json` blocks the consultant, notes + Opus floor for reviewers; explicit model=opus no longer skips the check; install backs up an edited consultant/reviewer to `*.bak-<ts>` and installs the pinned one (561d6c323). Fixtures that spawn the consultant now pin the agent hashes in their temp HOME.
- P1: stored roots are compared as stored strings (never re-resolved); a pinned path that is now a link/junction is refused; registered hooks run `python -I` (0939121ba).
- P6: `--verify` checks config.json + agents.pinned.json against `pins.sha256.json`, privileged agent files, pins vs source; `--require-hooks` needs the exact installed command (e4d9141ef). An install made before this needs a re-run of install.py (no pins.sha256.json -> verify fails by design).
- P5 residual: `#direct` is a whole marker (0af0f842d, also hook_antigravity). SKILL.md updated (9a8b9826b).
- Release pin: seq 2 pins 9a8b9826b, committed separately (55f97436c). TODO owner: any later change to S needs a new pin; push both.
- Not done / residuals: `.agents` mirror of route_state not synced (its in_vault only denies); an agent with NO model key in a Fable session is not treated as Fable-sensitive under OFF; a missing pin file now adds a note on every vault-reviewer spawn on a machine without an install.

## Review round 2 fixes (2026-11-22, same branch; 20_TESTS/test_cost_router_global_boundary.py, now 33 tests)
- P3: a spawn with no model in tool_input and none in the agent frontmatter is Fable-sensitive in a Fable session, also under OFF (4d37d7aad). `CLAUDE_CODE_SUBAGENT_MODEL` resolving to Fable now DENIES (exit 2) every spawn except the pinned fable-consultant, instead of rewriting (affc60e79).
- P4: project agent files need exactly one plain `name:` line or they block Fable-sensitive spawns (file named); agent definitions are hashed with LF-normalized line endings (hook and install.py); only files named/stemmed like the spawned agent (plus fable-consultant on Fable-sensitive spawns) are compared with the pins (df8a5fc86).
- SKILL.md states the residual: pins.sha256.json and config.json are unauthenticated; --verify does not stop a same-user writer of ~/.claude (09d632ca6).
- Release pin seq 3 pins 09d632ca6, committed separately (9fce0a8b0). Any later change to S needs a new pin.
- Tests: the 6 requested paths, 775 passed. Residual: a Fable-session spawn of an agent without a model key is capped by routing (sonnet/opus), never left on Fable.
