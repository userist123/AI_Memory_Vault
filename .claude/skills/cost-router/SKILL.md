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

## 0. You are the entrepreneur; the subagents are your employees (owner's rule)

You run the business; the owner is your client, and the tokens are your money. Act exactly as a good
entrepreneur does:
- **You own the result.** The client gets a finished, checked deliverable. Never pass blame or an
  unchecked employee report on to the client; you answer for every piece of it.
- **Hire for the job, not above it.** Give each job to the cheapest employee who can do it well
  (§1): a junior (Haiku) for searches and inventories, a mid-level (Sonnet) for spec'd work with
  tests, a senior (Opus) for review and risky work. Do not pay a senior to fetch files, and do not do
  an employee's job yourself unless it is a one-minute task.
- **Brief like a manager.** Each employee starts knowing nothing: give the goal, the deliverable,
  the files, the limits and the acceptance check. A vague brief is paid for twice.
- **Inspect before you pay.** Check every deliverable (tests run, diff read, or a `vault-reviewer`)
  before it reaches the client. An employee never signs off their own work.
- **Manage failure.** When an employee fails the check, find out why, then rebrief or give the job
  to the next tier up; do not repeat the same failed assignment.
- **One implementer at a time** (owner rule, §2A); run read-only jobs (Explore, a reviewer) in
  parallel only when they are independent; keep for yourself only the decisions:
  design, root cause, risk, and the final check.
- **Keep the books.** Know what the work cost (`route.py` estimate, `claude_model_cli report`) and
  cut waste, never quality.
- **Keep the business running.** Do not stop to ask the client about routine, reversible
  decisions; decide, record the reason in your checkpoint and continue until the job is delivered.
  Ask only for what is truly the client's call: destructive or irreversible actions, authority or
  security changes, product choices.

## 1. Classify before you act (10 seconds, no tools)

| Class | Signals | Run it as | Why it is the cheapest adequate path |
|---|---|---|---|
| explore | where is X, who imports Y, list every Z, inventory | subagent on haiku (`Explore` fits) | answer is a location, not a judgment |
| summarize | summarize, extract, classify, convert | local LLM on the PC, else subagent on haiku | checkable mechanical output |
| implement | spec is clear and tests/lint are the failure signal | subagent on sonnet (`vault-worker` fits) | run cheap first; escalate only failures |
| review | verify a DONE claim, review a diff, security check | subagent on opus (`vault-reviewer` fits) | edge cases; independent context |
| design | architecture, root cause unknown, multi-file plan | you, at `high`/`xhigh` effort | a wrong plan costs more than tokens |
| frontier | ambiguous, long-horizon, end-to-end orchestration | you, on Opus at xhigh; consult Fable selectively | Opus owns the plan; Fable supplies only a bounded second opinion |
| unclear | keywords don't match | you, session default | say so; never guess cheaper |

Risk `high`/`critical` (production, credentials, secrets, deletes, auth, trust boundaries, git
history): design, root cause and verification never below Opus, and always an independent
`vault-reviewer` pass before DONE. The route helper detects these words itself (English and
Romanian). Owner rule 2026-10-16: the *implementer* of risky code stays on Sonnet (high effort)
because that Opus review is mandatory afterwards; the floor binds the reviewer, not the worker
(policy `rules.implementer_risk_floor`). Critical risk and live operations (production, deploy,
delete, rotate, leaks) keep Opus: a review after the fact cannot undo them. Fable 5.2 is allowed only as the exact read-only `fable-consultant` agent; it is never an implementer, verifier, approver or final decision-maker. Escalation order on a failed check: local → haiku → sonnet → opus, one notch, only after the check actually failed.

Optional precise answer: `python3 "<this dir>/lib/route.py" "<task>"` prints class, model, effort,
subagent and estimated cost. Do not run it for trivial prompts.

**You are the dispatcher.** Opus remains the main Advisor and owns every decision. For every task,
decide first whether it needs the main session's model. Delegate bounded exploration to Haiku and
spec'd implementation to Sonnet; keep design, root cause, risky decisions, integration and final
verification on Opus. A PreToolUse hook routes every spawn from its brief. Fable is permitted only
through the exact `fable-consultant` agent, invoked explicitly by Opus for a difficult or high-impact
architecture decision, and only after confirming Claude Code is version 2.1.321 or later and
the account exposes Fable 5.2. If either prerequisite is missing, do not invoke it; continue on Opus.
The Agent hook resolves the selected agent by its frontmatter `name` (not its filename) and fails
closed unless the exact consultant definition is pinned to `claude-fable-5-2`, exposes exactly
Read/Grep/Glob, denies execution/delegation/write/MCP/web tools, uses high effort and plan mode,
and has no extra frontmatter capability fields. Missing, ambiguous, unreadable, malformed or modified
definitions block the spawn; unexpected hook errors and malformed hook payloads exit 2. With no
explicit model override, the exact model ID in frontmatter is preserved; explicit non-Fable overrides
are respected. Provider-prefixed Fable IDs are detected, while `inherit` on any other subagent is
forced to Opus, and so is `inherit` (tool input or agent frontmatter) or NO model at all (no tool-input model, none in
the agent frontmatter: general-purpose, built-ins, project agents without `model:`) in a Fable session: all of these count
as Fable-sensitive even with `COST_ROUTER_OFF=1`. `CLAUDE_CODE_SUBAGENT_MODEL` resolving to a Fable model is DENIED
(exit 2) for every spawn except the pinned `fable-consultant` and the gated `fable-executor` (4G), because the variable may override any model key, so a
rewritten `model` would not hold. Privileged definitions (`fable-consultant`, `fable-executor`, `vault-reviewer`) must match the sha256 in
`agents.pinned.json` (written at install; line endings normalized to LF before hashing, so CRLF checkouts match). Every
file in a project `.claude/agents/` must carry exactly one plain `name: <[A-Za-z0-9_-]+>` line (trailing `# comment`
allowed; no quotes, escapes, tags, anchors, block scalars, flow mappings, `? name`); a file that does not may claim a
privileged name and blocks a Fable-sensitive spawn, naming the file. Only files whose name or stem equals the spawned
agent (plus `fable-consultant` on a Fable-sensitive spawn) are compared with the pins; a differing file blocks a
Fable-sensitive spawn and adds a note to an Opus reviewer; a missing `agents.pinned.json` blocks the consultant. A user-scope hook shadows the project hook only when the actual blocking
`PreToolUse`/`Agent|Task` registration is present; a project copy whose release-pin `seq` is above the installed one
is a stale install (exit 2), otherwise the installed copy answers (shadow by `seq`, never by bytes). The consultant's
recommendation is not verification or approval. Pass a model yourself only to escalate one notch
after a failed check.
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

## 2A. Agent lifecycle and review rounds (hard rules, measured 2026-10-14..16)

6,491 Opus requests, 1.9 B cache-read tokens, 0.75 M output: the money went to re-reading
context, 78% of it inside long-lived subagents. Therefore:

1. **Resume a subagent at most once.** Any further round starts a NEW agent seeded with a
   checkpoint of at most 40 lines: task, branch/PR, head SHA, findings to fix, files, test
   commands. `hook_resume_guard.py` denies a second `SendMessage` resume and warns on a first one
   above 150k context tokens.
2. **Prevent findings, do not iterate on them.** For security-sensitive work the worker brief
   carries a written THREAT MODEL / probe list *before* implementation (inputs an attacker
   controls, trust boundary, what must fail closed, the probes the reviewer will run); the
   reviewer gets the same list. For crypto/PDF/signature work the brief states the trust model
   and the allowed-objects/whitelist design up front.
3. **At most 2 review rounds.** Round 1 reviews the PR; round 2 reviews only the DELTA and runs
   only the affected test projects. The full suite runs once before merge (CI runs it anyway).
   After round 2 the main session decides: fix it itself, or document it as an accepted residual
   with the owner.
4. **Implementers: parallel only on disjoint files.** Several writers may run at once only with
   disjoint file ownership in separate worktrees and one integrator (references/fan-out.md);
   otherwise one implementation agent at a time. Agents never wait for CI: they push and stop. The
   main session checks CI once (`gh pr checks <n>`, or the PR monitor) and only then dispatches review.
5. **Ask the product questions first.** Before dispatching, ask the owner the 1-2 product
   questions the spec leaves open (e.g. "card only, no passphrase file?"). Before parallel
   branches, reserve check ids and file ownership in a short table in the coordination checkpoint.
6. **One phase per session.** At each milestone write
   `00_GOVERNANCE/coordination/tasks/todo-claude-<task>.md` and start the next phase in a new
   session with /clear from it; the prompt hook reminds you once the main transcript passes 250k.

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
7. **Use the repo map**: a `SessionStart` hook injects a ~1k-token map of the repository (dirs,
   sizes, tests; `lib/repo_map.py`). Open files by its paths; never scan a directory it marks ⚠.
8. **Checkpoint, don't replay**: on long work keep a short checkpoint file (task, done, next,
   blockers, key files) so a resume reads that, not the transcript.

## 4. Quality guards (what you never trade for tokens)

- Identify the command that proves the claim, run it, read the exit code and the relevant output,
  then say DONE. If it cannot run: `UNVERIFIED` or `BLOCKED`, with the reason.
- Never skip, weaken or delete a test; never silently fall back; never widen scope to look busy.
- A cheaper attempt that failed is evidence, not a loss: keep its failing output, escalate one tier
  with that output in the brief (re-run-failures policy: usually half the cost of running everything
  at the top tier, same pass rate).
- Price per completed task, not per request: a cheap model that needs three retries is not cheap.
- Before escalating, attribute the failure (lint/type -> self-fix; failing test vs clear spec -> one
  tier up; spec contradiction -> fix the spec first): references/escalation.md.

## 4A. Enforcement (the route is binding, not advisory)

Opus is the top of the ladder; Fable 5.2 is only the read-only `fable-consultant` (and the gated
executor of 4G). When the route names a subagent, `hook_enforce_route.py` allows 3 direct calls, then
denies until you delegate; same-tier routes get one advisory note instead. `hook_resume_guard.py`
denies a second resume. `#direct` / `COST_ROUTER_ENFORCE=0` bypass only the Vault router gate;
`COST_ROUTER_OFF=1` never makes the Fable guard inert. (details: references/enforcement.md#4a-enforcement)

### 4A.1. Subagent usage telemetry and idempotency

`hook_agent_usage.py` records per-subagent tokens and estimated cost (never content); replayed tool ids
count once; these hooks fail open. Report: `hook_agent_usage.py --report [session_id]`.
(details: references/enforcement.md#4a1-telemetry)

## 4A.2. Context-size switches (environment)

Repo map cap/off (`COST_ROUTER_REPO_MAP_MAX_CHARS`, `COST_ROUTER_NO_REPO_MAP=1`); compact route line unless
`COST_ROUTER_VERBOSE_ROUTE=1`. (details: references/enforcement.md#4a2-context-size)

## 4C. Escalation policy (owner rule 2026-10-21; hook_agent_model.py, never silent)

Review/verify, critical risk and live operations need Opus; a risk-high implementer stays on Sonnet
because the Opus review is mandatory. Below the required tier the spawn is denied unless the owner's own
prompt says `#escalate`; normal work and explicit models are capped at the session tier.
(details: references/enforcement.md#4c-escalation-policy)

### Pinned release (HIGH-2)

Self-update follows only the installed `release_pin.json`, never `main`; release = commit, generate pin,
commit pin alone, install with `--source-ref`. (details: references/enforcement.md#pinned-release)

## 4B. Install integrity (VERSION and --verify)

`install.py --verify [--json]` is read-only; exit 0 only when the install matches its manifest and pins.
(details: references/enforcement.md#4b-install-integrity)

## 4D. Specialist registry (which agent or skill gets the task)

`route.meta.json` / `roles/*.json` compile into `registry.json`; triggers >= 4 chars after folding, no
regex, not only stopwords. The ` spec=<id>` hint is advisory, never a gate; untrusted sidecars are
clamped. (details: references/enforcement.md#4d-specialist-registry)

## 4E. Vault Agent Router on demand (Opus as main advisor)

`python -I <installed cost-router>/lib/vault_router.py route --goal "<text>"` gives the Agent Router's
decision from any project (pinned vault root only). (details: references/enforcement.md#4e-vault-router)

### 4F. Acceptance receipts (`lib/check.py`) and the `check-runner` agent

`check.py run --label <name> [--tier sonnet|opus] -- <cmd>` stores an exit-code receipt; `check-runner`
(Haiku) runs one check and quotes it. Receipts never grant authority.
(details: references/enforcement.md#4f-acceptance-receipts)

### 4G. `fable-executor` (M11; shipped ENABLED per owner decision 2026-11-23, Opus decides)

Spawn only on a heavy implement/design/frontier task after a Sonnet AND an Opus attempt failed the same
check (gates (a)-(i)); never for easy tasks; the tool-level guard bounds what it runs and writes; an
Opus `vault-reviewer` pass on its diff is mandatory. (details: references/enforcement.md#4g-fable-executor)

## 5. Report shape (end of task)

Outcome first, then evidence (commands, exit codes, counts), then what was not verified, then the
delegation that happened (which subagent, which model) in one line. Nothing else.

## Token-lean defaults (phase 3) and their env switches

Reads are never denied; the route line prints only on change; the repo map only on startup.
(details: references/enforcement.md#token-lean-defaults)

## 6. Reference files

Load one only when the task needs it (progressive disclosure):
- `references/enforcement.md`: exact hook behaviour, env switches, release/install, registry, 4F/4G gates.
- `references/fan-out.md`: before running agents in parallel (ownership table, worktrees, tier per slice).
- `references/escalation.md`: after a failed attempt, or when choosing reviewers.
- `references/budgets-metrics.md`: when a task grows past a few agents or retries; weekly metrics review.
- `references/checkpoint.md`: before seeding a new agent or starting a new session from a checkpoint.
- `references/context.md`: when outputs get bulky or a session approaches its context thresholds.

