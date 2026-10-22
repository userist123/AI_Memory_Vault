# Release readiness (inputs for the owner; nothing here is a release)

DECISION: SAFE FOR CONTROLLED LOCAL TESTING

Conditions (independent round-2 review, 2026-10-22): (1) the global self-update hook stays unregistered (user hooks = 0) and HIGH-2 (unpinned/unsigned remote installer) remains an owner-accepted residual until signing is implemented; (2) repo-root sessions run with COST_ROUTER_NO_SELF_INSTALL=1 or the owner accepts the global skill refresh; (3) accepted residuals recorded with the owner: MED-B (state-file forgery of escalation_authorized; hooks are a cost-policy aid, not a boundary against an agent with arbitrary write), R2-1 LOW-MED user-level/plugin agent defaults above the session tier are not capped, R2-2 LOW unrecognised model strings pass through on non-review briefs, R2-3 LOW settings-file model still drives the same-tier advisory (never escalation), RT03/RT15/RT16 xfail residuals; (4) the loosened pre-W7 assertions in 20_TESTS/test_cost_router_enforce.py are reviewed before any merge; (5) nothing is committed, pushed or installed globally without owner authorization. TOKEN SAVINGS NOT DEMONSTRATED (unchanged).

Nothing was committed, pushed, merged or installed globally by this mission. All changes are uncommitted in the shared working tree on branch `gemini/casa3d-nextgen-functionality-uiux` (HEAD 2c72a7228 at mission start; re-verified unchanged at the gate, SOURCE_VERIFIED, see GATE_LOG.md).

## Decision inputs
- Final full-suite result and its diff against the baseline: GATE_LOG.md (G1), evidence.json.
- Isolation, whitespace, ownership, global state and strict YAML gates: GATE_LOG.md (G2-G7).
- Security: SECURITY_REDTEAM_RESULTS.md (14 guarded classes, 3 documented residuals, HIGH-2 proposal not implemented).
- Token claim: TOKEN_ACCOUNTING_VALIDATION.md (savings NOT demonstrated).
- Matrix: RELIABILITY_TEST_MATRIX.md.
- Independent review (vault-reviewer, opus): not yet done; mandatory per the repository rules before DONE for risky code.

## Conditions before any release (INFERRED from the repository rules)
1. Independent review pass of the final diff and evidence, at most 2 rounds.
2. Owner decision on HIGH-2 (self-update pinning) and on default opt-in for the global install.
3. Owner decision on the `#escalate` semantics below.
4. The pre-existing baseline failures (other agents, environment) are classified, not fixed by this mission.
5. The cost-router files carry earlier uncommitted work (phases 1-4, Antigravity): a release must take the whole set, not only this mission's hunks.

## Runtime switches (rollback without touching files)
- `COST_ROUTER_OFF=1` (also true/yes/on): every hook returns at once, no output, no state; `install.py --session-start/--self-update` is inert. TEST_VERIFIED (`test_cost_router_claude_hooks.py::test_cost_router_off_makes_every_hook_inert`, `escalation_matrix::test_f5_*`, `::test_t6_*`).
- `COST_ROUTER_NO_SELF_INSTALL=1`: self-update hook and installer self-update/session-start do nothing. TEST_VERIFIED (`test_cost_router_self_update.py`, `redteam::test_rt01_*`).
- `#escalate` (typed by the owner in a prompt, valid for that prompt epoch only): allows a mandatory review / required tier above the session tier, with a visible note. Without it: review/verify briefs or critical/live-op work below the required tier is DENIED; normal work above the session tier is capped at the session tier with a visible note (no deny, no silent raise). Unknown session model counts as the cheapest tier: set `ANTHROPIC_MODEL`.

## Per-file rollback (`git checkout -- <file>`; only for mission files; NEVER for other agents' files)
WARNING: these files also contain earlier uncommitted work (phases 1-4); checkout restores HEAD and drops all of it. Copy the file first.
Tracked, modified by this mission (mtime after 2026-10-21 22:50):
- `.claude/skills/cost-router/hook_agent_model.py`
- `.claude/skills/cost-router/hook_prompt_route.py`
- `.claude/skills/cost-router/hook_resume_guard.py`
- `.claude/skills/cost-router/hook_self_update.py`
- `.claude/skills/cost-router/install.py`
- `.claude/skills/cost-router/lib/route_state.py`
- `.claude/skills/cost-router/SKILL.md`
- `03_IMPLEMENTATION/packages/interfaces/memory_access.py`
- `03_IMPLEMENTATION/packages/interfaces/vault_runtime.py`
- `03_IMPLEMENTATION/packages/memory/controller.py`
- `03_IMPLEMENTATION/packages/memory/storage/file_engine.py`
- `03_IMPLEMENTATION/packages/retrieval/hybrid_retrieval.py`
- `20_TESTS/test_cost_router_enforcement.py`
- `20_TESTS/test_cost_router_self_update.py`
- `20_TESTS/test_cost_router_skill.py`
- `20_TESTS/test_cost_router_token_waste.py`
Untracked, created by this mission (rollback = delete the file; there is no HEAD version):
`20_TESTS/redteam/`, `20_TESTS/regression/test_retrieval_correctness_overnight.py`, `20_TESTS/test_cost_router_escalation_matrix.py`, `20_TESTS/fixtures/telemetry/`, `30_SCRIPTS/evaluation/token_accounting_check.py`, `07_EVALUATION/reports/overnight_reliability_2026-10-21/`.
Untracked/modified by the telemetry worker, also files from earlier phases (files exist untracked, so no HEAD version): `.claude/skills/cost-router/hook_agent_usage.py`, `30_SCRIPTS/evaluation/claude_cost_router_benchmark.py`, `20_TESTS/test_claude_cost_router_benchmark.py`, `20_TESTS/test_cost_router_claude_hooks.py`.
Stray file: `scratch_crlf.py` (0 bytes, repository root, created 23:51 during the mission, owner not identified, INFERRED worker scratch). Not removed by the gate; owner may delete.
Checkpoint files (todo-claude-*) get frontmatter only if G7 required it (GATE_LOG.md).

## Global state touched by this mission
None intended. Observed global changes during the mission window are attributed in GATE_LOG.md G5 (frontend-design plugin installed by the separate frontend-design session; cost-router global copy rewritten at 23:32:44 by the SessionStart of session 9eb044d1 (RUNTIME_VERIFIED, 20:32:44Z), not by a mission command).
