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

All pull requests require explicit approval by @userist123 through the owner-approval
workflow. CODEOWNERS identifies the owner.

The owner must configure GitHub branch/ruleset protection with:
- pull request required before merge;
- required owner review;
- required status check owner-approval;
- no administrator/automation bypass;
- force-push and branch deletion disabled on protected branches.

Committed files cannot safely enforce these GitHub repository settings by themselves.

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
