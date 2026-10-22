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
| frontier | ambiguous, long-horizon, end-to-end orchestration | you, on Fable only if the owner chose it | the one place 2.5x Opus pays |
| unclear | keywords don't match | you, session default | say so; never guess cheaper |

Risk `high`/`critical` (production, credentials, secrets, deletes, auth, trust boundaries, git
history): design, root cause and verification never below Opus, and always an independent
`vault-reviewer` pass before DONE. The route helper detects these words itself (English and
Romanian). Owner rule 2026-10-16: the *implementer* of risky code stays on Sonnet (high effort)
because that Opus review is mandatory afterwards; the floor binds the reviewer, not the worker
(policy `rules.implementer_risk_floor`). Critical risk and live operations (production, deploy,
delete, rotate, leaks) keep Opus: a review after the fact cannot undo them. Fable is never a subagent or verifier model. Escalation order on a failed check: local → haiku → sonnet → opus → fable,
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
4. **One implementation agent at a time.** Agents never wait for CI: they push and stop. The main
   session checks CI once (`gh pr checks <n>`, or the PR monitor) and only then dispatches review.
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

## 4A. Enforcement (the route is binding, not advisory)

- **Escalation ceiling**: Opus is the top of the ladder; a route escalates to Fable only for the
  `frontier` class in the main session. The route line says "stay on Opus" otherwise.
- **Route state**: the prompt hook stores the route per session in
  `~/.claude/cache/cost-router/route-<session_id>.json`. A short follow-up (`continua`, `ok`, under
  4 words, a slash command) keeps it; only a new non-trivial prompt replaces it. `delegated` and
  `vault_routed` are marker files next to it, so a concurrent counter write never clears them; each
  holds the prompt's epoch nonce, so a marker that could not be deleted is void for the next
  prompt. Cache files older than 7 days are pruned on each new prompt.
- **Main session dispatches**: when the route names a subagent, `hook_enforce_route.py`
  (PreToolUse on Edit/Write/NotebookEdit/Bash/Read/Grep/Glob) allows 3 direct tool calls, then
  denies until the work is delegated with the Agent tool (any Agent/Task spawn unlocks it for the
  rest of the prompt). Not counted: one single read-only or one-shot git/gh command (`git status`,
  `git log`, `gh pr view|checks|merge|edit`, ...; no chain, pipe or redirect) and a short edit of
  `MEMORY.md`, a memory note or a `todo-*.md` checkpoint. A subagent's own calls (`agent_id`/`agent_type` set) are never counted.
  A reviewer/verifier `subagent_type` is never below Opus.
- **Vault router in-hook**: inside the AI Memory Vault, an Agent spawn for an implement/review/design/
  frontier route first runs `python -m routing.route_cli route --goal <first 300 chars>` itself (5 s
  timeout, `PYTHONPATH=03_IMPLEMENTATION/packages`; the goal is never stored). ROUTED/PLANNED records the
  receipt and allows (context line `vault-router: <status> -> <subagent_type>`, only with COST_ROUTER_VERBOSE_ROUTE=1); BLOCKED, a crash, a
  timeout or unparsable output denies and sets `delegation_denied` (fallback text: run the router
  in Bash). A Bash call of `routing.route_cli route|dispatch` (at the command start or after a
  separator; a mention does not count) still marks the router as run, so no second run happens.
  The call is also recorded per session as a route receipt for the prompt's task class; a later
  prompt of the same class reuses it instead of running the router again. Inside Claude Code
  (`CLAUDECODE=1`) the router offers `vault-worker`, `vault-reviewer` and `Explore` as runtimes
  (`execute_via: claude_code_agent_tool`); a security review with no vendor-independent verifier
  falls back to `vault-reviewer`, logged in the receipt, instead of BLOCKED.
  Launchers recognised: `python`/`python3` (any path, flags such as `-I`, `-X utf8`), `py [-3]`,
  `uv run python`, `env VAR=x python`. Known limitation: the mark is set in PreToolUse, when the
  command is about to run, not after it succeeded (the Bash PostToolUse input does not reliably
  expose an exit code), so a router call that fails still counts as routed.
- **Advisory on the same tier**: SessionStart stores the session `model` (startup/clear/compact only; a later
  `/model` switch is not seen until the next compact or restart). If the route's model tier EQUALS the session's
  (sonnet session, sonnet route), the 4th direct call is allowed with one note instead of denied: delegating to
  the same tier is pure overhead (final A/B, 24 runs: +9..+74% cost where implement work went to `vault-worker`
  on the session model; `07_EVALUATION/reports/cost_router_claude_ab_benchmark_2026-10-21_phase2_final/SUMMARY.md`).
  A route ABOVE the session (risk floor, review -> opus) or BELOW it (opus session, sonnet route) is enforced as
  before; unknown session model: denied as before.
  The session model is resolved on SessionStart and on every prompt (`route_state.resolve_session_model`: payload `model` >
  stored > last assistant `message.model` of the transcript > env `ANTHROPIC_MODEL` > `.claude/settings.local.json`, `.claude/settings.json`,
  `~/.claude/settings.json` `model` > unknown); headless payloads carry no `model`. On an equal tier (class not review/design/frontier,
  risk < high) the route line reads `cost-router: <class> same tier (<model>): direct work ok`, the state keeps `subagent` for
  telemetry plus `same_tier: true`, and enforcement is one advisory note.
  A router decision whose `execute_via` is not `claude_code_agent_tool` denies the Claude spawn (dispatch it with `route_cli dispatch`).
- **Resume guard**: `hook_resume_guard.py` (PreToolUse on SendMessage) finds the target's
  transcript (`<session>/subagents/agent-<id>.jsonl`); a second resume of the same subagent is
  denied, a first one above 150k context tokens (`COST_ROUTER_RESUME_MAX_TOKENS`) gets a warning.
  Teammates, other sessions and unknown names are left alone when the session's subagents directory exists;
  when the transcript cannot be resolved the resume is counted by the named agent id (no fail-open). The counter
  is a read-modify-write under a lock file (`route-<sid>.resumes.lock`), so concurrent hooks lose no increment.
- **Overrides**: `#direct` in the prompt, even a short one such as `#direct do it` (owner: the
  main session does it itself), or
  `COST_ROUTER_ENFORCE=0` in the environment. Any hook error allows the call.
- **Kill switch**: `COST_ROUTER_OFF=1` (also `true`/`yes`/`on`) makes every hook return at once: exit 0, no stdout, no
  state writes (`route_state.disabled_by_env()`; used by the benchmark baseline arm so both arms load identical
  CLAUDE.md, project MCP and skills and differ only in the hook layer). `hook_agent_usage.py --report` still works.

### 4B. Subagent usage telemetry and idempotency

- **Telemetry**: `hook_agent_usage.py` (PostToolUse on `Agent|Task`) records per subagent, in
  `route-<sid>.agent_usage.jsonl` (append-only JSON lines, last 200): resolved model, requested model, the 4-way token split
  (input, cache_read, cache_creation, output), total tokens, duration, tool-use count and an
  estimated USD cost from the policy rate card (`transcript_usage.estimate_cost_usd`; unknown model
  -> `null`, never an invented price). Prompt and subagent content are never stored; it prints nothing.
- **Idempotency keys** (`route_state.seen_event`, sidecar `route-<sid>.seen`, last 500 keys): `post:<tool_use_id>`
  (telemetry), `pre:<tool_use_id>` (main-session call counter; a replay returns the same decision),
  `resume:<tool_use_id>` (resume counter), `prompt:<prompt_id>` (prompt routing). Hooks of one event
  run in parallel and an identical command in several settings files runs once, but a copy under a
  different path (a global install) runs twice, so a repeated id must not count twice.
- **Never both paths closed**: when `hook_agent_model.py` denies a spawn (router BLOCKED or unavailable) it sets the
  per-prompt marker `delegation_denied`; `hook_enforce_route.py` then allows at most `BREAKER_EXTRA_CALLS` (6) direct calls past the 3 free ones
  (the first with a context line), then denies; a new prompt epoch resets it.
- **Fail open**: these hooks are efficiency policy, not authorization. Malformed JSON, missing fields,
  an unknown event, an unwritable state directory or any exception means exit 0 with no output.
- **context_tokens** = `input_tokens + cache_read_input_tokens + cache_creation_input_tokens` of the
  last assistant message (output excluded); `transcript_usage.usage_split()` returns all parts.
- **Report**: `python .claude/skills/cost-router/hook_agent_usage.py --report [session_id]` prints the
  recorded subagents with per-model totals and the summed estimated cost (read-only). After a
  compaction `hook_repo_map.py` also adds one `cost-router:` state line to the context.

## 4A. Context-size switches (environment)

- `COST_ROUTER_REPO_MAP_MAX_CHARS` caps the SessionStart repo map (default 2500, about 600 tokens); the
  largest directories are kept, the rest is summarised as "N smaller directories omitted".
- `COST_ROUTER_NO_REPO_MAP=1`: `hook_repo_map.py` prints nothing.
- The per-prompt route line is compact by default (class, target, model; at most 120 chars). The long form
  (effort, verifier, owner-language reminder, local-LLM hint, enforcement note) needs `COST_ROUTER_VERBOSE_ROUTE=1`.
- Enforcement does not depend on the text: state is persisted either way. `COST_ROUTER_ENFORCE=0` still disables it.
- The compact line ends with ` lang=<code>` when `~/.claude/settings.json` sets `language` (still at most 120 chars).

## 4C. Escalation policy (owner rule 2026-10-21; hook_agent_model.py, never silent)
- Review/verify briefs (by subagent type OR brief), critical risk and live operations (production, deploy, delete,
  rotate: `rules.implementer_risk_floor.never_for_stems`) need opus, whatever `subagent_type` or `model` the Agent call
  names. A risk-high IMPLEMENTER (worker type or non-review brief) stays on its class model (sonnet) because the Opus
  reviewer pass is mandatory (`rules.implementer_risk_floor`, owner rule 2026-10-16).
  On a session below the required tier the spawn is DENIED (`requires <tier>; session is <s>`) unless the owner wrote `#escalate`.
- `#escalate` counts only in the owner's own prompt (UserPromptSubmit), only for that prompt; `tool_input` text never does.
- Normal-risk work is capped at the session tier (note `not escalated: session <s>; #escalate allows <t>`);
  an explicit `model` above the session tier is capped the same way. Downward routing is unchanged.
- An explicit `model` is capped the same way on EVERY class, an unclassified brief included (W7): above the session tier ->
  session tier + note; unclassified without `model` and an agent default (frontmatter `model:` or `CLAUDE_CODE_SUBAGENT_MODEL`)
  above the session tier -> `updatedInput.model` = session tier + note. `#escalate` allows with a note.
- R2-1: the agent default is read from the project `.claude/agents/<type>.md`, then the user `~/.claude/agents/<type>.md`
  (matched by directory listing; the type name is never a path). A plugin type (`a:b`), a path-like name, a non-built-in
  type with no agent file, an unreadable file or an unrecognised `model:` is UNKNOWN -> session tier + note, even with
  `#escalate`. Built-ins (general-purpose, Explore, Plan, ...) inherit and are left alone.
- R2-2: an explicit `model` is resolved by alias, exact id, alias with a `[..]` suffix (`opus[1m]`) or the longest model_id
  prefix followed by `-`/`[`; same tier as the target keeps the given spelling. Anything else (`claude-opus-4-1`, `gpt-5`, `""`)
  is never passed through: unclassified -> session tier, classified -> route model, risky/review -> the floor; note
  `unrecognised model '<x>'; capped to <tier>`; `#escalate` does not apply to it.
- Unknown session model = cheapest tier (note: set ANTHROPIC_MODEL). The transcript's last model beats a stored one.
  A model known only from a settings file (`route_state.resolve_session_model_ex` source `settings`/`none`) is a guess and counts
  as unknown for escalation; payload, transcript, stored and env `ANTHROPIC_MODEL` are trusted.
- `COST_ROUTER_OFF=1` stays inert for every hook, including `install.py --session-start`.
- `COST_ROUTER_NO_SELF_INSTALL=1` makes `hook_self_update.py` and `install.py --session-start/--self-update` inert:
  no git, no network, no write under `~/.claude`.
- Hook-driven refresh is OFF by default (owner, 2026-10-22): `install.py --session-start/--self-update` and
  `hook_self_update.py` do nothing unless `COST_ROUTER_SELF_INSTALL=1` or `~/.claude/cost-router.auto-refresh`
  exists (`--uninstall` removes it). The opt-outs above keep priority. HIGH-2 (unpinned remote) stays open.

## 4B. Install integrity (VERSION and --verify)

Every install (also `--session-start`) writes `VERSION`: line 1 is the source commit (`git rev-parse HEAD`, `+dirty`, or `unknown`; the
self-update ref when given), line 2 the sha256 of the file manifest. `python install.py --verify [--json] [--home <dir>]` is read-only:
it lists missing/extra/differing files and both VERSION lines; exit 0 only when identical, 1 on any difference or a missing VERSION.

## 5. Report shape (end of task)

Outcome first, then evidence (commands, exit codes, counts), then what was not verified, then the
delegation that happened (which subagent, which model) in one line. Nothing else.

## Token-lean defaults (phase 3) and their env switches
- Read/Grep/Glob are never denied by the enforce hook (a deny costs a retry request that re-reads the prefix); the delegate reason is delivered once as context. Edit/Write/NotebookEdit/non-exempt Bash are still denied. `COST_ROUTER_DENY_READS=1` restores the deny.
- The route line prints only when (class, model, subagent) changes, on risk >= high, or with `COST_ROUTER_VERBOSE_ROUTE=1` (long form, always). State is written every prompt either way.
- The repo map is injected on `startup` only (`COST_ROUTER_REPO_MAP=always`: also clear/compact); compact gets a one-line state. Cap 1500 chars (`COST_ROUTER_REPO_MAP_MAX_CHARS`).
- The Agent hook emits no allow-path context (only the denial reason, or `COST_ROUTER_VERBOSE_ROUTE=1`); read-only agent types (strict allowlist: a project `.claude/agents/<type>.md` whose frontmatter `tools` are all in Read, Grep, Glob, LS, WebFetch, WebSearch or vault-memory read MCP tools; the built-in Explore without such a file, or the project Explore with Bash, is NOT read-only) are clamped to the explore class (haiku, cap one tier above) on non-risky, non-review/design briefs only (review/verify/design or risk >= high keep the old floor); `COST_ROUTER_READONLY_FLOOR=1` restores the old floor. The subagent `prompt` is never modified.
- `COST_ROUTER_OFF=1` makes every hook inert. Evidence: the phase-3 benchmark dir under `07_EVALUATION/reports/` (to be produced).
