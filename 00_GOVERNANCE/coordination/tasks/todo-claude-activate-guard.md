# todo-claude-activate-guard

- **Task (owner decision 2026-10-24):** activate agent-integrity-guard in the vault only (D:), not globally.
- **Branch:** `claude/activate-guard-vault`
- **Done:** `install.py` gained a `--project` mode that writes `${CLAUDE_PROJECT_DIR:-.}`-relative hook commands (so the committed `.claude/settings.json` works in every checkout, not a worktree-absolute path). Ran it against the vault: the two guard hooks (PreToolUse, PostToolUse) are appended to `.claude/settings.json`, keeping all cost-router hooks. End-to-end through the exact settings command string on Windows: unapproved egress -> ask, write to settings.json -> deny, pytest -> allow.
- **Compat:** cost-router tests that read the real settings.json still pass (guard appended, so their PreToolUse[0]/PostToolUse[0] Agent|Task entries are unchanged; guard commands do not match the cost-router regex). New test: installer --project mode writes relative commands.
- **Scope:** vault sessions only. Global (~/.claude) activation was NOT chosen; leave it for the owner.
- **Key files:** `.claude/settings.json`, `.claude/skills/agent-integrity-guard/install.py`, `20_TESTS/test_agent_integrity_guard.py`.
