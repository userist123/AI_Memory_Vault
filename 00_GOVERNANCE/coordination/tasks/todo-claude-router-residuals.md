---
id: "b6f1c2a4-6d7e-5f80-9a1b-2c3d4e5f6a70"
title: "Checkpoint: Claude Code - router residuals R2-1/R2-2 and test assertions"
type: "project"
lifecycle: "REVIEW"
created: "2026-10-22T00:00:00Z"
updated: "2026-10-22T00:00:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "00_GOVERNANCE/coordination/tasks/todo-claude-router-residuals.md"
  created_by: "claude-code"
tags:
  - "checkpoint"
  - "cost-router"
---

# todo-claude-router-residuals

Predecessor: `todo-claude-overnight-reliability.md` (verdict SAFE FOR CONTROLLED LOCAL TESTING; evidence in
`07_EVALUATION/reports/overnight_reliability_2026-10-21/`). Branch gemini/casa3d-nextgen-functionality-uiux (shared,
dirty with other agents' work). No commit/push/global changes; never run `claude -p`; tests with temp HOME/APPDATA +
`PYTHONPATH=$(python -c "import site;print(site.getusersitepackages())")` + `COST_ROUTER_NO_SELF_INSTALL=1`.

## Tasks
1. R2-1 (LOW-MED): `.claude/skills/cost-router/hook_agent_model.py` `_agent_default_model` reads only project
   `.claude/agents`; also read `~/.claude/agents/<type>.md` (user scope) and treat plugin agent types (`plugin:name`)
   whose default model cannot be resolved as UNKNOWN → cap to the session tier with a note (never pass an unknown
   default above the session silently). Tests with tmp HOME agent file `model: opus` from a haiku session.
2. R2-2 (LOW): unrecognised explicit model strings (`opus[1m]`, `claude-opus-4-1`, any non-policy id) on non-review
   briefs: normalise via the policy's alias/longest-prefix matcher (`lib/transcript_usage.py` / router) and apply the
   session cap; if still unrecognised → cap to the session tier with note. Tests.
3. Pre-W7 loosened assertions in `20_TESTS/test_cost_router_enforce.py` ("class=explore" → "explore", changed
   additionalContext checks): restore exact checks against the parsed JSON / current compact format
   (e.g. assert the exact compact line or parsed fields), without changing hook behaviour.
4. Run: escalation matrix + all `test_cost_router_*.py` + redteam + `test_hook_antigravity.py` both orders; `git diff --check`.
5. One delta-only vault-reviewer (opus) pass; then update `evidence.json` R2-1/R2-2 status and this checkpoint.

## Done
- [ ] 1 - [ ] 2 - [ ] 3 - [ ] 4 - [ ] 5

## Owner decisions still open (do not act)
- HIGH-2: sign/pin the remote self-update (proposal in SECURITY_REDTEAM_RESULTS.md).
- Self-update opt-in default; MED-B accepted residual; commit of mission files on a dedicated branch.
- 2026-10-22 implementer subagent (claude-opus-5-5): tasks 1-4 DONE (UNREVIEWED, task 5 pending). hook_agent_model.py: _resolve_model (alias/id/alias[..]/longest id prefix), unknown explicit model capped (unclassified -> session tier, classified -> route model, risky/review -> floor), _agent_default_model reads project then ~/.claude/agents, UNKNOWN_DEFAULT (plugin a:b, path-like, non-builtin w/o file, unreadable, unrecognised model:) -> session tier; same-tier spelling kept. Tests: 71 new r2_ cases in test_cost_router_escalation_matrix.py (54 fail on the pre-fix hook copy); enforce.py exact assertions restored; test_cost_router_claude_hooks.py banana-9 test updated to the R2-2 contract. Suites (matrix+cost_router_*+benchmark+redteam+antigravity) 642 passed/3 xfailed in both orders; git diff --check clean.
- 2026-10-22 tasks 1-5 DONE: review round 1 APPROVE WITH NOTES; F1-F3 (frontmatter dash-in-value, unclosed -> fail closed, BOM, #escalate vs unrankable agent model) fixed by worker (claude-sonnet-5-5) in hook_agent_model.py with 5 test_r2b_ tests in the matrix file.
