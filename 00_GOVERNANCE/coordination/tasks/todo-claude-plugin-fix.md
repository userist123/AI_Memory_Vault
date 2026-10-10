# Checkpoint — claude-plugin-fix (expose a usable skill through the Claude Code plugin)

- **Task:** owner command "fix plugin" (2026-10-10). The marketplace at `.claude-plugin/` installed, but the plugin
  loaded only `.mcp.json`: `plugin.json` declared no component paths and the vault keeps skills under `.agents/skills/`.
- **Branch:** `claude/determined-noether-9gl3z8`.

## Done
- New vault-authored skill `.agents/skills/ai-memory-vault/SKILL.md` (source: self): retrieval interfaces, priority,
  invariants, `memory_propose`, controlled skill ingestion, coordination. Mirrors `CLAUDE.md`; `VAULT_STATE.md` wins.
- `plugin.json`: `skills` points at that one directory; version 1.0.1 in both manifests.
- Verified: `claude plugin validate .`, layout validator, `20_TESTS/test_claude_contract.py`,
  `20_TESTS/test_untrusted_content_guard.py`, `20_TESTS/test_no_orphaned_test_files.py`.

## Decisions
- Did not expose `.claude/hooks/owner_authority_gate.py` as a plugin hook: it is fail-closed and needs the owner broker;
  as a plugin hook it would deny every tool call on a machine without the broker. It stays opt-in via
  `.claude/owner-authority.settings.example.json`.
- Did not expose `.agents/skills/vault-security-audit`: its four test paths no longer exist (moved under `20_TESTS/`).
- Did not expose all of `.agents/skills/` (2680 skills): that would push every imported skill description into each
  session's context and treat RAW community material as operational.

## Next steps
- `.mcp.json` runs `python 03_IMPLEMENTATION/.../memory_mcp_server.py` relative to the cwd. Inside a plugin the cwd is
  the project, not the plugin root, so the MCP server only starts when the session runs in a vault checkout. A
  plugin-specific `.mcp.json` using `${CLAUDE_PLUGIN_ROOT}` would fix that; not changed here (shared with the project).
- Remediation v7 FAZA 4 (`00_GOVERNANCE/coordination/remediation_v7/todo.md`) still lists the full plugin
  conformity work; this change covers only the skill exposure.
- Fix `vault-security-audit` paths or retire the skill.
