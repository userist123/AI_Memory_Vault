---
name: cost-router
description: Finish any task at the lowest token cost that keeps quality - classify the task, pick the cheapest model/effort/subagent that can complete it, delegate bulk reads and bounded changes to cheaper subagents, keep the main context small, and prove the result before claiming DONE. Use at the start of every non-trivial task and whenever a task is getting expensive.
when_to_use: Apply on every new task, bug fix, feature, refactor, research or review. Trigger phrases - "cost", "tokens", "cheap", "budget", "use the right model", "consuma putin", "economiseste tokens", "ruteaza modelul".
---

# cost-router: finish the task, spend the fewest tokens that still buy the result

Policy source: `policy.json` next to this file (rate card as of its `pricing_as_of`), explained in
`00_GOVERNANCE/protocols/Claude_Model_Routing_Policy_V1.md` when you are inside the AI Memory Vault.
Quality is not negotiable: every rule below cuts tokens by cutting *waste*, never by cutting
verification. If a cheaper path fails its check, escalate one tier; never ship unverified.

## 1. Classify before you act (10 seconds, no tools)

| Class | Signals | Run it as | Why it is the cheapest adequate path |
|---|---|---|---|
| explore | where is X, who imports Y, list every Z, inventory | `Explore` subagent (haiku, low) | answer is a location, not a judgment |
| summarize | summarize, extract, classify, convert | `Explore` subagent (haiku, low) | checkable mechanical output |
| implement | spec is clear and tests/lint are the failure signal | `vault-worker` subagent (sonnet, medium) | run cheap first; escalate only failures |
| review | verify a DONE claim, review a diff, security check | `vault-reviewer` subagent (opus, high) | edge cases; independent context |
| design | architecture, root cause unknown, multi-file plan | you, at `high`/`xhigh` effort | a wrong plan costs more than tokens |
| frontier | ambiguous, long-horizon, end-to-end orchestration | you, on Fable only if the owner chose it | the one place 2.5x Opus pays |
| unclear | keywords don't match | you, session default | say so; never guess cheaper |

Risk `high`/`critical` (production, credentials, deletes, security boundary): never below Opus.
Fable is never a subagent model. Escalation order on a failed check: haiku → sonnet → opus → fable,
one notch, only after the check actually failed.

Optional precise answer: `python3 "<this dir>/lib/route.py" "<task>"` prints class, model, effort,
subagent and estimated cost. Do not run it for trivial prompts.

## 2. Shape the work so the expensive context stays small

- **One dependent chain that fits your context → do it yourself**; an orchestrator pays for plan +
  handoff + merge. Delegate only bulk, independent or self-contained pieces.
- **Bulky reads → `Explore`**: it absorbs the file dumps and returns one paragraph with `file:line`.
- **Bounded changes with a test → `vault-worker`**, with the exact spec, files, and the command that
  proves it. Ask for `decision / evidence / risks / unknowns / confidence / recommended_action`.
- **Before DONE → `vault-reviewer`** on anything non-trivial, or run the proving command yourself.
  Agent reports are not verification; only what was run counts.
- Launch independent subagents in one message, not serially. Each subagent is a fresh prefix: give
  it everything it needs in the brief; it cannot see your conversation.

## 3. Token hygiene (the free wins; apply always)

1. **Read excerpts**: `grep -n`, `sed -n a,bp`, `Read` with offset/limit. Never dump whole large
   files, lock files, logs, generated files or the whole vault into context.
2. **Do not re-read** what is already in context; do not re-run a passing check "to be sure" unless
   the inputs changed.
3. **Batch**: independent tool calls in one message; one targeted test file while iterating, the
   full suite once before the final push.
4. **Keep the cache warm**: do not change the main-session model, top-level effort or tool set
   mid-task (each change cold-starts the prompt cache and resends the whole conversation at full
   price). Switch between tasks, not inside one.
5. **Effort**: `medium` for routine work on Opus/Sonnet/Haiku, `high` where edge cases matter,
   `xhigh` for the hard tail only; `max` overthinks. Lower effort before changing model.
6. **Write less**: no narration between tool calls, no restating the plan, final report in facts
   and numbers. Subagent briefs: goal, what is ruled out, files to read, scope, output contract.
7. **Checkpoint, don't replay**: on long work keep a short checkpoint file (task, done, next,
   blockers, key files) so a resume reads that, not the transcript.

## 4. Quality guards (what you never trade for tokens)

- Identify the command that proves the claim, run it, read the exit code and the relevant output,
  then say DONE. If it cannot run: `UNVERIFIED` or `BLOCKED`, with the reason.
- Never skip, weaken or delete a test; never silently fall back; never widen scope to look busy.
- A cheaper attempt that failed is evidence, not a loss: keep its failing output, escalate one tier
  with that output in the brief (re-run-failures policy: usually half the cost of running everything
  at the top tier, same pass rate).
- Price per completed task, not per request: a cheap model that needs three retries is not cheap.

## 5. Report shape (end of task)

Outcome first, then evidence (commands, exit codes, counts), then what was not verified, then the
delegation that happened (which subagent, which model) in one line. Nothing else.
