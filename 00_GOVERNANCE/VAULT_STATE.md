# VAULT STATE — read this first

**This file records what is verified true right now, not what the architecture
intends.** README, CLAUDE.md and AGENTS.md describe the design. This file
describes the measured state. Where they disagree, this file wins and the other
document is the one that needs fixing.

Every number below was produced by running code against the real vault, and
every claim is re-checked on each test run by
`20_TESTS/test_vault_state_accuracy.py`. If a claim here drifts from reality,
the suite fails. That is the point: a state document nobody verifies becomes
the next stale docstring.

---

## 1. What this is, in two sentences

A persistent external memory substrate for AI agents, with lifecycle,
provenance and a synapse graph over notes. It aims to influence planning and
retrieval, not merely return matching text — but read section 3 before
believing any specific component does that today.

## 2. Read this before you trust a name

The repository layout is a trap for newcomers in three specific ways.

**`memory_controller/` is a shim, not the implementation.** It is a 19-line
`__init__.py` that sets `__path__` across sibling packages. The real controller
is `03_IMPLEMENTATION/packages/memory/controller.py`, roughly 1000 lines. A
`grep` inside `memory_controller/` finds nothing and is not evidence of
absence. An external audit reached exactly that false conclusion.

**A module existing is not a module being used.** Before believing any
component is "in production", run the rule from `CLAUDE.md`:

    grep -rlE "(from|import)[^#]*\bMODULE\b" --include='*.py' . \
      | grep -v "/tests/\|test_\|benchmarks\|20_TESTS\|07_EVALUATION"

Empty result means it is not wired, whatever the file name or commit message
says.

**Documentation has drifted before.** `synapse_store.py` claimed for months to
be "NOT wired into MemoryController.search()" while the controller imported it
in its constructor. Corrected 2026-09-06.

## 3. Component reality, measured

| Component | State | Evidence |
|---|---|---|
| `memory/controller.py` — `search()` | real, in production | ~1000 lines, `search()` at line 274 |
| Query-driven candidate generation | real | r004; before it, `retrieve()` never read the query text |
| `lifecycle/policy.py` | real, sole authority | r001; 7/7 mutation paths gated, AST-verified |
| `FileStorageEngine` | real, repaired | scanned 7 dead folders and loaded **0** notes until `da99af0` |
| Graph expansion in `search()` | **implemented, OFF by default** | `controller.py:118` builds the store, `:406` traverses; `enable_graph_expansion=False` |
| Cognitive core (`PlanComplexityAnalyzer`, `CouncilBudgetController`, `ContextPackBuilder`) | **wired, OFF by default** | `03_IMPLEMENTATION/packages/memory/controller.py`; evaluated on heldout v2 in `07_EVALUATION/cognitive_core/EVALUATION_REPORT.md` (52% envelope token reduction, zero recall loss); `enable_cognitive_core=False` |
| `graph/plasticity.py` | real, **wired in production** | wired into `synapse_store.py` and `controller.py`; transactional prune and byte-for-byte rollback active |
| `executive`, `global_workspace`, `reasoning`, `working_memory` | **wired, OFF by default** | `03_IMPLEMENTATION/packages/memory/controller.py`; evaluated on benchmark v3 in `07_EVALUATION/cognitive_core/MODULE_EVALUATION_REPORT.md`; OFF by default |
| `RetrievalTrace` v1.1.0 | real, in production | `observability/retrieval_trace.py`; every note carries a reason code; 16.7 KB per search, verified on 8 benchmark queries |
| Agent lifecycle floor | real, in production | `controller.py`; `AI_AGENT` asking for no lifecycle gets ACTIVE + REVIEW. Measured cost before adoption: 1 case in 130 |
| Untrusted content guard | real, in CI | `30_SCRIPTS/verification/untrusted_content_guard.py`; 4 blocking rules, 3 report-only; 27 reviewed allowlist entries |
| Typed relations in the graph | **audited, 20/49 accepted** | Perplexity, independent; 29 rejected rows purged at source; **65 still unaudited** |
| Held-out benchmark v1 | **INVALID, and no longer run in CI** | gold ids resolve to nothing; recall structurally 0; its schema check also could never pass |
| Held-out benchmark v2 | real, gold verified | `07_EVALUATION/heldout_retrieval_benchmark_v2/` |
| Edge proposer | real | 18% → 90% sampled precision, 182 proposals |
| `30_SCRIPTS/ingestion/convert_pdf_to_text.py` | real, measured | r030-r031; **20 of 20** books, 1,088 chunks measured by chunking |
| `30_SCRIPTS/ingestion/model_extract_concepts.py` | real, gates and selectivity both work | r031; recurrence floor validated on all 3 structure modes |
| `30_SCRIPTS/ingestion/extract_book_concepts.py` (rule-based) | real, **unusable on books** | 28% of its 112 corpus candidates are not terms |

## 4. Corpus and graph, measured

| Measure | Value |
|---|---:|
| Notes in the index (`VaultIndex`, export residue excluded) | 1040 |
| Notes visible to `FileStorageEngine` | 858 |
| Graph edges | 483 |
| — declared / inferred / wikilink | 203 / 203 / 77 |
| Notes usable as a graph **seed** (out-edge) | 160 |
| Notes reachable as graph **gold** (in-edge) | 148 |
| Graph cases with pairwise-disjoint nodes | 32 |

Index and storage differ by design: they scan overlapping but distinct roots,
and storage requires a frontmatter `id`. Do not treat 842 and 738 as the same
population.

`search()` traverses **one hop** along outgoing edges. It is not multi-hop.
Graph results describe roughly 9% of the corpus and must never be pooled with
whole-corpus retrieval numbers.

## 5. Known open defects

- **The write path is migrated for six types, not for the rest.** New notes of type
  `knowledge`, `lesson`, `error`, `preference`, `procedure` and `project` now go where the
  existing notes of that type already are (`01_ARCHITECTURE/knowledge`, `01_ARCHITECTURE/memory`,
  `10_DOCUMENTATION/procedures`, `02_PRODUCT/projects`), when the vault has that folder
  (`storage/path_resolver.py`, `CONTENT_TREE_FOR_TYPE`). Other types still go to the legacy tree, and
  an existing note in the legacy tree keeps its legacy destination on update; existing notes in the
  content roots stay pinned in place (`db08b847`). Nothing has been moved: the legacy folders are not
  migrated, only new writes are redirected.
- **The memory is reachable by agents, but only just, and the results are weak.** `.mcp.json` registers
  the MCP server `vault-memory` (`interfaces/memory_mcp_server.py`: `memory_search`, `memory_get`,
  `memory_propose`); `python -m cognitive_core.recall_cli` is the CLI fallback. There is no REST
  server at `localhost:8000`. Claude Code 2.1.277 loaded the registration and connected, but no
  Claude Code model turn has used it yet (the standalone CLI was not logged in). 20 real work
  questions from `00_GOVERNANCE/coordination/` were put through the server: 5 of the 57 first-three
  results were relevant, and none of the 8 coordination files behind them is an indexed note. Evidence
  and the computed report: `07_EVALUATION/memory_usage/`.
- **`FileStorageEngine` hard-fails on duplicate UUIDs.** That is deliberate
  integrity behaviour, and stays that way — the legacy/content root union
  still makes collisions possible in general. The specific recurring instance
  is fixed (r024 WP-0): the tracked fixture that carried the all-zeros UUID
  moved from `01_ARCHITECTURE/knowledge/test_00000000.md` (a content root) to
  `20_TESTS/fixtures/test_00000000.md` (same pollution class as `unknown_A.md`,
  flagged in r005 and not fixed there), so a local untracked note reusing that
  id under `01_KNOWLEDGE/` no longer collides with anything tracked. The error
  message now names both paths and which tree (content root vs legacy write
  root) each belongs to, for the next time two notes genuinely do collide.
- **86% of notes have no semantic edge.** Many are connected only to
  navigation hubs, which look connected in Obsidian and carry no retrieval
  signal.
- **`prune()` semantics** were tightened in r009a so wikilink edges survive.
  Plasticity is no longer uncalled: the audited purge of 29 typed relations ran
  through it, with a journal and a rollback proved field for field. One caveat
  worth keeping in mind — **that rollback restores the in-memory synapse store,
  not the repository.** The purge that happened rewrote 29 notes' frontmatter,
  and undoing that is `git revert`, not `PlasticityEngine.rollback()`.
- `06_INBOX/RAW_IMPORTS/` is allowlisted in `.gitleaks.toml`. Anything
  force-added from there is not secret-scanned.
- **65 of the graph's 85 typed relations have never been audited.** The 49 that
  were came back at 20 accepted. No claim about the graph's overall precision is
  supported until the rest are labelled; 49 rows is what was measured, not 114.
- **Where the 91 missed benchmark cases are lost is still unknown.** Reason
  codes now exist for every note, but nothing has yet connected them to the
  benchmark. Until that runs, choosing between a reranker and better candidate
  generation is a guess.
- **Promoted notes were islands, and one still could be.** A note can declare
  a relation, validate on write and read correctly in Obsidian while
  contributing nothing to the graph: `SynapseStore.from_index()` reads
  `target_id` and `type`, and skips anything else with a bare `continue`
  (`synapse_store.py:234`). `Promoted_reservoir_sampling.md` used `target`
  with a file path, `relation` instead of `type`, and `derived_from`, which
  is not in `ALLOWED_RELATIONS` and degrades silently to `related_to`. Three
  mismatches, zero edges. Fixed, and guarded by
  `20_TESTS/test_promoted_notes_reach_the_graph.py`, which asserts against
  the real store rather than the frontmatter and was confirmed to fail on the
  broken form before being trusted.
- **Cross-model agreement is a reference set, not the filter.** It was
  briefly recorded here as the only working ranking signal. It is not: it
  separates cleanly on conference papers, where the 1-of-4 tail is
  experimental furniture, and not on monographs, where the same tail holds
  `long-term potentiation` and `memory consolidation`. Its distribution
  barely moves between books (4-of-4 at 6-9%, 1-of-4 at 66-70%), which makes
  it a property of the method rather than a measure of quality. It costs four
  runs. `30_SCRIPTS/ingestion/agree_across_models.py` is kept for producing
  the reference set that the cheap single-model rule is checked against, and
  `--min-models` defaults to 1 so it annotates rather than filters.
- **Nothing about book extraction was validated on a book until late.** Every
  measurement through r031 was taken on `sarfraz22a`, a 17-page conference
  paper. The first monograph run exposed two defects immediately: a fixed
  24,000-character chunk limit that skipped 26 of Schacter & Tulving's 29
  chunks, and a grounding check requiring exact whole-quote matching that
  refused 51 candidates of which 17 quoted the source at 80% or better. Both
  are fixed. Treat any paper-scale number as unvalidated at book scale until
  it has been re-measured there.
- **Every per-candidate signal is empty; only agreement carries anything.**
  This is the list of what was measured and found to rank nothing, so it is
  not tried again: model confidence is `1.00` on every candidate including
  ones whose evidence was fabricated; `claim_type` was constant on every
  survivor in every run and has been removed from the request, which also
  stopped it corrupting `slot`; and a prompt-level exclusion list is ignored
  by the model — the terms it names as bad come straight back. `occurrences`
  is NOT on this list: it reads as dead at large chunk sizes, where one chunk
  covers a chapter and everything appears once, and carries real signal once
  chunks are small enough.
- **Volume is answered, by a recurrence floor.** `occurrences` — how many
  distinct sections define a term — is the one counted-rather-than-claimed
  field. Validated on all three structure modes (`pages`, `toc`, `font`), and
  the rate varies by 2.5x between books: 0.149 concepts at `>= 3` per chunk
  for Squire & Kandel, 0.236 for a survey, 0.270 for Soar. Across 1,088
  corpus chunks that is **160-290 candidates**, under this project's own 300
  stop condition. Recall is 40-55% of what four models would agree on. The
  method and everything that failed on the way to it are in
  `10_DOCUMENTATION/procedures/Ingesting_A_Book_Into_The_Ontology.md`.
- **A corpus run is 11.6-20.1 hours** for one model: 1,120 chunks measured,
  at 37-65 s/chunk. That count needs `--force-mode pages` on SIX books
  (Newell, Soar, Kandel 2001, Why We Forget, 2601.09113v1, Memory in the
  Age of AI Agents); without it, four of them lose a single chunk each
  that is 33-90% of the book, and Kandel is effectively not ingested. The spread is real — generation time follows output length,
  so a book with smaller chunks can be slower per chunk.
  Two ways this figure has been got wrong here, both worth not repeating.
  Timing a book's first chunks: they are front matter, produce almost no
  output, and that produced a 3.0-hour estimate wrong by 4.6x. And counting
  chunks by dividing `characters / headings` from the conversion report
  instead of chunking the file: that made Soar's 22 chunks look like 114 and
  its 47,991-character median look like 7,798, which hid the fact that 88% of
  the book was being skipped.
- **The two scans are ingestible now.** Ashby's *Introduction to Cybernetics*
  (156 pp) and Minsky's *Society of Mind* (336 pp) had no text layer at all.
  Tesseract is installed and `convert_pdf_to_text.py --ocr` handles them: 492
  pages in 6m23s, 0.78 s/page. Minsky came back clean — zero degraded pages,
  zero column artefacts. **Ashby is unblocked, not recovered**: its character
  quality is fine, but it is set in two columns and Tesseract merges them, so
  13% of its pages are in scrambled sentence order. Neither the degraded-page
  detector nor the extraction grounding check can see that, because every
  word is correct and the scrambled text genuinely is in the source.
- **`glm-4.7-flash` no longer loads.** 19 GB against an 8 GB GPU; the
  endpoint returns `llama-server process has terminated`. Only
  `qwen2.5-coder` 3b and 7b fit. Any speed or quality number attributed to a
  larger model predates this and was taken while it still spilled to CPU
  rather than failing.
- **A client timeout does not cancel the work.** After a request times out,
  the endpoint keeps generating and a request for another model queues behind
  it. Retrying a timeout deepens the backlog; only unreachable endpoints are
  retried.

## 6. Trigger table

| If the task involves… | Read first | Do not assume |
|---|---|---|
| Retrieval or search | `memory/controller.py::search` | that graph expansion runs; it is off by default |
| Anything graph | section 4 above | that "connected" means retrievable — check direction |
| Writing a note | `storage/path_resolver.py` | that it lands in the content tree |
| Lifecycle changes | `lifecycle/policy.py` | that any path may bypass it; none may |
| Benchmarks or evidence | v2 contract | that v1 numbers mean anything |
| Claiming something is wired | the grep in section 2 | a commit message |
| Any file edit | `00_GOVERNANCE/coordination/` | that you are the only agent working |

## 7. How previous sessions solved things

`10_DOCUMENTATION/procedures/Recording_A_Solved_Problem.md` defines what
"finished" means and how to record a solved problem. The methods themselves
live in `01_ARCHITECTURE/memory/Lessons/`, tagged `method`.

Read them before diagnosing something that feels familiar. They exist because
four reviewers spent an evening re-deriving the same findings, each starting
from zero. Four are recorded so far:

- repairing a read path can arm a destructive write path;
- a guard that fires for the wrong reason hides the defect it protects;
- two independently correct changes can cancel each other and report success;
- a module existing is not a module being used, and a shim looks like absence.

`20_TESTS/test_procedural_memory_contract.py` fails when one of these notes is
missing a required section, promotes itself past REVIEW, or leaves "Still open"
unanswered.

## 8. Handing work to another agent

Replies to the user are Romanian; everything transmitted to an agent is English
and complete. `30_SCRIPTS/prompt/compile_task_prompt.py` emits the deterministic
half of a brief — current commit, live corpus and graph numbers, the recorded
methods, the standing traps, and the definition of finished — so no handoff
starts from a blank page or from figures copied out of a document that may have
rotted.

    python 30_SCRIPTS/prompt/compile_task_prompt.py --intent <kind>         --task "..." --branch "rXXX/name" --owner "AGENT"

`--infer` proposes the kind from the request text and shows its reasoning
rather than choosing for you. Trust it for one thing: it catches an action
requested on something whose quality is unestablished — "promote the proposed
edges" is a question about quality, not an instruction — at 86%, and that rule
generalises. Plain classification scores 50% on unseen phrasings against 100%
on the requests its patterns were drawn from, so on weak signal it declines and
asks rather than guessing.

The kind decides what the brief must contain: `implement`, `verify`, `measure`,
`fix` or `migrate`. A measurement brief carries a stop condition and a
fail-loud treatment arm; a migration brief carries a proven recovery path. See
`10_DOCUMENTATION/procedures/Compiling_A_Request_Into_A_Brief.md`, which holds
the worked examples of turning a one-line request into one of these.

Task, requirements and forbidden are left as `TODO`: they need the sender's
judgement. A brief shipped with `TODO` still in it is unfinished.

## 9. How to update this file

Any session that empirically demonstrates a row here has changed must update
it in the same commit as the change. Not afterwards, not in a follow-up. The
accompanying test enforces the numeric claims; the prose rows are on your
honour, and the whole file is worthless the first time someone lets one rot.
