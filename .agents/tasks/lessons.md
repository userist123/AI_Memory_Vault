# Lessons

Durable lessons from real corrections, incidents and verification failures.

## Current lessons

Lessons from the 2026-10-07/08 Claude Code session (PR repair round, LogAnalyzer stage 2).

## 2026-10-08 — sequential merges need the combined test roots, not a subset
- What happened: #209 added `security/tests/test_audit_remediation.py::test_u01` asserting the
  MCP server has exactly three tools; #214 added six read-only `vault_*` tools. Each PR was
  green alone. I merged #214 after #209 having re-run only `20_TESTS` subsets on the
  combination, so `security/tests` went red on main (the security-boundary workflow is
  path-filtered and did not run on the merge).
- Rule: before merging PR B on top of a main that just received PR A, run **every** test root
  CI uses (`20_TESTS` and `security/tests`, plus any root the two PRs add) on `main + B`,
  not only the tests near the files B touches.
- Rule: when two PRs touch the same module (here `memory_mcp_server.py`), grep the other PR's
  tests for assertions on that module's public surface before merging.

## 2026-10-08 — Edits to Markdown tables
- A regex like `\| 7 \|` with count=1 hits the first matching cell anywhere in the file (it overwrote summary
  rows R4/R9 instead of the decision rows). Scope table edits to the target section and match the full row
  prefix (e.g. `| 7 | *Provisional:*`), then check `git diff` before committing.

## 2026-10-08 — Bulk worktree cleanup
- Never run a blanket "remove every worktree" while a subagent is running: it can delete the agent's
  working tree mid-task. Remove only worktrees you created yourself, by explicit path, or wait until no
  agent is running.

## 2026-10-08 — Resuming agents after a usage-limit reset
- Resuming several long-running agents at once re-sends each one's whole transcript at full price (cold
  cache): a large usage spike. Rule now: every agent keeps `00_GOVERNANCE/coordination/tasks/todo-<agent>.md`;
  restart work as a NEW agent from that checkpoint, one at a time, instead of SendMessage-resuming a long
  transcript.
- The owner believed a "reduced consumption" rule existed; it was nowhere in the repo or the preferences I
  receive. When an owner rule seems ignored, grep for it first and state plainly if it doesn't exist.

## Rule

When the owner corrects an agent or a real incident reveals a reusable failure pattern:
1. record the pattern;
2. write the preventive rule;
3. apply it immediately;
4. do not record speculative or redundant lessons.
