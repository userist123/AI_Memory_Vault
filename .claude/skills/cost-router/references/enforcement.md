# cost-router reference: enforcement and hook internals

Loaded on demand from `SKILL.md` (sections 4A-4G and the token-lean defaults). The text below is the
detail moved verbatim out of `SKILL.md`; only the duplicated headings were renumbered (4A.1, 4A.2).
This file describes what the hooks enforce; the hooks are the source of truth.

<a id="4a-enforcement"></a>
## 4A. Enforcement (the route is binding, not advisory)

- **Escalation ceiling**: Opus is the top of the execution and verification ladder. The `frontier`
  class remains on Opus at xhigh effort. Fable 5.2 is not a route tier; it is a bounded, read-only
  second-opinion consultant available only through `fable-consultant` (requiring an `opus` session).
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
  in Bash). A Bash call counts as a router receipt only when it is ONE plain command (no `;` `&` `|` redirect,
  backtick, `$(`, newline or comment) whose interpreter is a recognised launcher (python/python3/py [-3], `uv run python`,
  behind `env`/`VAR=x`) and that runs `-m routing.route_cli route|dispatch` (module form only, never inside `-c`) or the
  INSTALLED absolute `lib/vault_router.py route`; a mention, a chain, a project copy of the script or a relative path does
  not count. It then marks the router as run, so no second run happens.
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
- **Overrides**: `#direct` in the prompt and `COST_ROUTER_ENFORCE=0` bypass the Vault
  router-first gate only. They do not disable the Fable model guard. The Agent hook blocks when its
  policy cannot be validated for a Fable-sensitive spawn, when the registered user-scope copy is
  stale, or when the hook cannot execute (the Agent handler uses `onFailure: block`). Other
  legacy hooks retain their existing fail-open behavior.
- **Kill switch**: `COST_ROUTER_OFF=1` (also `true`/`yes`/`on`) switches off the efficiency behaviour of every hook: exit 0,
  no stdout, no state writes (`route_state.disabled_by_env()`; used by the benchmark baseline arm so both arms load identical
  CLAUDE.md, project MCP and skills and differ only in the hook layer). It does NOT make the Fable guard inert: a
  Fable-sensitive Agent spawn (fable-consultant, an explicit Fable model, `CLAUDE_CODE_SUBAGENT_MODEL=fable`, `inherit` in a
  Fable session) is still validated and rewritten. `hook_agent_usage.py --report` still works.

<a id="4a1-telemetry"></a>
### 4A.1. Subagent usage telemetry and idempotency

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

<a id="4a2-context-size"></a>
## 4A.2. Context-size switches (environment)

- `COST_ROUTER_REPO_MAP_MAX_CHARS` caps the SessionStart repo map (default 2500, about 600 tokens); the
  largest directories are kept, the rest is summarised as "N smaller directories omitted".
- `COST_ROUTER_NO_REPO_MAP=1`: `hook_repo_map.py` prints nothing.
- The per-prompt route line is compact by default (class, target, model; at most 120 chars). The long form
  (effort, verifier, owner-language reminder, local-LLM hint, enforcement note) needs `COST_ROUTER_VERBOSE_ROUTE=1`.
- Enforcement does not depend on the text: state is persisted either way. `COST_ROUTER_ENFORCE=0` still disables it.
- The compact line ends with ` lang=<code>` when `~/.claude/settings.json` sets `language` (still at most 120 chars).

<a id="4c-escalation-policy"></a>
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
- `COST_ROUTER_OFF=1` stays inert for every hook, including `install.py --session-start`, except the Agent hook's Fable
  guard (see Kill switch); the installed hooks run `python -I`.
- `COST_ROUTER_NO_SELF_INSTALL=1` makes `hook_self_update.py` and `install.py --session-start/--self-update` inert:
  no git, no network, no write under `~/.claude`.
- Hook-driven refresh is OFF by default (owner, 2026-10-22): `install.py --session-start/--self-update` and
  `hook_self_update.py` do nothing unless `COST_ROUTER_SELF_INSTALL=1` or `~/.claude/cost-router.auto-refresh`
  exists (`--uninstall` removes it). The opt-outs above keep priority. HIGH-2 is fixed by the pinned release below.

<a id="pinned-release"></a>
### Pinned release (HIGH-2)

The self-update hook never follows `main`. It re-syncs to the commit named by the INSTALLED `release_pin.json`
(`lib/release_pin.py`): fetch by SHA, verify every byte against the pin, then run the installed `install.py
--self-update --source-root <staging> --release-pin <temp copy of the validated pin> --source-ref <pin commit>`. No
installed pin -> `no-pin`; an install whose `VERSION` line 1 is not the pin commit (e.g. a plain checkout install) ->
`not-pinned-install`; in both cases nothing happens.
Residual: the pin is unsigned and the update only re-syncs to the installed pin; a lower `seq` is refused without `--force`.
Release procedure (owner): 1) commit the change; 2) `python .claude/skills/cost-router/lib/release_pin.py generate --repo . --commit <sha> --seq <n>`
(`seq` above the previous pin; writes `.claude/skills/cost-router/release_pin.json`, which never lists itself);
3) commit the pin in a later commit (that commit changes nothing else) and push both; 4) from that trusted checkout run
`python .claude/skills/cost-router/install.py --source-ref <pinned sha>` on each machine: VERSION records the pinned
commit, so the hook keeps that install in sync (a mismatching `--source-ref` is refused). Without `--source-ref` the
install records the checkout HEAD and the hook leaves it alone (`not-pinned-install`). The pin file is never part of
the commit it pins.

<a id="4b-install-integrity"></a>
## 4B. Install integrity (VERSION and --verify)

Every install (also `--session-start`) writes `VERSION`: line 1 is the source commit (`git rev-parse HEAD`, `+dirty`, or `unknown`; the
self-update ref when given), line 2 the sha256 of the file manifest. `python install.py --verify [--json] [--home <dir>]` is read-only:
it lists missing/extra/differing files and both VERSION lines; exit 0 only when identical, 1 on any difference or a missing VERSION.
It also checks the trust data: `config.json` (pinned vault roots, listed) and `agents.pinned.json` against `pins.sha256.json`
(what install wrote), the privileged agent files in `~/.claude/agents` against the pins, and (with a source) the pins against the
source agents; `--require-hooks` needs the exact command of the installed copy, not a substring.
Residual: `pins.sha256.json` and `config.json` are not authenticated. An attacker who can write `~/.claude` can rewrite
both together (and could replace the hooks themselves anyway), so `--verify` detects accidental drift and project-level
tampering, not a same-user attacker with write access to `~/.claude`.

<a id="4d-specialist-registry"></a>
## 4D. Specialist registry (which agent or skill gets the task)

Small metadata, never frontmatter: `route.meta.json` per skill (shipped under `registry_src/skills/<id>/`, installed next
to the skills) and `roles/<id>.json` per agent. `lib/registry_compile.py` (stdlib, run by `install.py`, never in a hook)
validates schema v1 (unknown key, duplicate id, a trigger that looks like a regex, tools or model that disagree with the
agent file: all errors; `*.bak-*` dirs skipped) and writes `registry.json` (<= 16 KB, deterministic) plus
`registry.hashes.json` (sha256 of every sidecar and artifact; `--verify` checks both).

Trust split. TRUSTED: `registry_src/` and `roles/` (pinned with the release); any error there fails the install.
UNTRUSTED: any other `~/.claude/skills/*/route.meta.json`. A malformed, unknown-key, duplicate-id, oversize (> 4 KB) or
privileged-name sidecar is skipped and listed in the install output and in `--verify`; it never aborts an install or a
self-update. A kept untrusted entry is hint-only and clamped: only id, triggers, task classes and run_in/subagent_type are
used; tier max sonnet, authority none, privacy standard, priority capped at 10 (a trusted entry wins ties); its
acceptance/summary/not_for text is never emitted into hook context; it cannot take a shipped id or name
`fable-consultant`, `vault-reviewer` or any opus/fable agent. For all entries a trigger must be >= 4 characters after
folding and not made only of common RO/EN stopwords (trusted: compile error; untrusted: the trigger is dropped).
Accepted residual: the opt-in SessionStart self-update (off by default) runs `install.py`, which compiles the registry
inside a hook. `--verify` recompiles it. `lib/specialists.py` picks at most one id (plus an overlay) for the prompt by
folded literal trigger match, then task-class fit, then priority; the hook prints ` spec=<id>` and, in the long form,
the specialist's acceptance check. The hint is advisory: prompt text can only select an id of the closed set, never
changes the route state, tier, tools or authority, and a missing or corrupt registry means no hint (fail open, the Fable
guard is separate). Not a gate: the route class and the hooks of section 4A stay binding.

Specialist roles (M10): `dfir-analyst` (Sonnet; Read, Grep, Glob, Bash; privacy local_only, network none, write none:
every finding cites artifact path + sha256 + record id, a missing artifact is UNKNOWN, evidence is never executed) and
`test-author` (Sonnet; adds Write/Edit; authority write tests: `git diff --name-only` lists only test files and the red
run is shown) are agents with a sidecar each; neither is privileged. `repro-debugger` is a BRIEF TEMPLATE, not an
agent: `roles/repro-debugger.json` is kind overlay, run_in subagent, subagent_type `vault-worker`. The compiler accepts
an overlay role only without an agent file of its own, on an existing non-privileged agent role, at most Sonnet, and
with tools and authority within its target's (its artifact hash is the target agent file), privacy and risk_ceiling no
looser than its target's, and no `uses`/`escalates_to` naming a privileged id. The hook of an overlay lead pick names the
agent to spawn: `<overlay-id> (overlay) on <subagent_type>`.

Declared, not enforced: the `dfir-analyst` limits (network none, write none) and the `test-author` tests-only scope are
declarations in the agent file and the sidecar. Both agents have Bash, so nothing in the tools allowlist enforces them;
the allowlist really removes only Write/Edit/Web*/MCP from `dfir-analyst`. A best-effort guard in
`hook_enforce_route.py` (same PreToolUse hook and matcher, keyed on the payload's `agent_type`) denies the obvious
violations: for `dfir-analyst`, Bash that starts a network command (curl, wget, Invoke-WebRequest/iwr,
Invoke-RestMethod/irm, ssh, scp, ftp, nc, `python -m http.server`, pip install, npm, git push/fetch/clone) or writes a
file (`>`/`>>`, Out-File, Set-Content, Add-Content, tee, cp/mv/rm/del/Remove-Item, New-Item); for `test-author`,
Write/Edit/NotebookEdit to a path outside a `20_TESTS`/`tests`/`test` directory or a `test_*.py`, `*_test.py`,
`*Tests.cs` file. Bash is Turing-complete (`python -c`, encoded commands, aliases, a script written elsewhere), so this
is a speed bump and not a sandbox: it fails open on any hook error, and only the explicit denials above are enforced.

<a id="4e-vault-router"></a>
## 4E. Vault Agent Router on demand (Opus as main advisor)

Opus as main advisor runs `python -I <installed cost-router>/lib/vault_router.py route --goal "<text>" [--json]` when it
wants the Agent Router's decision, from any project. The vault root is the first root pinned in the installed
`config.json` that still passes the M2 link/realpath check, never the cwd or the project; nothing configured is exit 3.
It runs the vault's `routing.route_cli route` (argv list, no shell, cwd = pinned root, 20 s timeout; `-I` ignores
PYTHONPATH, so the child puts the pinned packages dir on `sys.path` itself) and prints status, primary agent, verifier and
`execute_via`. Exit 0 ROUTED/PLANNED, 4 BLOCKED, 5 timeout or crash. The goal is one argv element and is never logged. A
project-local `routing/route_cli.py` is never executed. `hook_enforce_route` counts this call as a router receipt, like
`python -m routing.route_cli route|dispatch`. The automatic in-hook router gate stays inside pinned vault roots only;
outside them nothing calls the router unless the advisor runs the wrapper. On timeout the wrapper kills the whole
process tree (Windows `taskkill /T /F`, POSIX process group) and prints each field as one control-character-free line.

<a id="4f-acceptance-receipts"></a>
### 4F. Acceptance receipts (`lib/check.py`) and the `check-runner` agent

`python -I <S>/lib/check.py run [--session <id>] --label <name> -- <command...>` runs the command (argv list, no shell),
keeps the exit code, duration, last 40 output lines and the sha256 of the command line in
`~/.claude/cache/cost-router/checks/<session-or-pid>-<epoch>.json`, and prints `check <label>: exit=<n> pass|fail
(<pytest or dotnet summary>)`. `check.py last [--label]` prints the newest receipt. The `check-runner` agent (Haiku;
Read, Grep, Glob, Bash; sidecar `roles/check-runner.json`, write none) runs one given check this way and quotes the
receipt line and the failing lines verbatim, never interpreting causes. When the Agent hook escalates a spawn one tier it
appends `after failed check <label> exit=<n>` if the session (matched by `--session`) has a failing receipt: informational
only. Receipts are forgeable by the main session and never grant authority or relax any gate. A `.cmd`/`.bat` target
is started by cmd.exe, which re-parses the command line: `check.py` refuses (exit 2) when any argument holds `& | < > ^ %`
a double quote or a newline. `--session` must match `[A-Za-z0-9_-]{1,64}` (exit 2 otherwise); a bare command name is
resolved on PATH without the cwd (Windows: a name found only in the cwd is refused, exit 2); `last` and the escalation note
never read a receipt file over 256 KB. Residual: the list is a denylist (a `!` for delayed expansion is not refused) and a shim that evaluates its own arguments stays the caller's risk; run untrusted shims through an `.exe`.
`--tier sonnet|opus` (any other value: exit 2) records the tier of the attempt whose work is checked; every receipt also
records the session's current prompt epoch (`prompt_epoch`, from the route state; null without one).

<a id="4g-fable-executor"></a>
### 4G. `fable-executor` (M11; shipped ENABLED per owner decision 2026-11-23, Opus decides)

`.claude/agents/fable-executor.md`: Fable 5.2 (`claude-fable-5-2`), tools exactly Read, Grep, Glob, Bash, Edit, Write
(Agent/Task, MCP, WebFetch/WebSearch, NotebookEdit denied), checked by its own exact validator
(`_valid_fable_executor_frontmatter`; the consultant validator is unchanged), pinned in `agents.pinned.json`, privileged
(an edited copy is backed up and replaced at install; a project file claiming the name blocks). Sidecar
`roles/fable-executor.json` has no triggers and `selectable: false`: text never selects it. Owner rule: Haiku/Sonnet
execute, Opus advises and reviews, Opus or Fable execute only after the cheaper tiers failed.
The Agent hook allows a `fable-executor` spawn only when ALL hold, else exit 2 naming the condition:
(a) `rules.fable_executor.enabled` is `true` in the INSTALLED `~/.claude/skills/cost-router/policy.json` whose sha256
equals the one install.py recorded in `pins.sha256.json` (never the project copy, an env var, prompt text or a user-scope
sidecar; `max_per_session` int 1..10); (b) the session model is Opus (payload/transcript/stored/env, never a settings
guess; not Fable); (c) the definition matches its pin and the validator, no project file claims the name, no explicit
non-Fable model; (d) this session and prompt epoch hold a FAILED receipt of the same check label from `--tier sonnet` and
from `--tier opus`, and the newest receipt of that label still fails, AND the Agent hook itself recorded a real allowed
spawn of a writing (not read-only, not reviewer) agent resolved to sonnet and one resolved to opus in that epoch
(`route-<sid>.spawns`, appended under the resume-counter lock; receipts alone never count); (e) fewer than `max_per_session` executor spawns this
session (counter `route-<sid>.fable_executor` under the same lock as the resume counter); (f) no critical risk (brief or
prompt route) and no live-operation stem (`rules.implementer_risk_floor.never_for_stems` plus the hook's
`EXECUTOR_LIVE_PATTERNS`: push, apply, drop, upload, ship, release, rollout, migrate, truncate, `rm -rf`, force-push;
deploy/rotate/publish match inside compounds such as autodeploy/redeploy), matched on the brief after
`specialists.normalize/fold` (NFKC, Cf/zero-width removed, cedilla ş/ţ -> ș/ț, diacritics folded, casefold, whitespace
collapsed); (g) no local_only specialist (dfir-analyst) recorded for the prompt or triggered by the WHOLE brief (same
normalization plus the `specialists.CONFUSABLES` look-alike fold, also used by (f)), and the installed registry is readable;
the prompt hook records the local_only scan on every path, `#direct` override, router error and short/slash/trivial
prompts included (a short prompt keeps its route but starts a new prompt epoch); (h)
`COST_ROUTER_OFF` bypasses none of it (without a prompt state, (d) fails); (i) heavy task only: the CURRENT owner
prompt's route state (hook_prompt_route, from the owner's own text, never the brief) has class implement, design or frontier
and is not a short follow-up, trivial or slash prompt (`followup` marker); explore/summarize/review/unrouted/#direct-short
are denied. When to spawn is the Opus main session's decision: never for easy tasks, only on a heavy task after a sonnet
and an opus attempt failed the same check ((d) is that proof); the Opus vault-reviewer pass afterwards stays mandatory. On allow, the context says an Opus
vault-reviewer pass on the executor's diff is mandatory before DONE. `CLAUDE_CODE_SUBAGENT_MODEL=fable` still denies every
spawn except the pinned consultant and the gated executor; any other Fable request keeps being rewritten to Opus.
Flag state: Opus decides; the flag is shipped ENABLED (`"enabled": true`, `max_per_session` 2) per the owner decision
of 2026-11-23 ("there is no owner switch; Opus decides when Fable is most needed; never for easy tasks; for a heavy
task with a lot to run Opus may choose Fable if neither Opus nor Sonnet can do it"). Gates (b)-(i) and the tool-level
guard below still apply to every spawn. To change it (no env switch): in a trusted checkout set `"enabled"` under
`rules.fable_executor` in `04_CONFIG/claude_model_routing.json`, commit, generate the next release pin for that commit
(`python .claude/skills/cost-router/lib/release_pin.py generate --repo . --commit <sha> --seq <n+1> --out <pin>`, commit the
pin), then re-run `python .claude/skills/cost-router/install.py --source-ref <sha> --release-pin <pin>` and
`install.py --verify --require-hooks` (exit 0, `policy_ok` true). Editing the installed `policy.json` by hand changes its
hash, disables the executor (gate (a) fails) and fails `--verify`. Disable: the same steps with `false`.
Threat residuals: receipts, route state, the spawn record `route-<sid>.spawns` and `pins.sha256.json` live in the user
cache and are writable by the same user, so a same-user process (or a hostile main session with Bash) can still forge the
ladder by editing these files; the spawn record only raises the cost of a forgery from one check.py call to editing JSON
by hand. The gate limits induced overspend; it does not stop a hostile Opus main session (E1). The stem and trigger lists
are denylists.

**Tool-level guard (M12, the real boundary).** The (f)/(g) brief checks are only an extra signal: a brief is
attacker-shaped text (look-alike letters, words split by markdown or newlines, other languages). `hook_enforce_route.py`
(`executor_guard`) decides on what an `agent_type` `fable-executor` actually RUNS, before COST_ROUTER_OFF,
COST_ROUTER_ENFORCE=0, `#direct` or shadowing are looked at (X7), and fails CLOSED on any internal error. Threat model
(the reviewer gets the same list): X1 git push/reset --hard/clean/branch -D/checkout -- ./restore/rebase/filter-branch/
tag -d/remote, aliases; X2 network clients and package install/publish, gh, docker; X3 deploy/publish/release CLIs; X4
recursive or out-of-worktree deletes and writes; X5 obfuscation (encoded commands, `-c`/`-e` wrappers, env-var indirection,
substitution, here-strings, continuation, look-alikes, zero-width, quote splitting); X6 Write/Edit/NotebookEdit outside the
worktree; X7 the kill switches. **Bash ALLOWLIST**: the command must be unchanged by NFKC + Cf removal + confusables fold,
ASCII, <= 2000 chars, ONE simple command under Git Bash quoting (no unquoted `; & | < > ( ) { } $ backtick ! @`, no `$`
or backtick even in double quotes, no newline, comment, line continuation, env assignment, program path or `~`), and one of:
`pytest`/`python -m pytest` (M12 review: never `--pastebin`, `-p <plugin>` (only `-p no:<name>`), or `-o`/
`--override-ini` touching addopts or plugins, abbreviations included; `o` or `p` bundled after other short flags such as `-qo addopts=...` or `-xp evil` is denied, the scan stops at value options `-k -m -r -W`); `python|python3|py [-3] [-I -B -u -E -s -S -O -q, -X utf8|dev] <script inside the worktree>`;
`node [--test] <script inside the worktree>`; `dotnet build|test --no-restore` (`dotnet restore` and an implicit
restore are denied; options only from the allowlist `--no-restore --no-build --nologo -c|--configuration <name> -f|--framework <tfm> -v|--verbosity <level> --filter <expr> --logger console|trx`, so every MSBuild switch `-p:`/`/p:`/ `-property:`, `-t:`/`-target:`, `-restore`, `--interactive`, `--source`, `--output` and `@file` is denied; at most one project/solution argument, resolved from cwd and refused outside the worktree, junctions included); `npm|pnpm|yarn test | run test|build|lint [-- args]`;
`git [--no-pager] [-C <dir in the worktree>] status|diff|log|show|blame|rev-parse|ls-files|add|commit` (never `--amend`,
`--output`, `--ext-diff`, `--textconv`, `--exec`, `--upload-pack`, `--receive-pack`, `--config[-env]`, `--force*`,
`--hard`, `--delete`, `--mirror`, `-c`, `--git-dir`, nor any abbreviation of them of 2+ characters such as `--am`), `git switch -c|checkout -b <new branch>`, `git stash list`,
`git branch` listing; read-only `ls dir cat type Get-Content Select-String rg grep find head tail wc pwd` (no `find
-exec/-delete/-fprint`, no `rg --pre`). Every path-like argument of an executing command (`--basetemp`, `--rootdir`, node
ids, `-o`) must resolve inside the worktree, and for pytest/python so must every bare word naming an existing entry
(a junction `tests` -> elsewhere is refused); unquoted globs are refused there. Anything else is denied naming the rule.
**Writes**: the realpath of the target must be inside `git rev-parse --show-toplevel` of the session cwd (else no write
at all), lexically too, not in `.git` (also `.GIT`, `.git.`), not through a symlink/junction/reparse point, not a hard
link, not in a nested repository, not in `~/.claude`, no UNC/device/drive-relative path, no `:` stream, and never
agent or repository configuration (case-insensitive, after realpath, at any depth): `.claude/**`, `.agents/**`,
`CLAUDE.md`, `CLAUDE.local.md`, `AGENTS.md`, `GEMINI.md`, `.mcp.json`, `.gitattributes`, `.gitmodules`, `.gitconfig`, `.pre-commit-config.yaml`, `.gemini/**`, `.codex/**`, `.vscode/**`, `.husky/**`, `.idea/**`, `.devcontainer/**`, and `<root>/.github/workflows/**` (`.editorconfig` stays writable). **Install chain (finding 1)**: `install.py` refuses (exit 2, nothing changed) unless
every file it copies matches the sha256 in the release pin (`--release-pin`, else the source tree's
`release_pin.json`; no pin = refused). F3 residual: the source tree's own `release_pin.json` is unsigned and trusted; the pin protects against an Edit/Write tamper of the release files, not against finding-1 code (a script or test the executor wrote and ran) that rewrites both the files and the pin; install from a ref the Opus reviewer checked (`--source-ref <sha> --release-pin <pin>` from that commit). Residuals: the
CONTENTS of an allowed worktree script, test/conftest file, package.json script, project file, or a tracked hooks dir that
the repo's own `core.hooksPath` already points to are not inspected (the executor may write them, then run them through
an allowed command); F6: reads outside the worktree stay allowed and, under the accepted finding 1, code the executor writes and runs can reach the network, so the exfiltration risk is the same as for vault-worker (the guard blocks network CLIs, not the network itself); a same-user process outside Claude Code is out of scope. Finding-1 residual (accepted with the flag on): an executor can run code it writes inside the
worktree (a test, conftest or script, then `pytest`/`python`), the same as vault-worker; the guard bounds where it writes
and what it calls, not what that code does.


<a id="token-lean-defaults"></a>
## Token-lean defaults (phase 3) and their env switches
- Read/Grep/Glob are never denied by the enforce hook (a deny costs a retry request that re-reads the prefix); the delegate reason is delivered once as context. Edit/Write/NotebookEdit/non-exempt Bash are still denied. `COST_ROUTER_DENY_READS=1` restores the deny.
- The route line prints only when (class, model, subagent) changes, on risk >= high, or with `COST_ROUTER_VERBOSE_ROUTE=1` (long form, always). State is written every prompt either way.
- The repo map is injected on `startup` only (`COST_ROUTER_REPO_MAP=always`: also clear/compact); compact gets a one-line state. Cap 1500 chars (`COST_ROUTER_REPO_MAP_MAX_CHARS`).
- The Agent hook emits no allow-path context (only the denial reason, or `COST_ROUTER_VERBOSE_ROUTE=1`); read-only agent types (strict allowlist: a project `.claude/agents/<type>.md` whose frontmatter `tools` are all in Read, Grep, Glob, LS, WebFetch, WebSearch or vault-memory read MCP tools; the built-in Explore without such a file, or the project Explore with Bash, is NOT read-only) are clamped to the explore class (haiku, cap one tier above) on non-risky, non-review/design briefs only (review/verify/design or risk >= high keep the old floor); `COST_ROUTER_READONLY_FLOOR=1` restores the old floor. The subagent `prompt` is never modified.
- `COST_ROUTER_OFF=1` makes every hook inert (the Fable guard of the Agent hook excepted). Evidence: the phase-3 benchmark dir under `07_EVALUATION/reports/` (to be produced).
