# AGENT MEMORY

Universal persistent continuity layer for all agents.

## Cold start

Read in this order:

1. `UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1.md`
2. `agents/<YOUR_AGENT>/CURRENT.md`
3. `projects/<PROJECT_ID>/CURRENT.md`
4. latest referenced task/session record
5. verify branch and SHA against Git

## Agents

- CODEX: `agents/CODEX/CURRENT.md`
- ANTIGRAVITY: `agents/ANTIGRAVITY/CURRENT.md`
- PERPLEXITY: `agents/PERPLEXITY/CURRENT.md`
- LUNA: `agents/LUNA/CURRENT.md`

## Rule

Do not trust chat history as the source of continuity. The Vault is the portable state. Every substantive session must leave a committed trail containing project, application, folder, branch, commit, work performed, evidence, blockers and exact next action.
