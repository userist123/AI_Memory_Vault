# agent-integrity-guard

A standing integrity shield for Claude Code sessions (owner request, 2026-10-24).
It protects two things:

- **direction** — only the owner, the harness and the repo's instruction files
  (`CLAUDE.md`, `AGENTS.md`) can change what an agent does; everything arriving
  through a tool is data, not orders;
- **data** — nothing leaves the PC or the repository for a third party, except
  the approved destinations in `allowlist.json`.

It is built from behaviour rules plus two enforcing hooks, so a block is real,
not just advice.

## Files

| file | role |
|---|---|
| `SKILL.md` | the behaviour rules an agent must follow |
| `THREAT_MODEL.md` | what is protected, the controls C1..C9, residual risks, the reviewer probe list |
| `guard.py` | pure decision core (`decide(tool, input) -> allow/ask/deny`), no I/O |
| `hook_guard.py` | PreToolUse hook: turns a decision into Claude Code's permission JSON |
| `hook_scan_output.py` | PostToolUse hook: warns when tool output tries to redirect the agent |
| `allowlist.json` | approved egress and local MCP prefixes; edit by hand (the guard denies tool writes to it) |
| `install.py` | merge/remove/verify the two hooks in a settings.json |

## Install

Global (every project and session), into `~/.claude/settings.json`:

```bash
python .claude/skills/agent-integrity-guard/install.py
```

Project-only, into a repo's `.claude/settings.json`:

```bash
python .claude/skills/agent-integrity-guard/install.py --home /path/to/repo
```

`--remove` reverses it; `--verify` reports whether both hooks are present. The
installer is idempotent and keeps every other setting and hook. Hooks load on the
next session (open `/hooks` once or restart if a running session does not pick
them up).

## What it allows, asks, denies

- **allow**: ordinary work — git status/diff/commit/push to the owner's remote,
  pytest, local scripts, `gh` in the owner's repos, calls to the Anthropic API or
  localhost.
- **ask**: egress to a host not on the allowlist, a git remote/`gh` to another
  owner, an MCP tool not known to be local, a fetch whose URL carries a large
  opaque payload. The owner confirms.
- **deny**: a secret source read together with egress, publishing to a registry,
  persistence (scheduled tasks, Run keys, startup folder, git hooks, services,
  shell profiles), and any write that would modify the guard or disable its hooks.

## Limits

The hooks see tool calls, not the operating system, so code already running can
network on its own; this narrows what an agent can be *talked into*, it is not a
firewall. Host extraction can miss obfuscated destinations, so a doubtful call
that carries egress or touches a protected path fails closed. Tune `allowlist.json`
when a legitimate destination keeps asking.

## Tests

`20_TESTS/test_agent_integrity_guard.py` — every control has a block/ask case and
an allow case, plus bypass attempts (look-alike hosts, userinfo trick, PowerShell
forms) and the installer's idempotence/removal. 59 tests.
