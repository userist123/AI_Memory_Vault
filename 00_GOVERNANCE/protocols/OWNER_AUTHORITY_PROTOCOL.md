# Owner Authority Protocol

## Purpose

Memory Vault treats the creator/repository owner as the final authority for mutations to
the repository and for privileged machine operations performed by an AI agent.

An LLM is an untrusted operator, including an uncensored or locally hosted model.
The model must never be the source of authority.

## Trusted chain

LLM -> tool boundary -> owner-authority gate -> owner-controlled approval -> operation

If the owner gate is unavailable, malformed, expired, or cannot authenticate the owner,
the operation is denied.

## Repository requirements

All changes to main must pass the protected GitHub Actions environment named owner-approval.
That environment must list @userist123 as a required reviewer and must allow the owner to
approve the job even when the owner initiated the workflow. The environment must not permit
an administrator bypass. CODEOWNERS identifies the owner for protected files.

The repository setup script tools/github/configure-owner-authority.sh configures the owner
approval environment and protected main branch. These settings are intentionally outside
normal repository content: committed files cannot safely enforce their own GitHub security
settings.

GitHub supports required environment reviewers and can prevent bypass of environment
protection; required checks can then be made mandatory for a protected branch.

## Agent tools

.claude/settings.json installs a PreToolUse fail-closed hook. Read-only discovery tools
are allowed. All other tool calls require an external owner gate.

The external gate must authenticate the human owner independently of the LLM and must not
expose its secret/token to the agent process.

## PC boundary

Repository hooks cannot secure an entire PC against an agent that has unrestricted OS
credentials. For genuine PC-level enforcement, run the agent under a dedicated
least-privilege OS account/container/VM and keep owner credentials outside that boundary.

On Windows, use a dedicated agent account plus NTFS ACLs and application-control policy
(Windows Defender Application Control/AppLocker where appropriate). Do not grant the
agent local-administrator rights.

## Non-bypass invariant

Changing model, prompt, tool, shell, branch, API, subagent, or executable does not create
authority. The same effective mutation must map to the same owner-approval requirement.

## Fail closed

No approval -> no mutation.
Unknown operation -> no mutation.
Unknown caller -> no mutation.
Unavailable owner gate -> no mutation.
Expired approval -> no mutation.
