# todo-claude-integrity-guard

- **Task (owner 2026-10-24):** shield against external interference / direction changes, malware, backdoors, third-party data egress. Maximum protection.
- **Branch:** `claude/agent-integrity-guard` — PR #346 (draft).
- **Owner choices:** global install (`~/.claude` on C: + project copy on D:); block all not approved; egress allowlist = owner's GitHub repos, Anthropic API, localhost.

## Acceptance checklist (owner; do NOT close/activate until all pass) — see memory integrity-guard-acceptance
1. **Real enforcement at runtime** — VERIFIED. hook_guard.py run through the exact settings.json command string in Git Bash and under native PowerShell on Windows: `curl evil.tld` -> ask, write to settings.json -> deny, `git status` -> silent allow.
2. **Fail-closed** — VERIFIED. hook_guard.py (commit 9e63c9745): guard import fails -> inline check still denies danger / asks otherwise; unparseable payload -> ask; never a silent allow on error. Subprocess tests with guard.py absent.
3. **Egress & integrity, adversarial** — two review rounds (vault-reviewer). Round 1 + round 2 bypasses all fixed (commits 4481f277d, c6f2a97cc), each now a test: shell writes to guard/settings incl. quote-split `set""tings.json`, MultiEdit/NotebookEdit, scp/UNC/httpie/dns egress, persistence incl. /etc/profile.d, C3 secret+egress, disableAllHooks content. 103 guard tests.
4. **Windows compatibility** — VERIFIED (item 1): Git Bash + PowerShell, install.py idempotent/preserve/remove on C:-style home.
5. **Final validation** — VERIFIED. 36/36 CI checks green on e9b75d03c (incl. Structural hygiene, Analyze python/actions/csharp/js, CodeQL "No new alerts", security-boundary, zero-regression suite). Local: 103 guard tests + scanners. All 5 criteria met; awaiting owner decision to activate.

## Not done on purpose
- **Activation** = `install.py` into `~/.claude`. Separate owner-approved step, after CI is green. Not run yet.
- **Key files:** `.claude/skills/agent-integrity-guard/*`, `20_TESTS/test_agent_integrity_guard.py`.
