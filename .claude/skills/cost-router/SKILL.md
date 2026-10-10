---
name: cost-router
description: Finish any task at the lowest token cost that keeps quality - classify the task, pick the cheapest model/effort/subagent that can complete it, delegate bulk reads and bounded changes to cheaper subagents, keep the main context small, and prove the result before claiming DONE. Use at the start of every non-trivial task and whenever a task is getting expensive.
when_to_use: Apply on every new task, bug fix, feature, refactor, research or review. Trigger phrases - "cost", "tokens", "cheap", "budget", "use the right model", "consuma putin", "economiseste tokens", "ruteaza modelul".
---

# cost-router: finish the task, spend the fewest tokens that still buy the result

Policy source: `04_CONFIG/claude_model_routing.json` inside the AI Memory Vault (explained in
`00_GOVERNANCE/protocols/Claude_Model_Routing_Policy_V1.md`); in an installed copy it is `policy.json`
next to this file. The rate card is as of the file's `pricing_as_of`.
Quality is not negotiable: every rule below cuts tokens by cutting *waste*, never by cutting
verification. If a cheaper path fails its check, escalate one tier; never ship unverified.

## 1. Classify before you act (10 seconds, no tools)

| Class | Signals | Run it as | Why it is the cheapest adequate path |
|---|---|---|---|
| explore | where is X, who imports Y, list every Z, inventory | subagent on haiku (`Explore` fits) | answer is a location, not a judgment |
| summarize | summarize, extract, classify, convert | local LLM on the PC, else subagent on haiku | checkable mechanical output |
| implement | spec is clear and tests/lint are the failure signal | subagent on sonnet (`vault-worker` fits) | run cheap first; escalate only failures |
| review | verify a DONE claim, review a diff, security check | subagent on opus (`vault-reviewer` fits) | edge cases; independent context |
| design | architecture, root cause unknown, multi-file plan | you, at `high`/`xhigh` effort | a wrong plan costs more than tokens |
| frontier | ambiguous, long-horizon, end-to-end orchestration | you, on Fable only if the owner chose it | the one place 2.5x Opus pays |
| unclear | keywords don't match | you, session default | say so; never guess cheaper |

Risk `high`/`critical` (production, credentials, secrets, deletes, auth, trust boundaries, git
history): never below Opus, and always an independent `vault-reviewer` pass before DONE. The route
helper detects these words itself (English and Romanian); if the hint says sonnet/haiku for such
work, the hint is wrong and this rule wins. Fable is never a subagent or verifier model. Escalation order on a failed check: local → haiku → sonnet → opus → fable,
one notch, only after the check actually failed.

Optional precise answer: `python3 "<this dir>/lib/route.py" "<task>"` prints class, model, effort,
subagent and estimated cost. Do not run it for trivial prompts.

**You are the dispatcher.** For every task, decide first whether it needs the session's model at
all. If not, hand it on: the subagent *type* is free (`Explore`, `general-purpose`, `vault-worker`,
any other); what the policy fixes is the *model*. A `PreToolUse` hook on `Agent` sets each spawn's
`model` from the route of its brief (cheaper than the route is raised, more than one tier above is
capped, risk forces Opus, Fable never). Pass `model` yourself only to escalate one notch after a
failed check. Keep in the main session only design, root cause, risky decisions and the final check.
Do it yourself when the work is a one-minute edit or answer: a subagent starts empty and re-reads.

**Local tier (owner's PC only, free).** For text in, short text out (summarize, extract, classify,
translate) try the local model first: `python "<this dir>/lib/local_llm.py" --file <path> "<instruction>"`
(`--kind code` for code-reading questions, `--probe` to see what is installed). The files are read
by the local model, so their text never enters this conversation. Exit 3 means no local model:
fall back to a Haiku subagent. Its answer is unverified: check it before relying on it. Never use it
for multi-step code, design, risky work or verification. Cloud sessions have no local tier.

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
