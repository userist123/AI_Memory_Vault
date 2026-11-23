# cost-router reference: escalation and reviewer selection

Status: guidance, not hook-enforced, except where a hook is named. The tier floors and caps of
SKILL.md 4C and the `fable-executor` gate of 4G are enforced by the hooks and are unchanged here.

## Attribute the failure before escalating

| Failure | Action |
| --- | --- |
| Lint, syntax or type error | One self-fix on the same tier. No escalation. |
| A test fails against a clear spec | Next tier, one notch only (Haiku -> Sonnet -> Opus), with the failing command and output in the brief. |
| Spec contradiction, missing requirement, security-boundary ambiguity | Stop implementing. Opus (or `fable-consultant`, read-only) fixes the spec/checkpoint first; then a NEW executor gets the corrected brief. |

- A higher tier does not fix a wrong spec; escalating on a spec problem only buys a more expensive
  wrong answer.
- Keep every failing output: it is the evidence the next tier needs (and 4G gate (d) needs receipts
  from `lib/check.py run --tier sonnet|opus`).

## Zero-progress breaker

Two consecutive attempts with no new passing check -> stop, write a checkpoint
(references/checkpoint.md) and re-plan. Do not start a third attempt of the same approach.

## `fable-executor`

Unchanged: SKILL.md 4G (details: references/enforcement.md#4g-fable-executor). Only on a heavy task
after a Sonnet and an Opus attempt failed the same check; never for easy tasks.

## Reviewer selection

1. **Deterministic gates first.** Lint, types, tests, SAST and gitleaks are green before any LLM
   review is dispatched. An LLM reviewer never replaces a failing deterministic gate.
2. **Default reviewer:** `vault-reviewer` (Opus).
3. **Second read-only reviewer:** `fable-consultant` only for auth/crypto, sandbox or trust
   boundaries, concurrency, irreversible migrations, or when Opus review rounds disagree.
4. **The author never reviews its own work** (same agent, same transcript).
5. **Reviewer brief:** acceptance criteria, the probe list (same as the worker's threat model,
   SKILL.md 2A rule 2), the diff, and the test evidence (commands, exit codes, counts).
6. At most 2 review rounds (SKILL.md 2A rule 3); round 2 reviews only the delta.
