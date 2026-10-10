# Claude Model Routing Policy V1 — which Claude model runs which work, and why

**Status:** REAL, TEST_VERIFIED (`20_TESTS/test_claude_model_router.py`), not wired into any
automatic hook. The policy is advisory: Claude Code applies it through subagent frontmatter
(`.claude/agents/*.md`), the Agent tool's `model`/`effort` parameters and the `/model`,
`/effort` commands. Nothing here calls a provider.

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
- **security work gets an independent verifier** one tier up (Opus worker → Fable main session;
  for subagents, a fresh Opus context);
- **escalation order on failure**: haiku → sonnet → opus → fable, one notch, after the tests
  say the cheaper attempt failed;
- **subagent output contract**: `decision / evidence / risks / unknowns / confidence /
  recommended_action`, under 300 words, so the main session's context stays small.

## 4. How to use it

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
- No hook applies this automatically. A `UserPromptSubmit` hook printing the `route` line is a
  possible next step and is left opt-in because it adds tokens to every prompt.
- The usage counterfactual assumes equal token counts across models; a cheaper model that
  retries is not cheaper. Judge per completed task.
- Distinct from `providers/model_tier_router.py` (council tiers, protected core) and
  `routing/agent_router.py` (external runtimes); neither is changed.
