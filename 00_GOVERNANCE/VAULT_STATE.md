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
| Agent read contract — `memory_search` / `memory_get` (`interfaces/memory_access.py`, `retrieval/context/pack_builder.py`) | **real, in production** | `AI_AGENT` is served ACTIVE and REVIEW notes; a REVIEW note is flagged `unverified`. Withheld: a note flagged `quarantined`, ARCHIVED/RAW notes, and the body of an unverified REVIEW candidate inside a *trusted context pack*. Not being `verified` hides nothing (an ACTIVE note keeps its content). Real vault, 10 queries x top 5 through `memory_access`: **48/48** non-empty snippets and **48/48** `memory_get` ok (main 38/48 and 38/48; the audit-remediation branch before its repair 2/48 and 6/48). The extra 10 over main are REVIEW notes that were never stamped, read as unverified. Guard: `20_TESTS/test_memory_access.py`, whose fixture holds the real mix of states (`seed_real_distribution`); a verified-only fixture had hidden the regression. `ADMIN` and `HUMAN` are the owner views in the pack builder and the egress gate alike (`security/verified_reduction.py`, `OWNER_PRINCIPALS`) |
| Proposal queue approve → promote (`lifecycle/proposal_queue.py`, `queue_promoter.py`; CLI `memory_v6_cli`; REST `/api/v1/proposals/*`) | **real, works end to end** | An approval is an owner attestation: a typed owner `Principal` (the vault's own `ATTEST` matrix: HUMAN, ADMIN), a reviewer name and an evidence reference, none defaulted; a string such as `human` satisfies nothing. Before: over REST `promote-approved` always failed, and on every path a promoted candidate failed schema validation (`candidate-<uuid>` id, extractor keys in `provenance`, `fact`/`task` as note types), so no real controller ever took one. A promotion only proposes (RAW, unverified): it verifies nothing. One unattested legacy approval no longer blocks the rest (it is reported in `skipped`). Guard: `20_TESTS/test_rest_proposal_flow.py`, `20_TESTS/test_proposal_queue_attestation.py` |
| REST gateway (`interfaces/api_server.py`) | **real; bearer token on every route but `/status`** | `AI_MEMORY_VAULT_API_TOKEN`, fail-closed while unset. Its clients send it: `jarvis_web/js/vault_client.js` and `js/app.js` (from `sessionStorage`, never in source), `jarvis_v2/supervisor.py` (from the environment). The web page itself is served by `server.cjs`, which has no `/api/v1` proxy: the page needs the gateway behind the same origin. Other behaviour changes of the same branch: an `AI_AGENT` cannot update an ACTIVE note except `relations`/`confidence`/`verification`/`valid_until`, and never to `verified`; a note body over 20,000 characters is refused by the controller |
| Runtime-authority layer — `security/runtime_enforcer.py`, `runtime_adapter.py`, `memory_adapter.py`, `memory_boundary.py`, `memory_integrity.py`, `security_update_manager.py` | **implemented and tested, NOT wired into production** | The HMAC `ApprovalBroker`, the SQLite-WAL `PersistentNonceStore`, `production_mode`, revision/content binding, the write-boundary rollback and the update provenance gate have no importer outside `security/` and the tests, and nothing builds `RuntimeAdapter`, `RuntimeEnforcer` or `ApprovalBroker` with `production_mode=True`. Findings B1/B2/B4, M01/M02/M03/M07, U02/U03 are therefore *hardened in the library, not yet wired into production*; they harden nothing at runtime until a tool-execution path calls them. Guard: `20_TESTS/test_vault_state_accuracy.py::test_runtime_authority_layer_has_no_production_consumer` fails the day one gains a consumer, so this row gets corrected |
| External skills importer (`30_SCRIPTS/verification/import_external_skills.py`) | **real, fail-closed** | A script, binary, hidden path (except the checkout's top-level `.git`), executable bit, symlink or traversal aborts the import and every offending path is listed; a file of a type that is not imported (image, `LICENSE`, ...) is left out and written to `SKIPPED_FILES.json`. `20_TESTS/test_import_external_skills.py` |
| Secret scanning config (`.gitleaks.toml`) | **real, `[[allowlists]]` format** | Gitleaks refuses a file that mixes the legacy `[allowlist]` with `[[allowlists]]`; `20_TESTS/test_gitleaks_config_format.py` keeps the file in the array-of-tables form the other branches extend |
| Typed relations in the graph | audited once, by a single LLM rater (30/114 accepted) | Perplexity, one rater, no second opinion and no inter-rater agreement; Wave 1: 20/49 accepted, 29 purged; Wave 2: 10/65 accepted, 55 rejected; `07_EVALUATION/edge_audit_v2_remaining/AUDIT_RESULT.md` |
| Held-out benchmark v1 | **INVALID, and no longer run in CI** | gold ids resolve to nothing; recall structurally 0; its schema check also could never pass |
| Held-out benchmark v2 | real, gold verified | `07_EVALUATION/heldout_retrieval_benchmark_v2/` |
| Edge proposer | real | 18% → 90% sampled precision, 182 proposals |
| `30_SCRIPTS/ingestion/convert_pdf_to_text.py` | real, measured | r030-r031; **20 of 20** books, 1,088 chunks measured by chunking |
| `30_SCRIPTS/ingestion/model_extract_concepts.py` | real, gates and selectivity both work | r031; recurrence floor validated on all 3 structure modes |
| `30_SCRIPTS/ingestion/extract_book_concepts.py` (rule-based) | real, **unusable on books** | 28% of its 112 corpus candidates are not terms |
| `03_IMPLEMENTATION/packages/routing/` (agent router + dispatcher) | real and tested, **NOT wired into production** | reachable only through the manual CLI `python -m routing.route_cli` (`probe` / `route` / `dispatch --execute`); no production module imports it, it does not call `memory_search`, and nothing dispatches the verifier it selects (`PENDING_VERIFICATION` is terminal). Dispatch is tested against the real `04_CONFIG/agent_router.json` with fake executables only; no real agent was invoked (`20_TESTS/test_agent_dispatch_real_config.py`) |
| `03_IMPLEMENTATION/packages/agent_bridge/` (secure bridge) | library + tests, **NOT wired, no transport** | `transport: windows_named_pipe` is validated in `04_CONFIG/agent_bridge.json` but no pipe server exists; `load_bridge_config()` / `build_bridge()` and `AntigravitySession` have no consumer outside `20_TESTS`; nothing runs it end to end. `minimum_ttl_seconds` is enforced and an AGY session never carries context across tasks (`20_TESTS/test_agent_bridge_hardening.py`), verified against fakes, not the real `agy` |
| Direct routes `vault://` (`vault_access/`, 04_CONFIG/vault_domains.yaml) | real, **in the MCP server, CLI and Telegram bot** | measured 2026-10-10: 4442 routes in 117 domains (clean checkout; re-checked by `test_route_and_domain_counts_are_current`); by URI 4442/4442; by file name (measured 2026-10-10 on 4433 routes) 4199 resolve to themselves, 234 AMBIGUOUS, **0 wrong**; `07_EVALUATION/vault_routing/`. Names and titles only — topical questions still go to `memory_search`. The first call needs the metadata of every route: the MCP server warms it in a background thread at start (C YAML loader when PyYAML has it, one frontmatter parse per file; `20_TESTS/test_vault_access_perf.py`) |
| Access policy per egress channel (`04_CONFIG/access_policy.yaml`) | real, enforced on every `vault_*` call | cloud CLIs ≤ INTERNAL, Telegram ≤ INTERNAL, web export PUBLIC; inbox, archive, RAW skills never served to agents, and a refusal is returned as NOT_FOUND (real reason only in the audit); MCP and the CLI cannot assert the owner or the local channel (owner needs an interactive terminal). Single-user machine: an agent with a shell can still read files directly — this policy binds the vault tools, OS permissions bind the rest |
| Ollama/Telegram assistant (`vault_access/ollama_assistant.py`) | real, **not yet run against a live Ollama** | reads are extractive (no model call); questions use native `/api/chat` with explicit `num_ctx`, truncation check and verbatim-quote verification; proved with a fake transport (assistant + Telegram: 39 tests) |
| Book-to-Memory research modules (`lifecycle/validation/book_to_memory_*.py`, 19 modules: the 11 of phases 1-11 and 8 evaluation-integrity modules for PR #209 B03-B08: run config, leakage, paired statistics, raters, blind packet, real-model ablation harness, prompt audit, human labels) | **present, research-only, NOT wired** | no production consumer: only each other, the research runners and scripts (`08_RESEARCH/BOOK_TO_MEMORY/`, `30_SCRIPTS/evaluation/*b2m*`) and `20_TESTS/test_book_to_memory_*.py` import them. Their usage-test and ablation gates have no built-in scores: without supplied observations they report `INSUFFICIENT_DATA`, and without a recorded run config an ablation is refused as not comparable. The B03 task packet (`08_RESEARCH/BOOK_TO_MEMORY/b03_task_packet/`, 51 tasks, 204 trials, prompts only) has not been run: no real-model ablation, no multi-rater scoring and no human-label calibration exists (B03, B05, B06 wait on the owner). B07 was measured: 2 exact text overlaps in the frozen v1/v2 benchmarks (flagged), 0 in the H1 sets (`07_EVALUATION/b2m_leakage/`). Passing unit tests is not empirical evidence (PR #209 B01, B09); open items in `08_RESEARCH/BOOK_TO_MEMORY/OPEN_BLOCKERS.md`. Their owner-approval token is bound to the exact note: it signs the SHA-256 of the canonical note, its revision marker and an expiry, and an edit after approval, a replay, an expired or an old-format token is refused (B02; `20_TESTS/test_book_to_memory_lifecycle_gates.py::test_32_*`). A blocker's severity cannot be lowered, nor a blocker deleted, without an owner attestation in `08_RESEARCH/BOOK_TO_MEMORY/SEVERITY_ATTESTATIONS.md`; the `Repository Hygiene` workflow enforces it on pull requests (B12; `20_TESTS/research/test_blocker_severity_downgrade.py`) |
| `retrieval/interference_gate.py` | **present, NOT wired** | no production consumer at all; only `20_TESTS/test_interference_gate.py` imports it |
| H1 associative-recall experiment (`08_RESEARCH/BOOK_TO_MEMORY/run_h1_baseline.py`, `run_h1_associative_experiment.py`) | **research harness, NOT wired** | frozen case packet and runners under `08_RESEARCH/`; nothing in `03_IMPLEMENTATION` imports them. Its CI workflow `h1-associative-experiment.yml` runs only on changes to `08_RESEARCH/BOOK_TO_MEMORY/**`, `08_RESEARCH/RETRIEVAL/**` or the workflow file, or on demand. A run is an experiment result on one frozen packet, not production evidence |

## 4. Corpus and graph, measured

| Measure | Value |
|---|---:|
| Notes in the index (`VaultIndex`, export residue excluded) | 1260 |
| Notes visible to `FileStorageEngine` | 858 |
| Graph edges | 487 |
| — declared / inferred / wikilink | 154 / 155 / 178 |
| Notes usable as a graph **seed** (out-edge) | 196 |
| Notes reachable as graph **gold** (in-edge) | 143 |
| Graph cases with pairwise-disjoint nodes | 32 |

Index and storage differ by design: they scan overlapping but distinct roots,
and storage requires a frontmatter `id`. Do not treat 842 and 738 as the same
population.

The index count includes 51 `Promoted_*` notes (lifecycle REVIEW, verification
`unverified`, `provenance.source_type: import`) added as candidates from the OpenStax and
ontology concept batches. None of them is attested: an agent reads them flagged as
unverified, and they are not ACTIVE.

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
- **Agents now have direct routes, but no live agent turn has used them yet.** The `vault_*` tools are
  registered for Claude Code, Codex, Antigravity and Gemini CLI (`AGENTS.md`, "Direct routes for every AI");
  the stdio contract is tested, no client session has been observed calling them. The 8 coordination files
  that `memory_search` cannot reach (below) are reachable by route (`vault://coordination/...`).
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
- **All 114 declared typed relations in the live graph have been audited once; the audit is single-rater and unreplicated.** The remaining 65
  relations were evaluated by one LLM rater (Perplexity) in Wave 2: 10 accepted, 55 rejected (15.4% precision).
  A second rater, or a sample re-labelled by the owner, has not been run, so "100% audited" means "every edge was
  judged by one rater", not "every edge is verified".
  Across the whole graph population, 30 of 114 typed relations are verified (26.3% precision; 84 total rejections
  documented with rationales in `07_EVALUATION/edge_audit_v2_remaining/AUDIT_RESULT.md` and simulated in
  `07_EVALUATION/edge_audit_v2_remaining/audit_purge_dry_run_report.md`).
- **Causal loss attribution of missed benchmark cases: measured, single run.** Evaluated on all 130
  non-abstain benchmark v3 cases in `07_EVALUATION/loss_funnel/LOSS_FUNNEL_REPORT.md` (and `loss_funnel_cases.json`).
  Under production agent operating point (`AI_AGENT`, `page_size=5`), 71.56% of misses are `PAGINATION_CUT`
  (ranked > 5; median rank 20.5), 13.76% `AGENT_LIFECYCLE_FLOOR_EXCLUDED`, 11.93% `NEVER_CANDIDATE`, 1.83%
  `CANDIDATE_LIMIT_CUT`, and 0.92% `RAW_EXCLUDED` (0.00% undetermined). A pre-registered decision rule
  *points to* a cross-encoder / reranker rather than blind candidate generator expansion. That is the outcome of the
  rule on one run, not an adoption: no reranker is built, wired or evaluated.
- **Promoted notes were islands, and one still could be.** A note can declare
  a relation, validate on write and read correctly in Obsidian while
  contributing nothing to the graph: `SynapseStore.from_index()` reads
  `target_id` and `type`, and skips anything else with a bare `continue`
  (`synapse_store.py:234`). `Promoted_reservoir_sampling.md` used `target`
  with a file path, `relation` instead of `type`, and `derived_from`, which
  is not in `ALLOWED_RELATIONS` and degrades silently to `related_to`. Three
  mismatches, zero edges. That malformed form is guarded against by
  `20_TESTS/test_promoted_notes_reach_the_graph.py`, which asserts against
  the real store rather than the frontmatter and was confirmed to fail on the
  broken form before being trusted.
  **Nine promoted notes are genuine islands today** (`buffer`, `homeostat`, `regulation`, `reinforcement_learning`,
  `reservoir_sampling`, `retrieval`, `state_determined_system`, `transformation`, `variety`): every typed
  relation they declared was rejected by the edge audit, and a script had hidden that by injecting unsupported
  sentences into their bodies (e.g. "... in [[long-term memory]]"; PR #209 B11). The injected prose is removed, the
  original bodies are restored byte for byte, and the notes are listed in
  `KNOWN_ISLANDS_AFTER_AUDITED_PURGE` in that test, which pins the set so it can only shrink. Giving them a real,
  audited relation is open work.
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
| Finding or reading a specific file | `vault_resolve` / `vault_read` (`AGENTS.md`) | that a name is unique: AMBIGUOUS is an answer, not an error |
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
