# cost-router reference: checkpoint schema

Status: guidance, not hook-enforced. Used when a subagent would need a second resume (SKILL.md 2A
rule 1), at a phase boundary (rule 6), and when an envelope is reached (references/budgets-metrics.md).

## Schema (at most 40 lines)

```
task:        <one-line goal>
branch/PR:   <branch> / <PR number or none>   head: <sha>
files:       <files modified, one per line or comma-separated>
failing:     <exact command>
stderr:      <shortest excerpt that shows the failure, <= 8 lines>
falsified:   <hypotheses already ruled out, with the evidence>
remaining:   <acceptance criteria not yet met>
next:        <the single next step>
decisions:   <owner decisions that bind the work>
```

Leave out the transcript, narration and anything that can be re-derived from the repo.

## Verify before seeding

Before a new agent starts from a checkpoint, the lead checks it against reality:

1. `git diff --stat` (and `git rev-parse HEAD`) match `files` and `head`.
2. The `failing` command, re-run once, still fails with the quoted output; if it passes, update the
   checkpoint instead of dispatching.
3. Nothing in `falsified` is re-proposed as the plan.

A checkpoint that disagrees with `git diff` or the real test output is corrected first; an agent
seeded with a wrong checkpoint repeats the wrong work.
