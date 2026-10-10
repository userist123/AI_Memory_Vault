# todo-claude-model-router
STATUS: IN_PROGRESS        UPDATED: 2026-10-10T11:30:00Z (skill added; awaiting owner review / PR)
TASK: cost-aware Claude model/effort router for Claude Code (policy + CLI + subagents + usage report)
BRANCH / PR: ccr-d35fe5b8-vdxnq4 / none    BASE: 21da5bbf
SPEC: 00_GOVERNANCE/protocols/Claude_Model_Routing_Policy_V1.md, 04_CONFIG/claude_model_routing.json
DONE:
- research: rate card (claude-api skill 2026-10-06), Claude Code subagent `model`/`effort` frontmatter,
  cost-optimization lever order (cache > input hygiene > effort > model, priced per completed task)
- policy JSON, routing/claude_model_router.py, claude_usage_report.py, claude_model_cli.py
- .claude/agents/{Explore,vault-worker,vault-reviewer}.md pinned to haiku/sonnet/opus
- 20_TESTS/test_claude_model_router.py: 23 passed
- CLAUDE.md pointer section; protocol doc
- `cost-router` skill (.claude/skills/cost-router: SKILL.md, lib/route.py, hook_prompt_route.py, install.py),
  project hook in .claude/settings.json, 20_TESTS/test_cost_router_skill.py (12 passed)
NEXT (in order):
1. owner review; open PR from ccr-d35fe5b8-vdxnq4 if accepted
2. owner runs `python3 .claude/skills/cost-router/install.py` on each machine (user scope)
BLOCKERS / OWNER QUESTIONS:
- none
KEY FILES:
- 03_IMPLEMENTATION/packages/routing/claude_model_router.py, claude_usage_report.py, claude_model_cli.py
- 04_CONFIG/claude_model_routing.json; .claude/agents/*.md; 20_TESTS/test_claude_model_router.py
VERIFICATION SO FAR: full 20_TESTS (research/ excluded): 3604 passed, 14 failed -> all 14 traced:
  12 MCP stdio tests = environment had mcp 2.2.0 (repo pins 1.30.0), pass after pinning (44/44);
  2 reproducibility-gate tests = missing env dependency, fail on base too; route-count + strict-yaml
  tests fixed by re-measuring routes (4433/117, by URI 4433/4433, 0 wrong) and allowlisting the two
  plain docs. 23/23 new tests green; `report` on this session (11 Fable requests):
  1.81M cache-read, 171K cache-write, 34K output, 91% cache hit, $5.58; same tokens on Opus $2.41
