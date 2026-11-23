# cost-router reference: budgets and metrics

Status: guidance, not hook-enforced, except the items marked (hook).

## Per-task envelopes

| Envelope | Limit |
| --- | --- |
| Subagents per task | <= 8 |
| Concurrent writers | <= 3 (references/fan-out.md) |
| Retries per slice | <= 2 |
| Review rounds | <= 2 (SKILL.md 2A rule 3) |
| Subagent resumes | 1; a second is denied (hook: `hook_resume_guard.py`) |
| Subagent context | warning at 150k on resume (hook: `hook_resume_guard.py`, `COST_ROUTER_RESUME_MAX_TOKENS`) |
| Main session context | checkpoint at 250k (the prompt hook reminds you) |

When an envelope is reached: **stop, checkpoint, decide** (re-plan or escalate) and record why in
the checkpoint. Do not quietly continue past an envelope.

## Metrics

Source: `python .claude/skills/cost-router/hook_agent_usage.py --report [session_id]` (read-only;
per-subagent model, token split and estimated cost), plus the `lib/check.py` receipts.

- **First-pass yield** per task class and tier: share of slices whose first attempt passed its check.
- **Escalation waste:** tokens spent on failed attempts before the passing one.
- **Classifier misroutes:** the route class the hook chose vs the class the work turned out to be.
- **Cost per verified change:** total estimated cost / changes that passed their proving command.

Review the metrics weekly. A tier with low first-pass yield on a class is a routing problem, not a
reason to retry more.

## Changing policy

- Routing and policy changes are versioned in `policy.json` (source: `04_CONFIG/claude_model_routing.json`)
  and shipped through the pinned release (references/enforcement.md#pinned-release), with a rollback
  path: the previous commit and pin.
- Model names and prices are configuration, not code; revalidate them when a provider changes
  models or pricing (`pricing_as_of` in the policy).
