# cost-router reference: context and prompt cache

Status: guidance, not hook-enforced, except the items marked (hook).

## Tool output

- Output over **30 lines** goes to a file (task folder or the pytest basetemp); return the path and a
  digest (counts, the failing lines, `file:line`), not the output.
- Never paste whole files back into the conversation or into a brief; cite `file:line` ranges.
- Read excerpts (`grep -n`, `sed -n a,bp`, Read with offset/limit); never re-read an unchanged file.

## Prompt-cache prefix stability

- Keep stable prefixes stable: no timestamps, random ids or per-run values in system prompts, agent
  files, skill text or the first part of a reused brief.
- Do not change the main-session model, top-level effort or tool set mid-task; each change
  cold-starts the cache and resends the conversation at full price. Switch between tasks.
- Parallel agents with the same brief template share a cached prefix; put the slice-specific part last.

## Compaction and checkpoint thresholds

| Where | Threshold | Action |
| --- | --- | --- |
| Subagent | 150k context tokens | resume warning (hook: `hook_resume_guard.py`); prefer a new agent from a checkpoint |
| Subagent | second resume | denied (hook); new agent from a <= 40-line checkpoint (references/checkpoint.md) |
| Main session | 250k | prompt hook reminder; write the checkpoint, `/clear`, continue from it |
| After compaction | - | one `cost-router:` state line is re-injected (hook: `hook_repo_map.py`) |

Prefer a checkpoint + `/clear` over relying on automatic compaction: a checkpoint keeps the exact
failing command and falsified hypotheses that a summary tends to lose.
