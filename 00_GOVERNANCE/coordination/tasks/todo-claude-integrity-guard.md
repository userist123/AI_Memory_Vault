# todo-claude-integrity-guard

- **Task (owner request 2026-10-24):** a skill that protects against external interference / direction changes, malware, backdoors, and any data sent outside the repo/PC to a third party. Maximum protection.
- **Branch:** `claude/agent-integrity-guard`
- **Owner choices:** global install (`~/.claude` on C: + a project copy under D:); block everything not approved; approved egress = owner's GitHub repos, Anthropic API, localhost.
- **Done:** `.claude/skills/agent-integrity-guard/` — THREAT_MODEL.md (controls C1..C9), guard.py (pure decision core), hook_guard.py (PreToolUse deny/ask), hook_scan_output.py (PostToolUse injection warning), allowlist.json, install.py (merge/remove/verify, idempotent), SKILL.md, README.md. 59 tests in `20_TESTS/test_agent_integrity_guard.py` (each control blocks + allows normal work; bypass attempts; installer).
- **Design:** allowlist by destination and write-location, not attack signatures, so it holds for unknown tools. Fail closed on a doubtful call with egress or a protected path.
- **Not installed yet:** this PR only adds the files. Installing into ~/.claude is a separate, owner-approved step (`install.py`), because it changes how every session behaves.
- **Residual risk (stated):** hooks see tool calls, not the OS; not a firewall. Documented in THREAT_MODEL.md and README.md.
- **Key files:** `.claude/skills/agent-integrity-guard/*`, `20_TESTS/test_agent_integrity_guard.py`.
