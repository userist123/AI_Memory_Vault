---
type: evaluation_report
category: skill_intake
id: "external-skill-intake-design-stack-2026-10-11"
document_status: active
provenance_status: complete
trust_level: REVIEWED
created: 2026-10-11
---

# External skill intake: design stack (2026-10-11)

Owner request: install 4 Claude Code plugins and 3 `npx` skill installers on Marius-PC, but only
after the sources pass a review recorded in git. Every source was shallow-cloned into an isolated
directory. Nothing in them was executed. Two checks ran on them: the repository's
`untrusted_content_guard` rules, and an independent read-only security review on Opus. The review
cites file:line for each finding.

| Item | Source @ commit | On PC before | Verdict | Action |
|---|---|---|---|---|
| frontend-design | anthropics/claude-plugins-official @ f359d43b | installed, disabled | SAFE (text-only skill, 0 guard findings) | enable; copy in `.agents/skills/frontend-design-anthropic` |
| superpowers | obra/superpowers, marketplace-pinned 5bf4e780 | installed, enabled (6.4.1) | already in use | none |
| ralph-loop | anthropics/claude-plugins-official @ f359d43b | installed, disabled | SAFE-WITH-CONDITIONS | enable; copy in `.agents/skills/ralph-loop` |
| chrome-devtools-mcp | ChromeDevTools/chrome-devtools-mcp, marketplace-pinned 1cec9cd1 | missing | SAFE-WITH-CONDITIONS | install with the flags below |
| find-skills (`npx skills`) | vercel-labs/skills @ 13e4063a | missing | REJECT for agent use | not installed |
| one-skill-to-rule-them-all (task-observer) | rebelytics/one-skill-to-rule-them-all @ 56e89071 | missing | SAFE-WITH-CONDITIONS, outside the Vault | owner decision |
| impeccable (`npx impeccable install`) | pbakaus/impeccable @ c63b8dd7 | missing | REJECT as shipped | not installed; the Vault keeps its text-only `.agents/skills/impeccable` |

## Findings that decide the verdicts

- **ralph-loop.** A global Stop hook (`hooks/stop-hook.sh`, which needs bash, jq and perl) acts on
  any `.claude/ralph-loop.local.md` in the working directory. When that file has no
  `session_id`, its body becomes the prompt the hook re-injects. A cloned repository that commits
  such a file can therefore loop the agent. `max_iterations` defaults to 0, which means unlimited.
  The hook makes no network calls.
  Conditions:
  - always pass `--max-iterations`;
  - before working in a foreign repository, check `git ls-files .claude/ralph-loop.local.md`.
- **chrome-devtools-mcp.** The server has full browser control (`evaluate_script`, `file:`
  navigation, screenshots, network inspection). Usage statistics go to Google Clearcut and trace
  URLs go to the CrUX API, both by default. It also reads `${CWD}/cd4a.config.json`, which can set
  `executablePath` and `chromeArg`; a planted file can therefore launch an arbitrary binary.
  Conditions:
  - pin the version and run with `--no-usage-statistics --no-performance-crux --isolated`;
  - set `CHROME_DEVTOOLS_MCP_NO_CONFIG_DISCOVERY=1` and `CHROME_DEVTOOLS_MCP_NO_USAGE_STATISTICS=1`;
  - keep it away from logged-in browser profiles.
- **find-skills / `npx skills`.**
  - The skill tells the agent it may run `npx skills add … -g -y`, which skips confirmation.
  - A project-scope install writes to `<cwd>/.agents/skills`. That is this Vault's human-gated
    skills directory, so the install bypasses `skill_ingestion.py promote --verified`.
  - Telemetry to `skills.sh` is on by default.
  - The `npx` package is unpinned, and it runs a `dist/` folder that is not in the repository.
- **impeccable.**
  - Its SessionStart, PostToolUse (Edit|Write) and Stop hooks run a native binary. The binary is
    downloaded on first run, and its checksum comes from the same release.
  - `/impeccable live` starts `claude --permission-mode bypassPermissions`, or Codex with sandbox
    bypass, using a prompt built from text the browser page submits. A page can therefore inject
    into an agent that runs with no permission checks.
  - `concept-seed` fetches remote instructions from `impeccable.style` and posts the user's choice
    back.
  - The guard also flags 6 "user approved/confirmed" phrases in its reference files, plus the Windows
    executable `scripts/impeccable.cmd`.
- **task-observer.**
  - It has no hooks and no network calls. Its scripts only append to `skill-observations/`.
  - Its description requires it to run before the first tool call of every session, and it suggests
    a CLAUDE.md line or a session-start hook to enforce that.
  - It keeps a second store for memory and skill improvement, parallel to the Vault, which
    `CLAUDE.md` prohibits.
  - Acceptable only on demand, at user scope on the PC, with no CLAUDE.md line and no hook.

## Not verified

- The published npm builds and the downloaded binaries were not compared with the reviewed source.
- Whether impeccable's `hook` path itself reaches the network was not checked.
- How these plugins behave inside Claude Code on Windows was not observed.

## One-time install (owner: "run once, then the AI does the rest")

`30_SCRIPTS/setup/install_design_stack.py --apply` installs exactly the cleared items, once. A
marker file (`~/.claude/design-stack.installed`) turns a second run into a no-op. Without
`--apply` the script only prints the plan.

What it installs:
- It enables `frontend-design`, `ralph-loop` and `superpowers`.
- It adds the MCP server `chrome-devtools-mcp@1.9.0`, the version the official marketplace pins.
  At that version, `cd4a.config.json` discovery does not exist yet. It runs with
  `--no-usage-statistics --no-performance-crux --isolated`.
- It copies the `creative-web-director` skill.

The skill carries the owner's directive: PRODUCT/DESIGN/PLAN files, the anti-"AI slop" visual
standard, browser verification, ralph-loop always bounded, and memory through the Vault. It
triggers on any web/UI request, so the owner never types the commands themselves.
Tests: `20_TESTS/test_install_design_stack.py`.
