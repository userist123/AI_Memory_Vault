# todo-claude-reading-tool
STATUS: IN_PROGRESS        UPDATED: 2026-11-13T22:30:00+02:00
TASK: add the reading tool (MCP server `fetch`, URL -> Markdown) to the Vault, verified, then merge
BRANCH / PR: claude/reading-tool-fetch / (to open)    BASE: origin/main HEAD at branch time
SPEC: 10_DOCUMENTATION/procedures/Reading_Web_Sources_With_Fetch.md
DONE:
- .mcp.json: `fetch` runs isolated via `python -m uv tool run mcp-server-fetch==2026.11.11`
  (mcp-server-fetch needs mcp 2.x; the repo pins mcp==1.30.0 for vault-memory; a pip install
  into the same interpreter broke vault-memory locally: `mcp.server.fastmcp` missing)
- requirements-reading-tools.txt (uv==0.13.8, local only, not CI)
- skill .claude/skills/surse-web/SKILL.md; procedure (REVIEW/unverified)
- 20_TESTS/test_reading_tool_fetch.py: config, pin, guard still ASKs on mcp__fetch__*, live stdio handshake (skipped without uv)
NEXT (in order):
1. vault-reviewer pass on the diff; fix findings
2. push, open PR, check CI once, merge (owner asked to merge)
3. mark this checkpoint DONE
BLOCKERS / OWNER QUESTIONS:
- auto-allowing `mcp__fetch__` in agent-integrity-guard allowlist.json is an owner decision (hand edit); left as ASK
KEY FILES:
- .mcp.json, requirements-reading-tools.txt, .claude/skills/surse-web/SKILL.md,
  10_DOCUMENTATION/procedures/Reading_Web_Sources_With_Fetch.md, 20_TESTS/test_reading_tool_fetch.py
VERIFICATION SO FAR: test_reading_tool_fetch 6 passed (handshake live); test_vault_access_clients +
test_agent_docs_memory_interfaces 63 passed; test_agent_integrity_guard passed; route registry --check OK
