# todo-claude-support-distinct

- **Task (owner decision 2026-10-24):** support agents must be a different specialist from the primary (not the same agent on another runtime).
- **Branch:** `claude/support-distinct-specialist`
- **Done:** `routing/agent_router.py` `_council_support()` builds the support pool from candidates whose agent is neither the primary's nor already chosen. Example: coding goal support was code_engineer (dup) + security_reviewer, now security_reviewer + architecture_reviewer. Tests: `test_support_agents_are_distinct_specialists` (8 cases); the skill-budget test now sets its own budget below the uncapped total (the distinct-specialist route no longer exceeds 4 with the real config). 4 tests fail on the old router; all routing tests pass.
- **Key files:** `03_IMPLEMENTATION/packages/routing/agent_router.py`, `20_TESTS/test_agent_router_council_limits.py`.
