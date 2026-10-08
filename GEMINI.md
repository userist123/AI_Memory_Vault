# GEMINI.md — Gemini CLI / Antigravity

Follow `AGENTS.md` in full; it is the canonical operating contract for every agent in this repository.
Read `00_GOVERNANCE/VAULT_STATE.md` first.

Vault access goes through the MCP server `vault-memory` (`.gemini/settings.json` for Gemini CLI,
`.agents/mcp_config.json` for Antigravity): find the direct route with `vault_resolve`, read it
with `vault_read`, cite `vault://… sha256:… L<a>-L<b>`. Search with `memory_search` or
`vault_search`; propose durable knowledge with `memory_propose`.
