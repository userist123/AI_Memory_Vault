# AI Memory Vault

A persistent external memory system for AI agents: lifecycle-governed notes, a
retrieval pipeline (`MemoryController.search()`), and a derived synapse graph
over the corpus.

<p align="center">
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/actions"><img alt="GitHub Actions" src="https://img.shields.io/badge/CI-GitHub%20Actions-181717?logo=githubactions&logoColor=white"></a>
  <a href="00_GOVERNANCE/VAULT_STATE.md"><img alt="State card" src="https://img.shields.io/badge/state-VAULT__STATE.md-0F766E"></a>
  <a href="20_TESTS/"><img alt="Tests" src="https://img.shields.io/badge/tests-pytest--q-2563EB"></a>
</p>

> **Read [`00_GOVERNANCE/VAULT_STATE.md`](00_GOVERNANCE/VAULT_STATE.md) before this file.** This
> README describes intent and layout. VAULT_STATE.md describes what is
> currently true, re-derived from the live vault by
> `20_TESTS/test_vault_state_accuracy.py` on every test run. Where the two
> disagree, VAULT_STATE.md wins and this file is the one that needs fixing.

---

## What this actually does today

`MemoryController.search()` sanitizes a query, classifies it, gates candidates
through a hard lifecycle/type filter, ranks them (BM25 + entity fusion), and
returns a bounded, provenance-carrying context pack. A synapse graph exists
and can extend that candidate set by one hop; it is implemented and **off by
default**, and the evidence for turning it on is negative — see
[Graph expansion](#graph-expansion-does-not-currently-help-retrieval) below.
That is the whole claim this README makes about retrieval. Everything past
this point is either measured evidence for/against a specific mechanism, or a
map of where things live.

## Three ways this repository misleads a newcomer

These are not hypothetical. Each one has already fooled a real reader of this
vault.

1. **`memory_controller/` is a 19-line shim, not the implementation.** It sets
   `__path__` across sibling packages under `03_IMPLEMENTATION/packages/`. A
   `grep` inside `memory_controller/` itself finds almost nothing and is not
   evidence the thing it names doesn't exist — the real controller is
   `03_IMPLEMENTATION/packages/memory/controller.py`, over a thousand lines.
2. **A module existing is not a module being used.** Before believing a
   component is wired into production, check who imports it:
   ```bash
   grep -rlE "(from|import)[^#]*\bMODULE_NAME\b" --include='*.py' . \
     | grep -v "/tests/\|test_\|benchmarks\|20_TESTS\|07_EVALUATION"
   ```
   An empty result means it is not wired, whatever the filename or a commit
   message claims. `graph/plasticity.py` and the `attention`/`executive`/
   `global_workspace`/`reasoning` modules currently fail this check on
   purpose — present, deliberately not called from production.
3. **Documentation drifts, and this repository has proof.** `synapse_store.py`
   claimed for months to be "NOT wired into `MemoryController.search()`"
   while the controller imported it in its constructor; an external audit
   believed the docstring over the code. This README itself was rewritten
   (2026-09-07) after its previous version described a `cognitive_core/`
   module layout, a `09_COORDINATION/` directory, and a `memory_controller/`
   implementation that do not exist at their claimed paths in this checkout —
   caught the same way: by checking, not by reading the prose.

## Repository map (verified against this checkout, not the historical layout)

| Path | Contains |
|---|---|
| `00_GOVERNANCE/` | operating rules, coordination logs, `VAULT_STATE.md` |
| `01_ARCHITECTURE/` | the corpus of governed notes (knowledge, memory, graphs) |
| `02_PRODUCT/` | project-specific notes and workspaces |
| `03_IMPLEMENTATION/packages/` | the real runtime: `memory/`, `retrieval/`, `graph/`, `lifecycle/`, `security/`, and the `memory_controller` namespace shim over them |
| `07_EVALUATION/` | benchmarks, per-package measurement reports, frozen gold sets |
| `10_DOCUMENTATION/` | procedures, Obsidian-facing resources |
| `20_TESTS/` | the test suite this README's own numbers come from |
| `30_SCRIPTS/` | operational tooling (skill ingestion, brief compilation) |
| `.agents/` | agent profiles and operational skills |
| `.claude-plugin/` | Claude Code plugin surface |
| `.github/workflows/` | CI: test, security-scan, and audit workflows |

`cognitive_core/` at the repository root exists but currently holds one file,
`recall_cli.py` — a thin CLI wrapper, not the module set an earlier version
of this README described. `00_CORE/`, `01_KNOWLEDGE/` (top level), `02_PROJECTS/`,
`03_PROCEDURES/`, `04_MEMORY/`, `05_RESOURCES/`, and `99_SYSTEM/` are the
legacy write targets `storage/path_resolver.py` still points new notes at —
see [Write path](#write-path-and-edge-promotion). Directories not listed
above (`04_CONFIG/`, `05_DATA/`, `06_INBOX/`, `08_OBSERVABILITY/`,
`09_SECURITY/`, `40_EXPERIMENTS/`, `50_ARTIFACTS/`, `60_DEPLOYMENT/`,
`70_INTEGRATIONS/`, `80_ARCHIVE/`, `90_RELEASE/`, `99_META/`) exist but were
not audited for this rewrite; their names are the only claim made about them
here.

---

## What is measured, by area

Every subsection below names the exact report and the exact command that
reproduces its numbers. None of these numbers are asserted from memory.

<details>
<summary><strong>Retrieval ranking — default changed 2026-09-07, held-out confirmed</strong></summary>

`RelevanceScorer`'s blended score (50% lexical overlap, 50% epistemic
confidence) was the production ranking key. Held-out validation found ranking
by the already-computed fusion score (`fused_score`) instead recovers more
context-recall cases (4/27 → 6/27 measurable, held-out; +2 at a threshold
pre-registered before the run). The production default is now
`RANKING_ARM_FUSED_SCORE`; the prior behavior is still available via
`ranking_arm=RANKING_ARM_BASELINE`.

Report: `07_EVALUATION/r025_wp8_a1_heldout/WP8_A1_HELDOUT_VALIDATION.md`
Reproduce: `python 07_EVALUATION/r025_wp8_a1_heldout/run_a1_heldout.py`
</details>

<details>
<summary><strong>Query-classifier filtering — a real defect, fixed behind a flag left off</strong></summary>

The query classifier infers a lifecycle filter from any query merely
containing a keyword like "verified" or "classified" as ordinary text — and
neither lifecycle stage has any notes in this corpus, so the inferred filter
collapses the candidate pool to zero. Confirmed on the frozen benchmark: 10 of
42 cases lose their gold note this way. Two softer arms exist
(`classifier_filter_arm="boost"` / `"conditional"`) that recover most of
those cases without weakening an explicit, caller-supplied filter — RAW
exclusion and any caller-passed lifecycle/type filter stay hard in every arm.
Production default is unchanged (hard exclusion), per this being a measured
finding, not a default flip.

Report: `07_EVALUATION/r025_wp9_classifier/WP9_CLASSIFIER_FILTER_ARMS.md`
Reproduce: `python 07_EVALUATION/r025_wp9_classifier/phase_b_arms.py`
</details>

<details>
<summary><strong>Abstention — the metric could not fail, so it wasn't measuring anything</strong></summary>

The benchmark harness scored every "the system should decline to answer"
case as automatically correct, because the scoring formula's gold set was
always empty for those cases. Measured the actual signal available (top
fused score, score margin, generator agreement) across answerable,
unanswerable, and 20 generated nonsense queries: none separate cleanly. The
harness now scores these cases `UNMEASURABLE` instead of a fabricated pass,
and reports `n` / `n_measurable` / `n_unmeasurable` separately everywhere.

Report: `07_EVALUATION/r025_wp11_abstention/WP11_ABSTENTION_METRIC.md`
Reproduce: `python 07_EVALUATION/r025_wp11_abstention/phase_a_calibration.py`
</details>

<details>
<summary id="graph-expansion-does-not-currently-help-retrieval"><strong>Graph expansion — does not currently help retrieval, and the benchmark barely tests it</strong></summary>

**Graph expansion is off by default, and nothing here argues for turning it
on.** Every paired comparison run against this benchmark (this session and
earlier ones) shows zero net improvement from enabling one-hop graph
expansion, sometimes a small net loss.

A closer audit found something more specific: of the graph-class benchmark
cases (old and new), **none currently reach their gold note via actual graph
traversal** (`graph_expanded_ids`) — either the gold note is already inside
the ordinary candidate pool (so expansion changes nothing) or the classifier
collapse above empties the pool entirely. So the existing on/off comparisons
have not actually exercised the traversal mechanism for a single case; "no
improvement" is honestly reported, but it is evidence about this benchmark's
current cases, not a settled verdict on the mechanism.

An attempt to add more graph cases up to the corpus's disjoint-node ceiling
(32, corrected from a prior "~33" estimate this file and an older report both
carried) found only 2 of 56 candidate edges could be turned into a
genuinely-traversal-dependent test case — a real declared edge in this vault
usually connects two notes similar enough that ordinary ranking already finds
both, without needing the graph at all.

Reports: `07_EVALUATION/heldout_retrieval_benchmark_v2/CONTRACT.md` (v2.1),
`07_EVALUATION/r025_wp12_graph_expansion/WP12_GRAPH_EXPANSION.md`
Reproduce: `python 07_EVALUATION/heldout_retrieval_benchmark_v2/run_production_arms.py`
</details>

<details>
<summary id="write-path-and-edge-promotion"><strong>Write path and edge promotion — both measured broken, neither patched here</strong></summary>

`storage/path_resolver.py` sends every new note `MemoryController.propose()`
creates to a legacy folder (`01_KNOWLEDGE/`, `02_PROJECTS/`, etc.) that the
graph/retrieval index layer never scans. Measured: **zero notes currently sit
there** — not because it was fixed, but because nothing has yet called
`propose()` against the real, file-backed storage engine. The next one will.

Separately, an attempt to promote 45 already-reviewed, already-approved edge
proposals into their source notes found `MemoryController.update()` rejects
all 45: it only permits mutating an ACTIVE-lifecycle note (a small minority
of this corpus), and the canonical frontmatter schema requires a UUID-format
`id`, which many real notes don't have. Neither gate was loosened to force
the writes through — doing so would be the exact policy bypass this vault's
lifecycle authority exists to prevent.

Reports: `07_EVALUATION/r025_wp10_write_path_damage/WP10_WRITE_PATH_DAMAGE.md`,
`07_EVALUATION/r025_wp6_edge_promotion/WP6_EDGE_PROMOTION.md`
Reproduce: `python 07_EVALUATION/r025_wp10_write_path_damage/measure_write_path_damage.py`
</details>

---

## Quick start

```bash
# Full deterministic suite (in-memory fixtures; does not touch the real vault's files)
pytest -q

# One AST-level security/policy proof, of several under 20_TESTS/regression/
pytest -q 20_TESTS/regression/test_candidate_generation_call_path.py

# VAULT_STATE.md's own numeric claims, re-derived from the live vault
pytest -q 20_TESTS/test_vault_state_accuracy.py
```

`pytest -q` from the repository root is the reproduction command for every
pass/skip count referenced anywhere in this vault's own reports — none of
those counts are meant to be trusted without it.

---

## Security and lifecycle

- Lifecycle transitions have exactly one authority:
  `03_IMPLEMENTATION/packages/lifecycle/policy.py`. Every mutation path
  (`propose`, `review`, `promote`, `update`, `attest`, `archive`, `supersede`)
  routes a transition decision through it; nothing re-implements the rule
  locally. `20_TESTS/regression/` contains AST-level proofs (not just
  behavioral tests) that specific call paths cannot bypass their gate.
- RAW-lifecycle exclusion is enforced unconditionally inside storage's own
  `query()` method — every classifier arm and ranking arm described above
  sits strictly downstream of it and cannot reach past it.
- A principal cannot promote its own claim to `verified` by writing that
  field directly; only `attest()` can, and it requires a reason and an
  evidence reference.

---

## Contributing / coordination

Multiple agents may work this repository concurrently. Check
`00_GOVERNANCE/coordination/` for in-progress or completed work before
touching a file, and read `00_GOVERNANCE/VAULT_STATE.md` section "Handing
work to another agent" before starting anything non-trivial — it links the
procedures for turning a request into a brief and for recording a solved
problem, and explains why four of this vault's own contributors independently
re-derived the same findings before someone thought to write them down.

<p align="center">
  <sub>AI Memory Vault — README last verified against commit history through r025, 2026-09-07.</sub>
</p>
