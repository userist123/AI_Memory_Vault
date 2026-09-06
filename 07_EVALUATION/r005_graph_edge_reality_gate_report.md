# r005 Phase 1 — Graph Edge Reality Gate

**Decision: NO-GO. Do not proceed to Phase 2 (wiring graph expansion into
the production query path).**

This is a blocking measurement, run and reported before any Phase 2 code was
written, per the r005 task specification. Numbers below are reproducible via
[`r005_graph_edge_reality_gate.py`](r005_graph_edge_reality_gate.py) →
[`r005_graph_edge_reality_gate_report.json`](r005_graph_edge_reality_gate_report.json),
using exactly the primitives r005 was told to reuse (`VaultIndex.load()` +
`SynapseStore.from_index()`) against the real vault content
(`01_ARCHITECTURE`, `02_PRODUCT`, `10_DOCUMENTATION`, `00_GOVERNANCE` —
`VaultIndex.DEFAULT_ROOTS`, which do match this repo's actual top-level
layout, unlike `FileStorageEngine`'s canonical folders noted as a gap in
r004). No new resolution logic was written for this measurement — it only
observes what the existing primitives already do.

## Headline number

**8 real, dual-resolvable, non-fixture declared edges** in a 905-note corpus
(865 canonical + 40 RAW/archived). The stop-condition threshold is 100.
8 << 100. Stop condition triggered.

This is not a surprise unique to this measurement — the codebase already
documents the same finding independently, in `edge_proposer.py`'s own
docstring: *"806-869 nodes, ~9-147 declared edges depending on snapshot,
density well under 1 edge/node. A graph that sparse does not propagate
activation."* This report adds the current exact number and the structural
reasons behind it.

## 1. How many synapses exist in the real corpus, not in test fixtures?

`SynapseStore.from_index()` over the full real corpus (raw+archived
included, so a target isn't miscounted as "unresolvable" merely because it's
excluded from the canonical-only view) produces **18 total edges** (9
forward-declared + 9 auto-generated `related_to` mirrors — `from_index()`
mirrors every declared edge with a weak reverse edge by default).

Of those 9 forward-declared edges, **one is a literal test fixture sitting
inside a real-content directory**:
`01_ARCHITECTURE/knowledge/unknown_A.md` has `id: A`, body text
`"Content for A"`, and `relations: [{target_id: B}]` — synthetic data, not a
real note, physically committed inside `01_ARCHITECTURE/knowledge/`. Its
mirror edge is fixture too. Excluding both: **8 real forward-declared edges,
16 total including mirrors**, touching **10 of 905 notes (1.1%)**.

Those 8 real edges break down as two small, unrelated clusters:
- 5 edges: one "consolidated-knowledge" note fanning out to 5 lesson notes
  (originally declared `derived_from` in frontmatter, silently downgraded to
  `related_to` on load — see §3).
- 3 edges: three "tech-stack" lesson notes converging on one
  "System Tech Stack Overview" note.

That is the entire real graph. There is no larger connected structure to
expand into.

## 2. What fraction resolve on both endpoints to notes that actually exist?

Two different denominators matter here and they tell different stories:

- **Of edges that carry a `target_id` at all**: 9/9 = 100% resolve to a note
  that exists somewhere in the corpus (raw+archived included); 4/9 = 44%
  resolve within the canonical-only subset (the rest point at RAW/archived
  notes).
- **Of every `relations:` entry declared anywhere in frontmatter**: only
  **9 of 74 (12%)** carry a `target_id` field at all. The other 88% use a
  bare `[[Wikilink Title]]` string as the target
  (e.g. `{target: "[[01_KNOWLEDGE/Design_System_Foundation]]", type: "related_to"}`)
  with no `target_id`. `SynapseStore.from_index()` only resolves
  `target_id`-style relations — it does not attempt title/wikilink
  resolution. So the dominant authoring style in this vault (wikilinks) is
  invisible to the exact mechanism r005 was scoped to reuse, independent of
  whether those wikilinks would themselves resolve to real notes.

  (`VaultIndex.resolve()` *can* resolve by title via `by_title`, so a title
  resolver could technically recover most of that 88% — but writing one
  would be new resolution logic, not "connecting what exists," and is out of
  scope for a task that stops at 8 edges regardless.)

## 3. Distribution of edge types — generic vs. semantically meaningful

Full census of all 74 declared `relations:` entries (not just the 9 that
resolve):

| type | count | in `SynapseStore.ALLOWED_RELATIONS`? |
|---|---:|---|
| `related_to` (+ `relates_to` typo variant, + 1 bare-key form) | 26 | yes (generic/weak) |
| `implements` | 21 + 1 bare-key | **no** |
| `supports` | 10 | **no** |
| `derived_from` | 7 | **no** |
| `depends_on` | 7 | yes (strong) |
| `refines` | 1 | **no** |

Only `related_to` and `depends_on` — 33/74 (45%) — are in
`SynapseStore.ALLOWED_RELATIONS`. The other 55% (`implements`, `supports`,
`derived_from`, `refines` — the vault's actual, more semantically specific
vocabulary) are **silently downgraded to generic `related_to`** by
`SynapseStore.from_index()`'s `if relation not in ALLOWED_RELATIONS:
relation = "related_to"` fallback. So even setting aside the 12%
resolution-rate problem in §2, the vault's real relation vocabulary and
`SynapseStore`'s fixed enum barely overlap — most of what *is* declared
would collapse to the least informative label anyway.

## 4. Degree distribution and hubs

Among the 10 nodes touched by the 8 real edges: two nodes account for all
connectivity —
`f5cfb84d-68d1-...` (the consolidated-knowledge note, out/in-degree 5) and
`22222222-2222-...` (System Tech Stack Overview, out/in-degree 3). Every
other touched node has degree 1. `mean_out_degree` across the whole graph
is 1.5 (`degree_stats()`), and 895 of 905 notes (99%) have **zero** edges.

This is technically "two small hubs," but at this scale "hub domination" is
a non-question — there are only 10 connected nodes total to dominate. The
Phase 2 spec's hub-capping requirement would be tested against a synthetic
graph regardless of what's found here (see gaps, §7), since the real graph
can't exercise it.

## 5. How many edges carry provenance?

**Zero of 74.** No declared relation carries who proposed it, what supports
it, or who approved it — `relations:` entries are just
`{target/target_id, type/relation}` pairs. `Synapse.evidence` (verified
run_ids) is populated only by `reinforce()`, which nothing calls yet for
these edges.

## 6. edge_proposer.py output vs. hand-authored vs. fixture

- **Hand-authored**: 73 of 74 declared relations (98.6%).
- **Fixture**: 1 of 74 (`unknown_A.md`, see §1).
- **edge_proposer.py**: **0 promoted/persisted edges.** No `synapses.json`
  or any persisted `SynapseStore` file exists anywhere in this repository —
  `edge_proposer.py`'s output has never been promoted into anything the
  query path could read, exactly as its own docstring says it's designed to
  require ("Promotion into a canonical SynapseStore or into Markdown remains
  a separate, out-of-scope, human-gated step").

Per the STOP CONDITION instruction, `edge_proposer.py` was run against the
real corpus (TIER 1 deterministic only, no `--ollama`) to see what it *would*
produce:

```
python 30_SCRIPTS/knowledge/edge_proposer.py --vault . --limit 2000 \
  --out 07_EVALUATION/r005_edge_proposer_run/edge_proposals.json \
  --metrics-out 07_EVALUATION/r005_edge_proposer_run/metrics.json
```

```
nodes: 865
candidate pairs (pre-threshold): 7754
deterministic proposals (post-threshold): 2000  (limit-capped; more available)
accepted_strong: 5      accepted_weak: 1995      accepted_total: 2000
rejected_total: 0       valid_target_ratio: 1.0
edges_per_node: 4.624
```

(Full metrics: [`r005_edge_proposer_run/metrics.json`](r005_edge_proposer_run/metrics.json);
a small sample of the accepted proposals — 5 strong, 5 weak — is kept at
[`r005_edge_proposer_run/edge_proposals_sample.json`](r005_edge_proposer_run/edge_proposals_sample.json);
the full 2000-proposal, 1.5MB dump was not committed.)

So the proposer *can* manufacture thousands of candidate edges — but:

- **99.75%** of accepted proposals are `related_to`/weak
  (`origin=proposed_weak`), the least informative label, from rare-token
  co-occurrence heuristics — exactly the generic-relation problem in §3,
  reproduced synthetically at scale.
- They are unreviewed, unpromoted `PROPOSED_PENDING_REVIEW` records by
  design. Wiring them into a live query path would mean ranking notes by
  machine-guessed, human-unverified relations — a materially different (and
  larger) trust decision than "connect the graph that already exists," and
  explicitly against the proposer's own documented promotion boundary.
- At least one sampled "strong" proposal targets a synthetic
  `path:10_DOCUMENTATION/...` fallback ID (a note with no real frontmatter
  `id`), not a genuine note identifier — a small but concrete illustration
  of why this queue needs human review before promotion, not why it should
  be promoted automatically to clear a measurement gate.

Using these to manufacture a passing edge count for Phase 1 would be exactly
what the task instructions forbid ("Do not manufacture edges to clear the
gate"), so they are reported and set aside, not promoted.

## Conclusion and recommendation

The real declared graph has 8 usable edges touching 1.1% of the corpus, no
provenance, and a fixed relation vocabulary that would flatten most of what
little *is* declared into a generic label. There is no larger connected
structure to spread activation across. Building graph expansion into the
production query path on top of this would be, in the task's own framing,
*"measurement theatre"*: it would either do nothing (no path from a seed to
anything, 99% of the time) or occasionally connect two of the ten already-
findable nodes, at the cost of new filter-bypass surface area and code for a
capability that can't be exercised.

**Recommendation: do not build Phase 2.** This is the honest outcome the
task asked for. The graph substrate (`SynapseStore`, `VaultIndex`) is sound
and already tested (`20_TESTS/p12/test_p12_synapse_store.py`); what's
missing is real declared edges, and that is a content/authoring problem
(and, per §2-3, partly a resolver-coverage and vocabulary-overlap problem),
not a wiring problem. No Phase 2/3 code, tests, or measurement were written,
per the task's stop condition.

## 7. Gaps and follow-on questions (not this task's to answer)

- **Wikilink resolution gap** (§2): 88% of declared relations use
  `[[Title]]` references `SynapseStore.from_index()` can't follow. If the
  vault's authoring convention won't change, a follow-up could extend
  `from_index()` to resolve via `VaultIndex.by_title` before target
  existence is re-measured — that is new resolution logic, a call for
  whoever owns `graph/synapse_store.py`, and it might still land under 100
  edges even if it recovered every wikilink target, since most `[[Title]]`
  references point at section headers or external concepts with no note at
  all (not measured here — worth doing before investing in a resolver).
- **Vocabulary mismatch** (§3): the vault's actual relation vocabulary
  (`implements`, `supports`, `derived_from`, `refines`) barely overlaps
  `SynapseStore.ALLOWED_RELATIONS`. Widening the enum (or adding a mapping
  table) is a design decision with plasticity/weighting consequences, not
  something to bundle into a graph-expansion wiring task.
- **`unknown_A.md` fixture-in-production-directory** (§1): a test artifact
  is sitting inside `01_ARCHITECTURE/knowledge/`, a real-content directory,
  and gets loaded by `VaultIndex` like any other note. Worth a cleanup task
  independent of r005.
- **edge_proposer.py has no promotion path at all**: even if someone wanted
  to responsibly grow the graph, there is no reviewed-promotion tool between
  `06_INBOX/edge_proposals.json` and a canonical, queryable store — building
  one is explicitly out of scope for both the proposer's own docstring and
  this task.
- The Phase 2 requirements this report never got to exercise (hub-capping,
  bounded traversal, fail-closed degradation, filter-bypass-via-edge
  adversarial tests, trace extension) remain valid engineering requirements
  *for whenever the graph substrate justifies Phase 2* — they should be
  revisited against real numbers at that time, not implemented speculatively
  now against a graph too sparse to exercise them meaningfully.
