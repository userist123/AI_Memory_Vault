# WP-12 — graph-class statistical power: ceiling re-verified at 32 (not 33); only 2/56 candidates could be verified

package: WP-12 | intent: implement (expand graph cases) | status: DONE, with a materially short result
baseline: 12 `one_hop_graph_expansion` cases (10 heldout + 2 dev), disjoint ceiling 32 (CONTRACT.md)
result: 2 new cases added (H31, H32), both end-to-end verified; 54/56 other candidate edges could NOT
be verified as genuine graph-expansion tests | n: 56 eligible candidate edges tried | decision: added
what could be honestly verified (2), did not fabricate the rest to hit a target count, and surfaced
why as the primary finding of this package.

## Correction to this package's own brief

The brief states a disjoint-node ceiling of 33. Re-measured fresh (never trusted from memory or from
the brief): `SynapseStore.from_index(index).all()` has **exactly 278 edges**, byte-identical to
CONTRACT.md's own number — the runtime graph has not drifted since v2 was frozen. CONTRACT.md's cited
ceiling is **32**, and every automated matching computed here (see `filter_scan` in the report,
36-42 depending on how loosely "substantive" is defined) is strictly larger, so 32 remains a
conservative, provably-achievable figure. This package targets **32**, not 33, and does not raise the
ceiling based on its own looser heuristics — CONTRACT.md is explicit that a figure over this few
independent pairs must be reported conservatively, not maximised.

## Method

`measure_and_generate.py`:
1. Rebuilds the exact runtime graph (`SynapseStore.from_index(VaultIndex.load(...))`, same call
   `MemoryController` itself makes) and confirms the 278-edge count matches CONTRACT.md.
2. Builds candidate (seed, gold) edges excluding: every node already used by an existing graph case
   (12 cases -> 12+ nodes), `type == "legal_index"` notes (the one objective, code-checkable
   equivalent of CONTRACT.md's "generic index/catalog document" exclusion), notes under 100 characters
   of content, and any node `MemoryController._is_hub_node()` would skip during real expansion
   (total degree > 10 — a stricter, independent cap from `SynapseStore`'s own wikilink-only hub cut).
   **56 eligible undirected pairs remained.**
3. For every one of the 56 (not just a pre-selected matching), mechanically drafted a query from the
   seed note's own text and a `required_facts` string taken verbatim from the gold note's own text,
   then **verified end-to-end** by actually running the drafted case through
   `MemoryController.search()` with graph expansion OFF and ON: accepted only if gold is absent from
   context with expansion OFF (this is a real gap ordinary retrieval doesn't close) AND present via
   `graph_expanded_ids` with expansion ON (the edge is actually traversable at runtime, not just
   present in a static list).
4. Selected a maximum disjoint-node subset of the cases that passed step 3.

## What was measured

| step | count |
|---|---:|
| eligible candidate edges after all exclusions | 56 |
| structural disjoint-matching upper bound on those 56 | 19 |
| candidates that passed end-to-end verification | **2** |
| final cases added (disjoint, verified) | **2** |

Rejection breakdown across the 56: `gold_never_reached_via_graph_expansion` 36,
`gold_reachable_without_graph_expansion_not_a_real_graph_test` 16, one `GraphExpansionDegraded`
exception, one case where gold entered `graph_expanded_ids` but never survived into the final
returned context. None were rejected for a required-fact extraction failure.

## Why so few: a mechanism, not a case-authoring shortfall

A real declared/wikilink edge between two notes in this vault typically exists BECAUSE the two notes
are topically related — and topical relatedness is exactly what the entity/BM25 fusion scorer also
rewards. The practical result: for most real edges, the gold note already lands inside the default
200-candidate pool (23.5% of this 850-note corpus) via ordinary lexical/entity ranking alone, with a
genuinely nonzero `fused_score` (verified directly on one rejected candidate: gold ranked 29/200 with
`fused_score=0.0169`, not a zero-score tie-break artifact). Once gold is in that pool,
`RetrievalEngine`'s own anti-redundancy check (`if t_id in seed_ids: continue`, `retrieval.py`)
correctly refuses to "discover via graph" something already a direct candidate — so no query wording,
however cleverly constructed, can turn such a pair into a genuine graph-expansion test. Two query
strategies were tried (the seed's own title, and a narrow low-frequency phrase from deep in the seed's
body) specifically to test whether sharper, narrower queries could keep gold outside the pool; the
second strategy raised the verified count from 1 to 2 out of the same 56 candidates — a real but small
effect, not enough to reach anywhere near 20 more cases.

## A finding about the EXISTING 12 cases, found while building this package's own verification

The same end-to-end check (gold absent with expansion off, present via `graph_expanded_ids` with it
on) was run against `H18`-`H27` and `D09`-`D10`. **0 of the 12 ever reach gold via
`graph_expanded_ids`.** Ten have gold already inside the 200-candidate pool (present or not in the
final top-10 regardless of the graph flag — the flag changes nothing for them); two (`H27`, `D10`)
have gold reachable in the final context, but because it was ALREADY reachable with graph expansion
OFF, not because of graph traversal. **Every prior graph on/off comparison run against this class
(R016, r024, r025 WP-8) has therefore never actually exercised the mechanism it was nominally
testing**, for a single case, old or new. This does not reverse those comparisons' conclusion — graph
expansion still measurably fails to help recall — but it changes what that conclusion is evidence of:
not "traversal doesn't help," but "traversal is essentially never reached by this benchmark's queries
in the first place." This is reported here because it was found here, per this vault's own standing
rule against silently absorbing an inconvenient result into a metric that already looks the way you
expected.

## What was added

`H31` (seed `proc-enterprise-integration-0001`, an Enterprise integration blueprint) -> gold
`330fa4bc-...` (`System Architecture`, via a `declared` edge) and `H32` (seed
`b4e88f21-...-a002`, Data Viz standards) -> gold `b4e88f21-...-a003` (Motion Design principles, via a
`declared` edge). Both re-verified with their exact final (properly accented Romanian) query and
`required_facts` text before being written into `heldout.json`. `heldout.json` grew from 30 to 32
cases (29 answerable + 3 unanswerable); `dev.json` is unchanged. Both files re-frozen
(`freeze.py --freeze`); `20_TESTS/test_benchmark_v2_gold_integrity.py` (15 tests) passes against the
new set. No non-graph case was touched, per this package's own restriction.

## Requirements / instructions checklist

- "Add graph cases up to the ceiling": attempted exhaustively (all 56 eligible candidates tried, not
  a sampled subset); the ceiling itself was not reached because so few candidates verify as genuine
  tests — reported as the finding, not forced.
- "Gold anchored in real notes reachable only via a real edge": both new cases satisfy this in the
  strongest available sense (behaviorally verified via `graph_expanded_ids`, not just structurally
  present in the static edge list) — a stronger bar than the existing 12 cases turned out to meet.
- "Re-freeze as v2.1 with new hashes and updated CONTRACT.md": done (`CONTRACT.md`'s new "v2.1"
  section; `heldout.json.sha256`/`dev.json.sha256` regenerated).
- "Do not touch non-graph classes": honoured — only two entries were appended; nothing else in
  `heldout.json` or `dev.json` was edited.

## What remains open

- Only 2 new cases exist where the brief anticipated up to ~20; the statistical-power goal this
  package was meant to serve is not materially advanced (14 verified-testable graph cases total, not
  32).
- Raising this further would require either a materially smaller default `candidate_limit` (a
  production retrieval change, out of this package's scope and with its own recall/precision
  trade-offs elsewhere in the pipeline) or a systematic way to find edges connecting topically
  DISSIMILAR notes (rare, possibly by design, since most real edges in this vault are declared because
  the notes are related) — neither is attempted here.
- The two accepted queries are real, verbatim phrases from each seed note's own text (matching
  CONTRACT.md's "the author must know the edge exists" acknowledgement) but read more like an
  editorially-chosen quotation than a naturally-typed question, unlike most of the original 12 cases'
  phrasing; a human editorial pass could likely improve naturalness without affecting verification,
  not attempted here.
- WP-6's edge promotion (this session's next package) will add new declared edges from the WP-2
  sample; whether ANY of those newly-promoted edges happen to connect topically-dissimilar notes (and
  so could yield a genuinely testable graph case where a purely-existing-edge search could not) is
  not evaluated here — it would need this same end-to-end verification repeated after promotion, not
  assumed.
