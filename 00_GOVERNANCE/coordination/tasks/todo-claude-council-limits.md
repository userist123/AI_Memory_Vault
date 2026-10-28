# todo-claude-council-limits

- **Task (owner decision 2026-10-24):** enforce the council limits in code: at most 3 agents per route (primary + support + verifier) and at most 4 selected skills. Token caps stay documented.
- **Branch:** `claude/enforce-council-limits`
- **Done:** `routing/agent_router.py` `_council_support()` replaces `support=candidates[1:3]`: support slots = min(`max_support_agents`, `max_council_agents` - 1 - verifier); the primary is never repeated as support; support agents share the skill budget the primary leaves. Caps are logged in `rejected` (`support:capped:...`, `support:<agent>:skills_capped:...`). `04_CONFIG/agent_router.json` defaults gain `max_council_agents: 3`, `max_total_selected_skills: 4`. New `20_TESTS/test_agent_router_council_limits.py` (15 tests; 11 fail on the old router). Budget note and VAULT_STATE updated.
- **Observed, not changed:** support can be the same agent as the primary on another runtime (e.g. code_engineer on codex + on claude_subagent_worker) — behaviour predates this change; owner call whether support must be a different specialist.
- **Environment:** drive C: was full (0 bytes) on 2026-10-24; tests that write to `%TEMP%` error out. Run tests with `TMP`/`TEMP` on D:.
- **Key files:** `03_IMPLEMENTATION/packages/routing/agent_router.py`, `04_CONFIG/agent_router.json`, `20_TESTS/test_agent_router_council_limits.py`.
