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
| `memory/controller.py` — `search()` | real, in production | 1210 lines, `search()` at line 383 |
| Query-driven candidate generation | real | r004; before it, `retrieve()` never read the query text |
| `lifecycle/policy.py` | real, sole authority | r001; 7/7 mutation paths gated, AST-verified |
| `FileStorageEngine` | real, repaired | scanned 7 dead folders and loaded **0** notes until `da99af0` |
| Graph expansion in `search()` | **implemented, OFF by default** | `controller.py:213` builds the store, `:583` traverses; `enable_graph_expansion=False` |
| Ranking arm (`ranking_arm`) | **default changed 2026-09-07 (r025 WP-8)** | was `RANKING_ARM_BASELINE` (RelevanceScorer), now defaults to `RANKING_ARM_FUSED_SCORE`; held-out confirmed +2 measurable context-recall cases at a pre-registered threshold; `RANKING_ARM_BASELINE` still available explicitly for rollback/comparison |
| Classifier-filter arm (`classifier_filter_arm`) | **implemented, OFF by default (r025 WP-9)** | the query classifier's INFERRED lifecycle/type filters can collapse the candidate pool to 0 (any query containing "verified"/"classified" as ordinary text — neither stage has any notes in this corpus); C2/C3 arms soften this behind a flag; production default stays the pre-existing hard-exclusion behaviour |
| `graph/plasticity.py` | real, **not wired** | zero production call sites; journal + rollback exist, nothing calls them |
| `attention`, `executive`, `global_workspace`, `reasoning` | present, **not wired** | r011 audited and recommended keeping them unwired |
| Held-out benchmark v1 | **INVALID** | gold ids resolve to nothing; recall structurally 0 |
| Held-out benchmark v2.1 | real, gold verified, 32 cases | `07_EVALUATION/heldout_retrieval_benchmark_v2/` (30 → 32 cases, r025 WP-12) |
| Edge proposer | real | independent resample: 81.8% precision at n=55 (r024 WP-2, seed 8675309); an earlier same-author pass measured 90% at n=30 on a seed used while tuning — treat the independent figure as operative |
| `MemoryController.update()` | real, **effectively unusable on most real notes** | ADMIN/HUMAN may only update ACTIVE-lifecycle notes (46/~850 of this corpus); the canonical schema requires UUID-format `id`; both confirmed 2026-09-07 (r025 WP-6) by an actual `update()` call, 0/45 succeeding, against real notes |

## 4. Corpus and graph, measured

| Measure | Value |
|---|---:|
| Notes in the index (`VaultIndex`, export residue excluded) | 842 |
| Notes visible to `FileStorageEngine` | 738 |
| Graph edges | 278 |
| — declared / wikilink / mirrored | 102 / 75 / 101 |
| Notes usable as a graph **seed** (out-edge) | 90 |
| Notes reachable as graph **gold** (in-edge) | 78 |
| Graph cases with pairwise-disjoint nodes | 32 |

Index and storage differ by design: they scan overlapping but distinct roots,
and storage requires a frontmatter `id`. Do not treat 842 and 738 as the same
population.

`search()` traverses **one hop** along outgoing edges. It is not multi-hop.
Graph results describe roughly 9% of the corpus and must never be pooled with
whole-corpus retrieval numbers. **The pairwise-disjoint ceiling of 32 was
corrected 2026-09-07 (r025 WP-12)** from a prior "~33" estimate (this file and
`R016_RESULT.md` both carried the stale figure); the runtime graph's edge
count (278) is confirmed unchanged, so 32 is not new drift, only a corrected
count. Of the 32 possible disjoint cases, the frozen benchmark uses 12
(`07_EVALUATION/heldout_retrieval_benchmark_v2/`); a same-session audit found
**0 of those 12 ever reach their gold note via actual graph traversal**
(`graph_expanded_ids`) rather than via ordinary retrieval already finding it —
every graph on/off comparison run against this benchmark to date (this
includes the NO-GO below) has therefore not exercised traversal for a single
case. See `07_EVALUATION/r025_wp12_graph_expansion/WP12_GRAPH_EXPANSION.md`.

## 5. Known open defects

- **The write path was never migrated.** `storage/path_resolver.py` still sends
  a `knowledge` note to `01_KNOWLEDGE` while the corpus lives in
  `01_ARCHITECTURE`. **Measured 2026-09-07 (r025 WP-10): 0 notes currently sit
  in any legacy write root** (not "many" — genuinely none; 6 of the 7 target
  folders do not even exist on disk), because nothing has ever called
  `propose()` against the real, file-backed `MemoryController` — every
  `propose()` this suite exercises runs against the in-memory fixture engine.
  The defect is therefore untriggered, not fixed: the very next real
  `propose()` call will still land in a directory the graph layer never scans.
  Existing notes are pinned in place so an update cannot relocate them
  (`db08b847`), but the taxonomy split is unresolved and is an architecture
  decision, not a constant.
- **`MemoryController.update()` cannot currently mutate most real notes.**
  Measured 2026-09-07 (r025 WP-6, attempting to promote 45 real edge
  proposals): ADMIN/HUMAN principals may only `update()` a note whose
  lifecycle is ACTIVE (46 of ~850 notes; the corpus is overwhelmingly
  REVIEW), and the canonical schema requires `id` to match
  `{"format": "uuid"}` while a large fraction of real notes use a slug id.
  0 of 45 real, already-approved edge writes succeeded. Not fixed here —
  loosening either gate without a separate, deliberate review would itself be
  the policy bypass this vault's lifecycle authority exists to prevent. See
  `07_EVALUATION/r025_wp6_edge_promotion/WP6_EDGE_PROMOTION.md`.
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
