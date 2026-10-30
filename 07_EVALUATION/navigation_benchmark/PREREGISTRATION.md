---
id: "0e7b6a52-9f1c-4f7e-a3d8-5c2b1e9d4a10"
title: "Pre-registration: vault navigation benchmark (direct routes vs grep vs flat index)"
type: "decision"
lifecycle: "REVIEW"
created: "2026-10-30T17:10:00Z"
updated: "2026-10-30T17:10:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "07_EVALUATION/navigation_benchmark/PREREGISTRATION.md"
  created_by: "claude-code"
tags:
  - "evaluation"
  - "navigation"
  - "preregistration"
---

# Vault navigation benchmark — pre-registration

Written before any run. A change to the gold set, the conditions, the grader or a threshold after results are seen
makes the run exploratory, and the report says so.

## Question

Does the vault's own navigation layer (direct `vault://` routes over `vault_access`, with the per-note coverage gate)
let an agent find a specific fact more often, or as often with less context, than the two patterns most published
setups use: plain search over the files, and a flat index of every note (the `llms.txt` / "LLM wiki index" pattern)?

## Gold set

24 questions (`gold_set.json`), each answered by one exact fact in exactly one routed note. Drafted by a Claude
(Opus) subagent from the repository, then checked by a model of another family (Codex, read-only): the fact exists in
the stated file, occurs in no other tracked file, and the question does not leak the file name. Items it rejects are
fixed or dropped before the run and the count is reported. The owner checks a 20% random sample (fixed seed).
Planned deviation from the original plan: the gold set was not drafted by Fable (Fable runs only as a main session).

## Conditions (same model, prompt, tools for reading files, turn and budget limits)

The subject is `claude -p --model sonnet`, max 20 turns, run in a detached checkout of
`claude/route-coverage-gate` from which the agent contracts (`AGENTS.md`, `CLAUDE.md`, `GEMINI.md`), `.claude/` and
`.mcp.json` are removed, so no condition gets the vault's written guidance and no project hook runs.

- **G — search**: Read, Grep, Glob.
- **I — flat index**: as G, plus `INDEX.md` at the root listing every routed note as `path — title` (generated from
  the route table; the agent is told it exists).
- **V — vault routes**: as G, plus the `vault-memory` MCP server (`vault_resolve`, `vault_list`, `vault_read`,
  `vault_search`, `vault_get_metadata`), principal `cloud_cli.claude_code`; the agent is told the tools exist.

Every answer ends with `ANSWER_PATH: <repo-relative path or vault:// URI>` and `ANSWER: <the fact>`.

## Runs and grading

24 questions × 3 conditions × 2 repeats = 144 runs, all repeat-0 runs before any repeat-1 run, order shuffled with a
fixed seed. If a usage limit stops the run, the report covers the complete repeats only.

Deterministic grading: **correct** = the answer path (a `vault://` URI is mapped to its file through the route
table) is the gold file AND the gold `fact_regex` matches the `ANSWER:` line. Also reported: path-only correct,
tokens (input + cache read + cache write + output), turns, cost estimate.

## Decision rule

- **V beats the baselines** if V's accuracy is at least **10 points** above the better of G and I, with a paired
  sign test over the 24 questions (per-question mean over repeats) p ≤ 0.05; **or** V's accuracy is no more than
  **5 points** below the better baseline and V's median tokens per run are at least **30%** lower than that
  baseline's.
- **V is worse** if its accuracy is more than 5 points below the better baseline and it does not use 30% fewer tokens.
- Anything else: **no demonstrated difference**, reported with the numbers.
- If every condition scores at least 95%, the questions are too easy: "benchmark not discriminating".

## Known limits

- One model; one repository (this vault); questions drafted by a Claude model.
- `vault_search` reaches the memory controller; its corpus can differ from the files on disk (lifecycle filters).
- "Anything online" is approximated by the two dominant published patterns, not by every product.
