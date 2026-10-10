# Claude Model Routing Policy V1 — which Claude model runs which work, and why

**Status:** REAL, TEST_VERIFIED (`20_TESTS/test_claude_model_router.py`,
`20_TESTS/test_cost_router_skill.py`). Applied in Claude Code by the **`cost-router` skill**
(`.claude/skills/cost-router/SKILL.md`): the skill carries the protocol, a `UserPromptSubmit` hook
injects one route line per non-trivial prompt, and the subagents in `.claude/agents/` pin the
cheaper models. Nothing here calls a provider.

**Owner question answered:** "Which model should Claude Code use for what, so the session
spends fewer tokens-dollars without losing the result?"

## 1. Rate card (Anthropic first-party API, as of 2026-10-06; `04_CONFIG/claude_model_routing.json`)

| Alias | Model | $/MTok in | $/MTok out | cache read | relative to Opus |
|---|---|---|---|---|---|
| `fable` | Claude Fable 5.1 | 10.00 | 50.00 | 0.25 | 2.5x |
| `opus` | Claude Opus 5.5 (Claude Code default) | 4.00 | 20.00 | 0.20 | 1x |
| `sonnet` | Claude Sonnet 5.5 | 2.00 | 10.00 | 0.20 | 0.5x |
| `haiku` | Claude Haiku 5.5 (>100K-token prompts cost 5x more) | 0.10 | 0.50 | 0.01 | 0.025x |

Fable usage can bill to usage credits instead of the plan's included limits; Claude Code asks
for consent before that happens. Every current model has thinking on; `effort`
(`low`..`max`) is the only depth control. Opus 5.5, Sonnet 5.5 and Haiku 5.5 default to
`medium`; Fable to `high`.

## 2. What the research says (claude-api skill, cost-optimization guide; Claude Code docs)

1. **Caching is the biggest lever and it is free.** Every turn resends the whole conversation;
   a stable system prompt and tool list keep it priced at the cache-read rate. Changing the
   model, the top-level effort or the tool set mid-session cold-starts the cache. In the session
   that built this, 91% of input was served from cache (§5).
2. **Lower effort on the same model before changing the model.** Research-style work is nearly
   flat across effort levels; long-horizon coding loses a few points per step down for a large
   cost saving. Sweep effort first, switch models between tasks, never inside one.
3. **Model choice is the last lever, and it is priced per completed task, not per token.** A
   cheaper model that needs retries is not cheaper. The published winners are two shapes only:
   an **orchestrator** (frontier model plans, cheap workers do bulk independent pieces) and
   an **advisor** (cheap executor, frontier consult on hard decisions). A single dependent chain
   that fits one context is cheapest on one model at lower effort.
4. **Re-run failures at higher effort/tier.** Run everything cheap, re-run only what the tests
   reject; this held the pass rate at roughly half the cost in Anthropic's coding runs.
5. **Subagents are the mechanism in Claude Code.** A subagent file pins `model:` and `effort:`;
   a project agent named `Explore` overrides the built-in explorer and keeps its own model; the
   built-in `claude-code-guide` already runs on Haiku. Subagent requests count against the same
   usage limits.

## 3. The routing table (`task_classes` in the policy file)

| Class | Model / effort | Runs as | Use for | Why |
|---|---|---|---|---|
| `explore` | haiku / low | `Explore` subagent | where is X, who imports Y, list every Z | answer is a location, not a judgment |
| `summarize` | haiku / low | `Explore` subagent | summarize, extract, classify, convert | checkable mechanical output |
| `implement` | sonnet / medium | `vault-worker` subagent | spec'd change with tests as the failure signal | run cheap first, escalate failures |
| `review` | opus / high | `vault-reviewer` subagent | verify DONE claims, review diffs, security review | edge-case reasoning; independent context |
| `design` | opus / xhigh | main session | architecture, root cause, multi-file plans | a wrong plan costs more than tokens |
| `frontier` | fable / high | main session only | ambiguous, long-horizon, end-to-end orchestration | the one place Fable earns 2.5x |
| unclassified | opus / medium (Claude Code default) | — | anything the keywords miss | reported as `confidence: low`, never guessed cheaper |

Rules applied after classification (`rules` in the policy; can only raise a tier):
- **risk floor**: `high`/`critical` risk (production, credentials, deletes, security boundary)
  never below Opus;
- **Fable is never a subagent model**; a subagent route that would escalate to Fable hands the
  step back to the main session;
- **risk is read from the task text** (`rules.risk_keywords`, English and Romanian: production,
  credentials, secrets, auth, trust boundaries, deletes, git history, ...); a detected `high`/
  `critical` risk raises the route to Opus at `high` effort, keeps it in the main session (the
  cheaper-model subagents are not used) and names an independent verifier;
- **the verifier is always `vault-reviewer` (Opus, fresh context)**, for every high/critical or
  security task that is not itself a review; Fable is never a verifier;
- **escalation order on failure**: haiku → sonnet → opus → fable, one notch, after the tests
  say the cheaper attempt failed;
- **subagent output contract**: `decision / evidence / risks / unknowns / confidence /
  recommended_action`, under 300 words, so the main session's context stays small.

## 3A. Enforcement and the local tier (2026-10-10, owner-approved)

- **Any subagent type, the model per route.** A `PreToolUse` hook on `Agent`
  (`.claude/skills/cost-router/hook_agent_model.py`) classifies each spawn's brief and rewrites its
  `model`: none given → the route's model; cheaper than the route → raised; more than one tier
  above → capped (one-notch escalation stays allowed); risk detected → Opus; Fable → Opus. An
  unclassified brief without risk is left to the agent's own frontmatter. The hook answers `allow`
  with `updatedInput`, as the hooks reference requires; deny/ask rules still apply. It never blocks.
- **One route line per prompt.** A project copy of a hook stays silent when the user-scope install
  of the same hook is registered (`is_shadowed` in `lib/route.py`).
- **Local tier.** `lib/local_llm.py` sends text-in/short-text-out work to Ollama on loopback
  (`local_llm` in the policy: `qwen2.5:7b-instruct` / `mistral:7b-instruct` for text,
  `qwen2.5-coder:7b` / `qwen2.5-coder:3b` for code; measured on the owner's PC, RTX 5060 8 GB,
  3.6–7.4 s cold). Files are read by the local model, never by Claude; input over
  `max_input_chars` is refused, not truncated; exit 3 = unavailable → Haiku subagent. The prompt hook
  advertises it for `summarize` only when a configured model answers on 127.0.0.1:11434. Never used
  for multi-step code, design, risky work or verification; unreachable from cloud sessions.

## 3B. Context economy in every project (2026-10-10, owner-approved)

Sessions spent 300–400k tokens exploring repositories (owner report, not measured here). The user
install now adds, for every project on the machine (and every cloud session via the bootstrap):
- **Repository map** — `SessionStart` hook `hook_repo_map.py` (matcher `startup|clear|compact`)
  injects `lib/repo_map.py`'s map of the current git repository: root files, test command, each
  top-level directory with file count, size, main types, README line and largest subdirectories;
  ⚠ marks directories over 300 files or 50 MB. Built from `git ls-files` and file sizes only,
  capped at 4,000 characters (about 1k tokens), cached in `~/.claude/cache/repo-map/` by path and
  HEAD (this repository, 17,562 files: 0.35 s uncached). Silent outside git; never blocks.
- **Reading rules** — a marked block in `~/.claude/CLAUDE.md` (rest of the file untouched): use the
  map, grep narrowly, no wholesale reads of ⚠ directories, one task per session with `/clear` from
  a checkpoint.
- **Read deny rules** in `~/.claude/settings.json` for pure tool caches only (`__pycache__`,
  `.pytest_cache`, `.mypy_cache`, `.ruff_cache`, `.git/objects`). Dependency and build trees
  (node_modules, virtualenvs, dist) stay readable: a deny beats any project allow, and debugging
  needs them. The rules actually added are recorded in `~/.claude/cost-router.deny-added.json`.
`--no-context` skips the last two; `--uninstall` removes exactly what was added. Tests:
`20_TESTS/test_cost_router_repo_map.py`.

## 4. How to use it

**Install once, every project on the machine** (user scope, idempotent, reversible):

```text
python3 .claude/skills/cost-router/install.py            # skill + agents + prompt hook into ~/.claude
python3 .claude/skills/cost-router/install.py --no-hook  # without the per-prompt hint
python3 .claude/skills/cost-router/install.py --uninstall
```

Inside this repository nothing needs installing: the project skill, agents and the hook in
`.claude/settings.json` are versioned, and a `SessionStart` hook (startup and resume) runs
`install.py --session-start` so the skill and agents also land in the session's user scope
(`~/.claude`), which is what an ephemeral cloud container needs. It adds no user-scope hook and it
does nothing after an `--uninstall` on that machine (marker `~/.claude/cost-router.disabled`; an
explicit `install.py` clears it). The installer refuses a user `settings.json` it cannot parse as
strict JSON before copying anything. Hook commands are `bash` lines: on Windows they need Claude
Code's Git Bash (unverified here). Another repository, or every cloud session at once, gets the same with the **bootstrap** below: a
sparse clone of just the skill's files (9 files, about 2 seconds, measured 2026-10-10 from a cloud
container), the installer, then cleanup. It never touches the current project's git state.

```bash
d=$(mktemp -d) && git clone -q --filter=blob:none --sparse --depth 1 https://github.com/userist123/AI_Memory_Vault_CODEX_READY "$d" && git -C "$d" sparse-checkout set --no-cone .claude/skills/cost-router .claude/agents 03_IMPLEMENTATION/packages/routing/claude_model_router.py 04_CONFIG/claude_model_routing.json >/dev/null 2>&1 && for py in python3 python; do if "$py" -c "import sys" >/dev/null 2>&1; then "$py" "$d/.claude/skills/cost-router/install.py"; break; fi; done; rm -rf "$d"; exit 0
```

The patterns carry no leading `/`: Git Bash (MSYS) rewrites an argument that starts with `/` into a
Windows path, so `/03_IMPLEMENTATION/...` never reached git and `install.py` stopped on "missing
source" (seen on the owner's PC, 2026-10-10). A slash inside the pattern anchors it to the root
just the same (gitignore rules; git prints a harmless warning, discarded). The `python3`/`python`
loop covers Windows, where `python3` may be the Store stub.

- **Every cloud session, any repository:** paste that line into the cloud environment's *Setup
  script* (session title bar, cloud environment menu, Edit). New sessions run it at start.
- **One other repository:** put it in that repo's `.claude/settings.json` as a `SessionStart`
  hook (`"matcher": "startup|resume"`, `"timeout": 60`).
- **A personal computer:** run it once in any shell (Git Bash on Windows); it is the same as
  running `install.py` from a clone. `/cost-router` invokes the skill by hand; Claude also loads
it on its own from the description. The hook adds about 60 tokens per prompt and is silent on
trivial prompts (slash commands, yes/no, under four words); it can never block a prompt.

Manual tools:

```text
python -m routing.claude_model_cli policy                       # the table above, from the JSON
python -m routing.claude_model_cli route --goal "<task>" [--risk high] [--subagent] [--input-tokens N]
python -m routing.claude_model_cli report [--project-dir .] [--all-projects] [--json]
```
(run from `03_IMPLEMENTATION/packages`, or with `PYTHONPATH=03_IMPLEMENTATION/packages`).

`route` prints class, model, effort, subagent, verifier, escalation target, the estimated cost
and what the same tokens cost on Fable. `report` reads the local Claude Code transcripts
(`~/.claude/projects/<slug>/*.jsonl`, de-duplicated by `requestId`) and prices real usage per
model, with the counterfactual cost on every other model (equal-token assumption: an upper
bound on savings).

The project `.claude/settings.json` sets `"model": "opus"`, so a session on this repository starts
on Opus 5.5 unless the owner overrides it (`/model`, `--model` and `ANTHROPIC_MODEL` all rank
higher): the main-session model is the largest single cost lever (Fable is 2.5x Opus per token)
and the skill never changes it. Pick Fable deliberately, for the ambiguous long-horizon tail.

In a session, the main agent (whatever model the owner picked with `/model`) does the
classification itself with this table and delegates: `Agent(subagent_type="Explore")` for
reads, `vault-worker` for bounded changes, `vault-reviewer` before claiming DONE. The main
session keeps design and orchestration. Defaults that keep the bill down regardless of routing:
`/effort medium` on Opus for routine work, `xhigh` only for the hard tail; stable tools and
system prompt within a session; excerpts, not whole files (CLAUDE.md token-economy rule).

## 5. Evidence

- Unit tests: `20_TESTS/test_claude_model_router.py` (policy validity, classification, risk
  floor, Fable-never-subagent, verifier independence, rate-card math, transcript de-duplication,
  subagent files consistent with the policy, CLI).
- Measured on the session that built this (2026-10-10, Fable 5.1 main session, after 11
  requests):

```text
model                  req     input   cache_rd   cache_wr   output   think  hit%      usd
claude-fable-5-1        11       292    1807494     171175    33964    5526   91%     5.58
                    same tokens on: fable $5.58, opus $2.41, sonnet $1.39, haiku $0.07
```

  Reading: with the prefix cached, the bill is dominated by cache reads of the 1.8M resent
  tokens and by output. The same token volume on Opus would have cost 43%, which is the
  argument for running routine sessions on Opus at `medium` and reserving Fable for the
  ambiguous tail; the exploration reads inside this session (several hundred KB of files) are
  what the `Explore` subagent now takes off the main model's bill.

## 6. Limits (honest)

- Keyword classification is heuristic. It is meant to make the policy explicit and testable,
  not to replace judgment; the main agent is the classifier in practice.
- Prices drift; `pricing_as_of` is in the file and must be refreshed with the rate card.
- The hook only *nudges* (one line of context); the skill body loads when invoked and stays in
  context for the session (about 1.2K tokens), which is the price of having the protocol present.
  The main agent still makes the call; a subagent is only as good as the brief it gets.
- The usage counterfactual assumes equal token counts across models; a cheaper model that
  retries is not cheaper. Judge per completed task.
- Distinct from `providers/model_tier_router.py` (council tiers, protected core) and
  `routing/agent_router.py` (external runtimes); neither is changed.
