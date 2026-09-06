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

## 4. Corpus and graph, measured

| Measure | Value |
|---|---:|
| Notes in the index (`VaultIndex`, export residue excluded) | 842 |
| Notes visible to `FileStorageEngine` | 738 |
| Graph edges | 278 |
| — declared / wikilink / mirrored | 102 / 75 / 101 |
| Notes usable as a graph **seed** (out-edge) | 90 |
| Notes reachable as graph **gold** (in-edge) | 78 |
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
  integrity behaviour, but the legacy/content root union makes collisions more
  likely. A tracked fixture `01_ARCHITECTURE/knowledge/test_00000000.md`
  carries the all-zeros UUID; if a local copy exists under `01_KNOWLEDGE/`,
  the engine raises and every storage-backed path is unusable in that working
  tree until one copy is removed.
- **86% of notes have no semantic edge.** Many are connected only to
  navigation hubs, which look connected in Obsidian and carry no retrieval
  signal.
- **`prune()` semantics** were tightened in r009a so wikilink edges survive,
  but plasticity is still uncalled, so the interaction is untested in anger.
- `06_INBOX/RAW_IMPORTS/` is allowlisted in `.gitleaks.toml`. Anything
  force-added from there is not secret-scanned.

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

## 7. How to update this file

Any session that empirically demonstrates a row here has changed must update
it in the same commit as the change. Not afterwards, not in a follow-up. The
accompanying test enforces the numeric claims; the prose rows are on your
honour, and the whole file is worthless the first time someone lets one rot.
