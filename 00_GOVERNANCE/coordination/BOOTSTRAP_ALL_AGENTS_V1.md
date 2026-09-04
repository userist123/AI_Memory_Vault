# UNIVERSAL AGENT COLD-START BOOTSTRAP V1

This file is the startup contract for every agent working on this Vault.

## On every start, regardless of PC, IDE, chat, or runtime

1. Pull/read `main` and record its SHA.
2. Read `09_COORDINATION/AGENT_MEMORY/UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1.md`.
3. Read `09_COORDINATION/AGENT_MEMORY/agents/<AGENT>/CURRENT.md`.
4. Identify the active `project_id` and read `09_COORDINATION/AGENT_MEMORY/projects/<project-id>/CURRENT.md`.
5. Open the latest task/session records referenced by those files.
6. Verify the working branch and current commit in Git.
7. Check whether another agent has changed the same area since the saved state.
8. Continue from the persisted `NEXT` action; do not reconstruct state from memory of a previous chat.

## During work

After every substantive milestone:

- update the task record;
- record exact paths;
- record test/command evidence;
- record evidence level;
- update `CURRENT.md`.

## Before stopping

The agent MUST leave:

`WHAT I DID → WHERE → EVIDENCE → WHAT FAILED/REMAINS → EXACT NEXT ACTION`

and commit the memory update to Git.

## Portable continuity target

A fresh machine must be able to determine without the old chat:

`agent → project → application → folder → branch → commit → task → changes → evidence → blockers → next action`

This is mandatory for all future projects too. For a new application, create a new `projects/<project-id>/CURRENT.md` and continue using the same protocol.
