# R009b v2 — benchmark contract

Supersedes v1, which is retained and marked `INVALID`. v2 is not a repair of
v1; it is a new frozen set with its own hashes.

## Why v1 was invalid

Every one of v1's 36 held-out cases and 12 dev cases referenced one of two
gold notes: `note_agents_contract` and `note_vault_cognitive_rules`. Neither
exists in the vault, and the runner never injected them. Scoring is

    candidate_recall = bool(gold & set(candidate_ids))

so the intersection was always empty: **candidate recall and context recall
were structurally 0 for every case, in every configuration.** A graph-on
versus graph-off comparison would have returned 0 versus 0 and reported "no
significant difference" about nothing.

The identifier format is the tell. Real vault ids look like
`knw-agent-memory-trace-protocol-0001` or a UUID; `note_agents_contract` is
author-invented placeholder text that was never checked against the corpus.

This was masked by a second defect: v1 hashed raw bytes, so on a Windows
checkout CRLF conversion tripped the freeze guard and the runner refused to
start. The false alarm stopped anyone from reaching the real failure.

## Corpus

The production index, `VaultIndex.load()` with `DEFAULT_ROOTS`, export residue
excluded: **842 notes**. Every non-abstain case must reference a gold id that
resolves in exactly this corpus, enforced by
`20_TESTS/test_benchmark_v2_gold_integrity.py`, separately for dev and
held-out.

## The graph-reachable subpopulation — read before quoting any graph result

`MemoryController.search()` traverses **one hop** along `neighbors(seed)`,
which returns outgoing edges only. Wikilink edges are not mirrored, so
direction matters. The class is therefore named `one_hop_graph_expansion`,
not `cross_cluster_multihop`: the runtime does not implement multi-hop, and a
class name asserting otherwise would be a false contract.

Measured on the current graph:

| Population | Count |
|---|---:|
| Edges in the runtime graph | 278 |
| Edges with both endpoints real, substantive notes | 217 |
| Notes that can act as a seed (have an out-edge) | 90 |
| Notes reachable as gold (have an in-edge) | 78 |
| **Graph cases with pairwise-disjoint nodes** | **32** |

That last number is the ceiling on statistical power. Graph results describe
**78 of 842 notes (9%)**, not the corpus. They must never be pooled with the
ordinary retrieval classes as if they shared a population, and any Wilson or
McNemar figure computed over fewer than ~30 independent pairs should be
reported with its width, not as a verdict.

v2 uses 10 graph cases in held-out and 2 in dev, well inside the disjoint
ceiling, at the cost of accepting that this class alone cannot resolve small
effects.

## Contamination

For `exact_identifier_lookup`, `paraphrase`, `synonym_substitution` and
`lexical_trap`, the answer exists independently of the graph, so queries were
written from an information need.

For `one_hop_graph_expansion` the author must know the edge exists in order to
build a case that tests it. That is not a breach of held-out discipline; it is
the only possible form of the test. It is declared here rather than hidden.

## Freeze

Canonical bytes = UTF-8 with every CRLF and lone CR normalised to LF. Freezing
and verification use that same representation, so a checkout cannot break the
guard while a real character change still does. Both properties are proven:
altering `Very High.` to `Very Low.` produces a mismatch, and converting the
file to CRLF produces an identical digest. `.gitattributes` additionally pins
`eol=lf` so the conversion does not happen in the first place.

    python freeze.py            # verify
    python freeze.py --freeze   # re-freeze, only for a deliberate new version
