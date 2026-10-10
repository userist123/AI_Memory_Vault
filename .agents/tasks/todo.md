# Active Task Ledger

This file is the execution ledger for non-trivial Claude tasks.

## Current task — Claude Code model router (cost-aware model/effort routing)

Objective: give Claude Code (and its subagents) an explicit, testable policy for which
Claude model and effort level to use per task class, grounded in current pricing, plus a
report on real token consumption from local Claude Code transcripts, so the owner can see
where the money goes and how much the policy saves.

Scope (affected files):
- `04_CONFIG/claude_model_routing.json` — policy: models, prices, task classes, rules
- `03_IMPLEMENTATION/packages/routing/claude_model_router.py` — classify + route + cost
- `03_IMPLEMENTATION/packages/routing/claude_usage_report.py` — JSONL usage aggregation
- `03_IMPLEMENTATION/packages/routing/claude_model_cli.py` — `route` / `report` / `policy`
- `.claude/agents/{Explore,vault-worker,vault-reviewer}.md` — subagents pinned to cheaper models
- `00_GOVERNANCE/protocols/Claude_Model_Routing_Policy_V1.md` — the human-readable policy
- `CLAUDE.md` — short pointer section
- `20_TESTS/test_claude_model_router.py`
- `00_GOVERNANCE/coordination/tasks/todo-claude-model-router.md` — checkpoint

Dependencies: none new (stdlib only). Does not touch the protected core
(`providers/model_tier_router.py` keeps its light/standard/heavy contract; this layer is
Claude-Code-side and independent).

Steps:
- [x] Research: pricing table (claude-api skill, cached 2026-10-06), Claude Code docs on
      subagent `model`/`effort` frontmatter and model aliases, cost-optimization lever order
- [x] Inspect existing routers: `providers/model_tier_router.py` (tier->provider, council),
      `routing/agent_router.py` (task->runtime). Neither routes Claude model *within* Claude Code.
- [x] Write policy JSON
- [x] Write router module + usage report + CLI
- [x] Write subagent definitions
- [x] Write protocol doc + CLAUDE.md pointer
- [x] Tests (red -> green)
- [x] Run `report` on this session's own transcript as evidence
- [x] Full `20_TESTS` run, checkpoint, commit, push

Risks: price table drifts (kept in config with a `pricing_as_of` field); keyword classifier
is heuristic (returns `confidence: low` + policy default instead of guessing silently).

Review criteria: tests green; `route` output explains model, effort, class, rationale and
estimated cost; `report` reproduces the token totals of a fixture transcript.

## Review (2026-10-10)
- New tests: 23/23. Full suite: 3604 passed; every failure traced to the environment (mcp 2.x, missing
  dependency) or to the two new plain `.md` files, both fixed (route re-measurement + allowlist).
- Measured on this session: 91% cache-hit; same tokens on Opus would be 43% of the Fable bill.
- Remaining work: owner decides whether to enable an opt-in prompt hook; prices to refresh with the rate card.
