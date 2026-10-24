---
name: cost-router
description: >-
  Route every Antigravity task and subagent to the cheapest model and context size that preserves
  correctness, and enforce token hygiene. Use before starting any non-trivial task, before
  spawning a subagent with `invoke_subagent`, when choosing between `flash`, `pro`, `inherit` or a
  local LLM, or when a session is growing expensive.
---

# Cost Router — Right Model, Smallest Context, Verified Output (Antigravity Edition)

You are the entrepreneur: you decide what is worth paying for, delegate bounded work to cheaper
workers in fresh contexts, and check their receipt before signing off. Your conversation context is
the most expensive memory in the system — every line you pull into it is paid for again on every
turn after. Keep it small.

## 0. Inspect before you pay (what is on this machine right now)

1. **In your own session** (zero cost): Available subagents (`research`, `compiler_and_tooling_engineer`,
   `backend_systems_engineer`, `frontend_saas_engineer`, `web_design_engineer_agent`,
   `secops_auditor`, `system_architecture_agent`, `web_quality_engineer`, `self`), skills, and MCP
   tools (`vault-memory`). Read that list before searching the disk for tools.
2. **Local models (free, private)**: probe once per task when the work is `summarize`, `classify`,
   `extract` or boilerplate:
   ```bash
   python ~/.gemini/config/skills/cost-router/lib/local_llm.py --probe
   ```
   If `reachable: true`, run the text through `local_llm.py --file <path> "<instruction>"`. If it
   exits `3` (no local runtime), do not retry in this session — move up one rung.
3. **One-line route decision**:
   ```bash
   python ~/.gemini/config/skills/cost-router/lib/route.py "<one-line task>" [--risk high] [--subagent]
   ```

## 1. The routing ladder (cheapest sufficient tier wins; never skip verification)

| Task class | Examples | Where it runs | Antigravity Model | Effort |
|---|---|---|---|---|
| `mechanical` | format, rename, sort imports, regex replace | deterministic script (`python`, `git`) — **no LLM** | — | — |
| `summarize` | summarize a file/log/diff, extract fields, draft commit message | `local_llm.py` if reachable, else `research` subagent | local / `flash_lite` | low |
| `explore` | find where X is defined, list callers, trace a symbol | `invoke_subagent` with `TypeName="research"` (read-only) | `flash` | low |
| `implement` | write/edit 1–3 known files from a clear spec, add unit tests | `invoke_subagent` with domain specialist (`compiler_and_tooling_engineer`, `backend_systems_engineer`, `frontend_saas_engineer`, `web_design_engineer_agent`, `self`) | `flash` | medium |
| `review` | code/security review, compliance check, regression audit | `invoke_subagent` with reviewer (`secops_auditor`, `system_architecture_agent`, `web_quality_engineer`, `research`) in fresh context | `flash` | high |
| `design` | multi-module architecture, ambiguous root cause, security/crypto invariants | main session | main chat model | high |
| `frontier` | novel cross-system synthesis where standard tier failed with a concrete error | main session | main chat model | high |

**Hard rule — Subagents never use the main chat model (`Model != "inherit"` and `Model != "pro"`):**
- Every subagent invoked via `invoke_subagent` MUST run on a different, lighter model (`"flash"` or
  `"flash_lite"`) than the main chat model. `hook_antigravity.py` automatically rewrites `"inherit"`
  or `"pro"` on subagents to `"flash"` / `"flash_lite"`.
- High/critical design, final verification sign-off, and live operations stay in the main session.

## 2. Subagent dispatch rules (why subagents save 5–20×)

A subagent starts with a **near-zero context** and returns a short summary; the 30 files it read die
with its context instead of staying in yours for the next 20 turns.

- **Offload to `research` (`Model="flash"`)**: any search across more than 2 files, any "where is /
  what calls / list all" question. Ask it to return: `file:line`, 1-line finding, ≤ 30 lines total.
- **Offload to a domain specialist (`Model="pro"`)**: any self-contained edit where you already know
  the files and the acceptance command. Give it:
  1. exact goal (1–2 sentences),
  2. exact files to read/edit (and what **not** to touch),
  3. the one command that proves it works (e.g. `pytest 20_TESTS/test_x.py -q`),
  4. output contract: changed files, test command + exit code, ≤ 25 lines.
- **Offload to a reviewer (`secops_auditor`, `system_architecture_agent`, `web_quality_engineer`, `Model="pro"`)**:
  every `high`/`critical` change and every change you wrote yourself before calling a big task done.
- **Keep in the main session**: a single edit to a file already in context, architecture decisions,
  and the final synthesis.
- **Never** spawn subagents in a loop, never fan out more than 3 in parallel, and always give a
  subagent everything it needs in the brief (it cannot see your conversation).

## 2A. Agent lifecycle and review rounds (hard rules)

1. **Resume a subagent at most once (`send_message`).** Any further round starts a NEW subagent via
   `invoke_subagent` seeded with a checkpoint of at most 40 lines: task, branch/PR, head SHA,
   findings to fix, files, test commands. `hook_antigravity.py` denies a second `send_message`
   resume and denies resuming a subagent above 150k context tokens.
2. **Prevent findings, do not iterate on them.** For security-sensitive work the worker brief
   carries a written THREAT MODEL / probe list *before* implementation; the reviewer gets the same list.
3. **At most 2 review rounds.** Round 1 reviews the PR; round 2 reviews only the DELTA and runs
   only the affected test projects. The full suite runs once before merge.
4. **One implementation agent at a time.** Agents never poll CI: they push and stop.
5. **Ask the product questions first.** Before parallel branches, reserve check ids and file
   ownership in a short table in the coordination checkpoint.
6. **One phase per session.** At each milestone write
   `00_GOVERNANCE/coordination/tasks/todo-antigravity-<task>.md` and recommend `/compact` once the
   main transcript passes 250k tokens.

## 3. Token hygiene (apply always)

1. **Read excerpts**: `grep_search`, `view_file_outline`, `view_code_item`, `view_file` with
   `StartLine`/`EndLine`. Never dump whole large files, lock files, logs, or generated files into context.
2. **Do not re-read** what is already in context; do not re-run a passing check unless inputs changed.
3. **Batch**: independent tool calls in one turn; one targeted test file while iterating, the full
   suite once before the final push.
4. **Write less**: no narration between tool calls, no restating the plan, final report in facts
   and numbers (in Romanian for user-facing text; English for code, files, skills, and subagent briefs).
5. **Use the repo map**: `SessionStart` (`hook_antigravity.py`) injects a ~1k-token map of the
   repository (`lib/repo_map.py`). Open files by its paths; never scan a directory it marks ⚠.
6. **Checkpoint, don't replay**: keep `00_GOVERNANCE/coordination/tasks/todo-antigravity-<task>.md`
   updated at every milestone.

## 4. Quality guards (what you never trade for tokens)

- Identify the command that proves the claim, run it, read the exit code and the relevant output,
  then say DONE. If it cannot run: `UNVERIFIED` or `BLOCKED`, with the reason.
- Never skip, weaken or delete a test; never silently fall back; never widen scope to look busy.
- Inside the AI Memory Vault, run the canonical router (`PYTHONPATH=03_IMPLEMENTATION/packages python -m routing.route_cli route --task "<goal>" --capabilities <caps> --runtime antigravity`) before dispatching implementation/review specialists.

## 5. Report shape (end of task)

Outcome first (in Romanian), then evidence (commands, exit codes, counts), then what was not
verified, then the delegation that happened (which subagent, which model) in one line.
