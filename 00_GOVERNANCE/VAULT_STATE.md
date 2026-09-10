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
| `graph/plasticity.py` | real, **not wired** | zero production call sites; journal + rollback exist, nothing calls them |
| `attention`, `executive`, `global_workspace`, `reasoning` | present, **not wired** | r011 audited and recommended keeping them unwired |
| Held-out benchmark v1 | **INVALID** | gold ids resolve to nothing; recall structurally 0 |
| Held-out benchmark v2 | real, gold verified | `07_EVALUATION/heldout_retrieval_benchmark_v2/` |
| Edge proposer | real | 18% → 90% sampled precision, 182 proposals |
| `30_SCRIPTS/ingestion/convert_pdf_to_text.py` | real, measured | r030; 18 of 20 books converted, 6.6M chars, 1,107 chunks |
| `30_SCRIPTS/ingestion/model_extract_concepts.py` | real, **gates work, selectivity does not** | r031; see `07_EVALUATION/book_corpus_conversion/FINDINGS.md` |
| `30_SCRIPTS/ingestion/extract_book_concepts.py` (rule-based) | real, **unusable on books** | 28% of its 112 corpus candidates are not terms |

## 4. Corpus and graph, measured

| Measure | Value |
|---|---:|
| Notes in the index (`VaultIndex`, export residue excluded) | 830 |
| Notes visible to `FileStorageEngine` | 738 |
| Graph edges | 262 |
| — declared / inferred / wikilink | 95 / 94 / 73 |
| Notes usable as a graph **seed** (out-edge) | 89 |
| Notes reachable as graph **gold** (in-edge) | 77 |
| Graph cases with pairwise-disjoint nodes | 33 |

Index and storage differ by design: they scan overlapping but distinct roots,
and storage requires a frontmatter `id`. Do not treat 842 and 738 as the same
population.

`search()` traverses **one hop** along outgoing edges. It is not multi-hop.
Graph results describe roughly 9% of the corpus and must never be pooled with
whole-corpus retrieval numbers.

## 5. Known open defects

- **The write path was never migrated.** `storage/path_resolver.py` still sends
  a `knowledge` note to `01_KNOWLEDGE` while the corpus lives in
  `01_ARCHITECTURE`. New notes land in the legacy tree. Existing notes are
  pinned in place so an update cannot relocate them (`db08b847`), but the
  taxonomy split is unresolved and is an architecture decision, not a constant.
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
- **`prune()` semantics** were tightened in r009a so wikilink edges survive,
  but plasticity is still uncalled, so the interaction is untested in anger.
- `06_INBOX/RAW_IMPORTS/` is allowlisted in `.gitleaks.toml`. Anything
  force-added from there is not secret-scanned.
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
- **Selectivity now exists, and it costs.** Cross-model agreement is the only
  ranking signal in this pipeline that carries information, because it is
  counted rather than claimed and no model can inflate it —
  `30_SCRIPTS/ingestion/agree_across_models.py`. Measured over four locally
  installed 7-8B models on the same chunks: 60% of concepts were found by
  exactly one model, and that tail is the experimental furniture
  (`Overall loss`, `Soft-targets`, `reliability plots`) nothing else could
  remove. It is not free: four models over 1,107 chunks is ~27h against ~7h
  for one, and it discards true positives — `catastrophic forgetting` and
  `experience replay` were each found by only one of four. `--min-models`
  therefore defaults to 1, annotating rather than filtering.
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
  stopped it corrupting `slot`; `occurrences` is 1 for 47 of 48 concepts in a
  full paper; and a prompt-level exclusion list is ignored by the model —
  the terms it names as bad come straight back.
- **Volume is answered, by a recurrence floor.** `occurrences` is the one
  counted-rather-than-claimed field, and it only works at 3 pages per chunk —
  at 10 pages one chunk covers a chapter and everything appears once. Whole
  book measured: 159 raw candidates, 38 at `>= 2`, **17 at `>= 3`**, which is
  roughly **210 corpus-wide** and under this project's own 300 stop
  condition. Recall is 40-55% of what four models would agree on. The method
  and everything that failed on the way to it are in
  `10_DOCUMENTATION/procedures/Ingesting_A_Book_Into_The_Ontology.md`.
- **A corpus run is ~13.6 hours** for one model at 45.8s per chunk over 1,073
  chunks. Do not re-derive this from a book's first chunks: they are front
  matter, produce almost no output, and generation time follows output
  length. That mistake produced a 3.0-hour estimate, wrong by 4.6x.
- **Two books cannot be ingested at all.** Ashby's *Introduction to
  Cybernetics* and Minsky's *Society of Mind* are scans with no text layer,
  0.0 characters per page. They need OCR, which does not exist here.
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
