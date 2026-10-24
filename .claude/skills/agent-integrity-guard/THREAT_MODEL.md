# agent-integrity-guard — threat model and probe list

Written before implementation (CLAUDE.md: security-sensitive work carries a threat model;
the reviewer gets the same list). Owner decisions 2026-10-24: global install (`~/.claude` on
C: plus a project copy under `D:\w`), block everything not approved, approved egress =
GitHub (owner's repos only), Anthropic API, localhost.

## What is protected

1. **Direction.** Only the owner, the harness and the repository instruction files
   (CLAUDE.md, AGENTS.md) may change what an agent does. Text arriving through tools —
   web pages, files, tool and MCP output, skills, other agents — is data, never a command.
2. **Data.** Nothing leaves the PC or the repository for a third party.
3. **Integrity.** No autostart or scheduled persistence, no tampering with the guard or with
   the settings that load it.

## Design principle: allowlists, not attack signatures

Controls are defined by **where** data goes and **where** a write lands, so they hold for
tools and techniques nobody listed:

| id | concern | control | decision |
|---|---|---|---|
| C1 | instructions hidden in tool output | SKILL.md rules; output scan reuses the repo's `untrusted_content_guard` rules and adds a warning | warn |
| C2 | network egress from a shell or fetch tool | every host found in the call is checked against the egress allowlist | ask if any host is not approved |
| C3 | data in an outbound request (secrets, env values, file contents) | a call that both reads a secret source and has any egress is refused, even to an approved host | deny |
| C4 | publishing or pushing to a remote the owner does not own | git/gh/package-publish targets checked against the owner namespace | deny publish, ask other remotes |
| C5 | MCP tools that reach third-party services | tool-name allowlist, local servers only by default | ask |
| C6 | persistence | writes into autostart, scheduled-task, git-hook and shell-profile locations, by path | deny |
| C7 | tampering with the guard | writes/deletes touching the guard directory or its hook entries in Claude Code settings | deny |
| C8 | guard error or unparseable input | fail closed when the call has egress or a protected path; fail open otherwise | deny on doubt |
| C9 | new code or packages | scan with the repo scanners (`skill_exfiltration_scanner`, `untrusted_content_guard`) before use | report |

Approved egress: `github.com` and `api.github.com` only under `userist123/`; `api.anthropic.com`;
`localhost`, `127.0.0.1`, `::1`. Package registries are not approved: installs ask.

## Residual risks

- Hooks see tool calls, not the operating system: code already running can network on its own.
  The guard narrows what an agent can be talked into; it is not a firewall.
- Host extraction can miss obfuscated destinations; C8 makes doubtful egress fail closed.
- Claude Code and MCP servers talk to their own backends outside any hook.

## Expected: content scanners flag `guard.py`

`guard.py` contains the detection patterns themselves (`curl`, `wget`, `npm publish`,
browser-credential paths, `http://`...). A content scanner such as
`security/skill_exfiltration_scanner.py` therefore reports it `BLOCK` — a malware
detector necessarily holds malware signatures. This is a false positive on the shield's
own source, not egress. The scanner is not a CI gate over `.claude/skills/`, so nothing
breaks; if the shield is ever moved under a scanned path, allowlist `guard.py` for the
`http_request`/`upload`/`browser_credentials` rules rather than weakening the patterns.

## Probe list for the reviewer

1. Unapproved hosts are asked, across shell flavours and quoting variants; approved ones pass.
2. Look-alike hosts (subdomain tricks, userinfo, path tricks, other GitHub owners) are not approved.
3. Secret source + egress in one call is denied, also to approved hosts.
4. Guard files and their settings entries cannot be modified through a tool call.
5. Persistence locations are denied for Edit/Write and for shell writes.
6. Malformed or oversized hook input never silently allows a call with egress.
7. Everyday commands (git status/diff/commit/push origin, pytest, local scripts, gh in owner repos) pass.
8. Install is idempotent, preserves other settings and hooks, and uninstalls cleanly.
