---
id: "a1189bdf-cee5-43e8-a11e-31cb44f97ea5"
type: protocol
category: security
lifecycle: REVIEW
verification: unverified
created: 2026-10-07
updated: 2026-10-07
provenance:
  source_type: ai
  source_ref: "codex/owner-authority-guardrail (PR #207)"
confidence: medium
tags: [protocol, owner-authority, security, approval-gate]
---

# Owner Authority Protocol

## Purpose

Memory Vault treats the creator/repository owner as the final authority for mutations to
the repository and for privileged machine operations performed by an AI agent.

An LLM is an untrusted operator, including an uncensored or locally hosted model.
The model must never be the source of authority.

## Trusted chain

LLM -> tool boundary -> owner-authority gate -> owner-controlled approval -> operation

If the owner gate is unavailable, returns anything other than an explicit approval, or the
hook input is malformed, the operation is denied. What the hook itself can and cannot
verify is listed in "What is enforced, and what is not" below.

## Repository requirements

All changes to main must pass the protected GitHub Actions environment named owner-approval.
That environment must list @userist123 as a required reviewer and must allow the owner to
approve the job even when the owner initiated the workflow. The environment must not permit
an administrator bypass. CODEOWNERS identifies the owner for protected files.

The repository setup script `30_SCRIPTS/security/owner_authority/configure-owner-authority.sh` configures the owner
approval environment and protected main branch. These settings are intentionally outside
normal repository content: committed files cannot safely enforce their own GitHub security
settings.

GitHub supports required environment reviewers and can prevent bypass of environment
protection; required checks can then be made mandatory for a protected branch.

## Agent tools

.claude/settings.json installs a PreToolUse fail-closed hook. Read-only discovery tools
are allowed, including the read-only tools of the `vault-memory` MCP server (`memory_search`,
`memory_get`, `vault_resolve`, `vault_list`, `vault_read`, `vault_search`, `vault_get_metadata`,
`vault_check_quotes`). Every other tool call — including `memory_propose`, which writes a
candidate note — requires an external owner gate.

The external gate must authenticate the human owner independently of the LLM and must not
expose its secret/token to the agent process. The hook cannot check this; see
"What is enforced, and what is not".

### Effect of merging this with no broker configured

With `MEMORY_VAULT_OWNER_GATE_COMMAND` unset, which is the default because **no broker ships
in this repository**, the hook denies `Bash`, `Edit`, `Write`, `NotebookEdit`, `TodoWrite`,
`Task`/`Agent`, `ToolSearch`, `Skill`, `BashOutput`, `memory_propose` and every MCP tool outside
the vault-memory read-only list (for example `mcp__github__*`). Merging this change blocks
almost all agent work in the repository (even `git status` or `pytest`) in every Claude Code
session that trusts the project, until an owner approval broker exists and is configured.
Which tools are gated is an owner policy decision; this protocol records it and does not
widen it.

### Owner gate command contract (`MEMORY_VAULT_OWNER_GATE_COMMAND`)

- The hook runs the command through the shell, once per gated tool call, and writes one JSON
  object to its stdin: `{"tool_name", "tool_input", "session_id", "cwd"}`.
- The command must exit 0 and print a JSON object whose `approved` is exactly `true`. Any
  other result is a denial: non-zero exit, `approved` false / "true" / 1, non-object or
  invalid JSON, empty output, a crash, or no answer within 6 seconds.
- The hook keeps no state between calls. Every approval is a fresh decision for one call.

### Fail-closed behaviour of the hook

Claude Code blocks a tool only on exit code 2 or a JSON `permissionDecision: deny`. Any other
non-zero exit (1, 127, 9009, a timeout) is a non-blocking error and the tool runs. The hook
is built around that:

- Inside the script, malformed JSON, empty stdin, non-object JSON, a missing or non-string
  `tool_name`, and any unexpected exception all produce a JSON deny (exit 0). If even the
  deny cannot be written, the script exits 2.
- The command in `.claude/settings.json` maps a crash, a missing script and a missing
  interpreter to exit 2. It tries `python3`, then `python`, and uses only an interpreter that
  actually starts (`-c "import sys"`), so the Windows Store `python3` stub (exit 9009) is
  skipped. The script path is `${CLAUDE_PROJECT_DIR:-.}/.claude/hooks/owner_authority_gate.py`.
- Portability: the command is POSIX shell syntax. Claude Code on native Windows runs hook
  commands with Git Bash, so this works there if Git for Windows is installed. Under a
  PowerShell-only setup `${...}`, `||` and `for` do not parse, and a command that fails to
  parse is not exit 2 either. On the owner's Windows machine, check that a gated tool such as
  `Write` is really refused before relying on the hook.
- Outside the hook's control: a hook that exceeds Claude Code's 10 s timeout (the broker is
  capped at 6 s to stay below it), and a user who disables hooks or has not trusted the
  project's `.claude/settings.json`. Only the OS-level boundary below covers those.

## What is enforced, and what is not

Enforced by the hook: deny by default for every tool outside the read-only list; approval
only through the configured broker command and only for the one call; denial on every
malformed input and every internal failure.

**Not enforced by this repository** (earlier drafts of this protocol implied otherwise):

- Approval expiry. The hook has no token, nonce, timestamp or signature that could expire.
  The broker decides per call; a broker that wants time-limited approvals must implement them.
- Owner authentication. The hook cannot tell who is behind the broker: it trusts any command
  that prints `{"approved": true}`. Authenticating the human independently of the LLM, and
  keeping the secret away from the agent process, is the broker's job. No broker is provided.
- Caller identity. `session_id` is passed to the broker but is not checked.
- Protection of the gate itself. An agent that can set `MEMORY_VAULT_OWNER_GATE_COMMAND` in
  its own environment, or edit `.claude/settings.json` or the hook script, can defeat the gate.
  The variable must be set by the owner outside the agent's reach, and the files are only
  protected by the OS boundary below.
- GitHub-side separation of owner and agent (see "Owner decisions before merge").

## Owner decisions before merge

1. **Gated tool set and broker.** With no broker the hook blocks almost all agent work (see
   above). Decide whether to write and ship a broker first, to allow-list more non-mutating
   harness tools (`TodoWrite`, `Task`/`Agent`, `ToolSearch`, `Skill`, `BashOutput`), or to make
   the hook opt-in through `settings.local.json` instead of the committed `settings.json`.
2. **The `owner-approval` environment has no reviewers today.** The `owner-approval` job passes
   within seconds on this PR, so the GitHub environment is unprotected and the gate is a
   no-op until the owner runs `configure-owner-authority.sh` (or sets the equivalent in the UI).
3. **Agents use the owner's own token.** Agents push and open PRs with the owner's GitHub
   credentials, and the script sets `prevent_self_review: false`, so GitHub cannot tell the
   owner from an agent: an agent holding that token can approve the pending deployment itself.
   A separate bot identity (GitHub App or machine user without reviewer rights) is needed for
   agents. The script does not set `can_admins_bypass` on the environment either; verify it in
   the UI, because the "no administrator bypass" requirement above is not enforced by code.
4. **Side effects of `configure-owner-authority.sh`** on `main`: `required_linear_history: true`
   (merge commits are refused, squash or rebase only), `strict: true` (every open PR must be
   brought up to date after each merge), `enforce_admins: true`,
   `required_conversation_resolution: true`, and `required_pull_request_reviews: null` (CODEOWNERS
   is advisory only, no review is required). The PUT replaces any existing protection on `main`.
5. **Windows boundary.** `Install-MemoryVaultAgentBoundary.ps1` denies the agent account all
   access to `.git`, so `git status` and `git log` fail for that account by design. It does
   not check that the account exists (`icacls` fails late). Unless the agent runs under that
   account, the PC-level boundary below does not exist.

## PC boundary

Repository hooks cannot secure an entire PC against an agent that has unrestricted OS
credentials. For genuine PC-level enforcement, run the agent under a dedicated
least-privilege OS account/container/VM and keep owner credentials outside that boundary.

On Windows, use a dedicated agent account plus NTFS ACLs (`30_SCRIPTS/security/owner_authority/Install-MemoryVaultAgentBoundary.ps1`) and application-control policy
(Windows Defender Application Control/AppLocker where appropriate). Do not grant the
agent local-administrator rights.

## Non-bypass invariant

Changing model, prompt, tool, shell, branch, API, subagent, or executable does not create
authority. The same effective mutation must map to the same owner-approval requirement.

## Fail closed

No approval -> no mutation.
Unknown operation -> no mutation.
Malformed or empty hook input -> no mutation.
Hook crash or missing interpreter -> no mutation (exit 2).
Unavailable or non-approving owner gate -> no mutation.

Approval expiry and caller authentication are not on this list because the hook does not
implement them; see "What is enforced, and what is not".
