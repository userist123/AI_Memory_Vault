# cost-router reference: fan-out (parallel agents)

Status: guidance, not hook-enforced. No hook counts concurrent writers or checks file ownership;
the integrator (the Opus main session) is responsible for every rule below.

## When parallel writers are allowed

Default: one implementation agent at a time. Several writers may run at once only when ALL hold:

1. **Disjoint file ownership.** Every slice owns a fixed set of files; no file appears in two slices.
2. **Own worktree and branch per slice.** `git worktree add <dir> -b <branch>` per writer; a writer
   never edits another slice's worktree or the integrator's checkout.
3. **Ownership table first.** Before dispatch, the coordination checkpoint holds a table:
   slice | agent + tier | worktree | branch | owned files/globs | reserved ids | test command.
4. **Shared files belong to the integrator only.** Lockfiles, migrations, route registries, barrel
   exports (`index.ts`, `__init__.py` re-exports), CI config, release pins and the checkpoint itself.
   A slice that needs a change there writes the request in its report; the integrator applies it.
5. **No shared evolving assumptions.** If slices depend on a design that is still being decided (an
   API shape, a schema, a naming scheme), do not fan out: settle it first, write it into the
   checkpoint, then split. Fan-out on unstable assumptions multiplies rework.

## Limits

- At most **3 concurrent writers** and at most **5 concurrent read-only agents** per task.
- Launch independent agents in one message; they cannot see each other or your conversation, so
  each brief is self-contained (goal, owned files, forbidden files, test command, output contract).

## Integration

- The integrator is the Opus main session. It merges in small batches (one or two slices at a time),
  runs the affected tests after each merge, and resolves conflicts itself.
- A slice that touched a file outside its ownership is rejected and re-briefed, not merged.
- The full suite runs once before the final push, not after every merge.

## Tier per slice (by complexity)

| Slice kind | Tier / agent |
| --- | --- |
| Mechanical: rename, docs, config, formatting | Haiku |
| Spec'd logic with tests | Sonnet `vault-worker` |
| Ambiguous or risky decision (design, security boundary, migration) | stays Opus (main session) |

Risky implementers still get the mandatory Opus `vault-reviewer` pass (SKILL.md 2A, 4C).

## Read-only fan-out

- Each read-only agent returns a digest of at most **30 lines** with `file:line` references.
- Bulky output (inventories, logs, full listings) goes to a file under the task folder; the agent
  returns the path and the digest, never the content, so the orchestrator context stays small.
