---
type: coordination
category: agent-status
status: active
title: CLAUDE_OPUS — current
---

# CLAUDE_OPUS — current

## 2026-10-07T18:50Z — vault_access cold start and route-count check (PR #214, claude)

Branch `claude/vault-universal-access`. The first `vault_*` call needed the metadata of all routes (15 s measured
under load, 28 s earlier; Codex `tool_timeout_sec = 60`). Now: LibYAML loader when PyYAML has it, one frontmatter parse
per file, headings scanned once, and the MCP server warms the route table in a background thread after the handshake
(`VAULT_ACCESS_WARM=0` disables it). A note with an impossible YAML scalar (`date: 2026-13-45`) used to break the whole
table; it is now one unreadable (fail-closed) note. VAULT_STATE.md route/domain count is checked by
`20_TESTS/test_vault_state_accuracy.py` (113 domains on a clean checkout, not 114). Tests: `20_TESTS/test_vault_access_perf.py`.

## 2026-10-06T19:30Z — Universal vault access (DONE on branch, PR open)

Branch `claude/vault-universal-access`.
- One access core for every AI: `03_IMPLEMENTATION/packages/vault_access/` (routes, policy, audit, redaction).
- Direct routes `vault://<domain>/<slug>` from `04_CONFIG/vault_domains.yaml`; policy per egress channel in `04_CONFIG/access_policy.yaml`.
- Adapters: MCP `vault-memory` (+6 `vault_*` tools, `--principal`), `python -m cognitive_core.vault_cli`,
  Ollama/Telegram assistant (`python -m cognitive_core.telegram_vault_bot`), PUBLIC export for web AIs.
- Client configurations: `.mcp.json`, `.codex/config.toml`, `.agents/mcp_config.json`, `.gemini/settings.json`, `GEMINI.md`.
- Evidence: `07_EVALUATION/vault_routing/`; tests `20_TESTS/test_vault_access_*.py`; CI `.github/workflows/vault-access.yml`.

## 2026-10-06T19:00Z — PR #211 review findings (PR #213 into the PR #211 branch)

Branch `claude/pr211-security-fixes`: token↔packet binding, safe task ids, fail-closed replay guard,
validated bridge config, full A2A contract, goal-free receipts in a private directory, response identity check.

## Not done / owner decisions

- Rotate any credential ever committed (R001 inventory) — owner only.
- Decide what moves to the private overlay (`AI_MEMORY_VAULT_PRIVATE_ROOT`); nothing was moved.
- Add the Telegram user id to `telegram.allowed_user_ids`; run the bot against the live Ollama once and record the result.
- Confirm on the Windows machine that Codex and Antigravity load the project-level MCP configs.
