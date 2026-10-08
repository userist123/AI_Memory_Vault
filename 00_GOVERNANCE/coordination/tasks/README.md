# Agent checkpoints — cold-resume protocol

Owner rule (2026-10-08). Applies to every agent working in this repository (Claude Code and its
subagents, Codex, Gemini/Antigravity, ChatGPT, others).

## Why
Resuming an agent by reloading its whole conversation re-sends hundreds of thousands of tokens
at full price (the prompt cache is cold after a pause or a usage-limit reset). Several agents
resumed at once multiply that. A short checkpoint file lets a fresh agent continue with a small
context instead.

## The rule
1. **One checkpoint file per agent:** `00_GOVERNANCE/coordination/tasks/todo-<agent-name>.md`
   (`<agent-name>` = your role or work package, lower-case, e.g. `todo-claude-orchestrator.md`,
   `todo-claude-wp2.md`, `todo-codex.md`, `todo-antigravity.md`).
2. **Keep it short (≤ 60 lines)** and current. Use the template below.
3. **Update it** at every milestone (a commit, a push, a PR opened, CI result, a decision, a
   blocker) and always **before you stop** for any reason. Commit and push it together with your
   work, on your own branch.
4. **Cold resume:** read only your checkpoint file and the files it points to. Do not reload the
   previous conversation. An orchestrator restarting work starts a **new** agent with the
   checkpoint path instead of resuming a long transcript.
5. **When the work is merged or abandoned,** mark the checkpoint `STATUS: DONE` (or delete it
   in the same PR that finishes the work).

## Token economy (applies always)
- Read excerpts (`grep`, line ranges), not whole large files; never dump generated files,
  lock files or logs into context.
- Run the full test suite once before the final push, not repeatedly; use targeted tests while
  iterating.
- Prefer one agent at a time; run agents in parallel only when the work is independent and the
  owner accepts the cost.
- Final reports: short, facts and numbers only.

## Template
```
# todo-<agent-name>
STATUS: IN_PROGRESS | BLOCKED | DONE        UPDATED: <ISO timestamp>
TASK: <one line>
BRANCH / PR: <branch> / <PR url or none>    BASE: <base sha>
SPEC: <paths of the spec/decisions to read first>
DONE:
- ...
NEXT (in order):
1. ...
BLOCKERS / OWNER QUESTIONS:
- ...
KEY FILES:
- ...
VERIFICATION SO FAR: <tests run, results, CI state>
```
