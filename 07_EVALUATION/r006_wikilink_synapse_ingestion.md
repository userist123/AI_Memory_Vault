# r006 — Wikilink synapse ingestion

Evidence level: `CODE_VERIFIED` + `TEST_VERIFIED`. `RUNTIME_VERIFIED` is not
claimed: no production query workload was executed against this graph.

## The problem

The vault kept its connectivity in two representations that had silently
diverged:

| Representation | Read by | Edges |
|---|---:|
| `relations:` frontmatter | `SynapseStore.from_index()` — the runtime | **8** |
| `[[wikilinks]]` in note bodies | Obsidian only | 6848 raw links |

This is why the Obsidian graph looked dense while r005's edge-reality gate
returned NO-GO on a 905-note corpus. The edges were not missing. They were
written in a representation nothing in the runtime parsed.

A second defect compounded it. Of 233 `relations:` entries in the four roots
`VaultIndex` indexes, **232 were bare strings holding file paths** rather than
the mapping with `target_id` that `from_index()` requires, and most pointed at
paths that do not exist in this repository (`00_CORE/…`; the real root is
`00_GOVERNANCE`). They were invisible twice over.

## Changes

1. `VaultIndex` gained a `by_slug` file-name index, and `resolve()` now falls
   back to it. Obsidian links reference file names, not titles, and the two
   routinely differ — a map-of-content note's heading is rarely its filename.
   Id and title lookups keep their previous precedence.
2. `SynapseStore.from_index(include_wikilinks=True)` ingests wikilinks as a
   second, weaker edge source: `origin="wikilink"`, `weight=0.2` (below
   `DEFAULT_WEIGHT`), directional and deliberately **not** mirrored.
3. Navigation hubs are excluded above an in-degree threshold.
4. `30_SCRIPTS/knowledge/normalize_relations.py` rewrites resolvable string
   relations into `{type, target_id}`. Unresolvable entries are **kept, not
   deleted** — some target notes that exist on disk outside the indexed roots,
   so dropping them would destroy valid links rather than remove false ones.

## Results

| Measure | Before | After |
|---|---:|---:|
| Declared edges (`origin="declared"`) | 8 | **70** |
| Total runtime-visible edges | 18 | **301** |
| Notes touched by an edge | ~10 (1%) | **132 (14%)** |
| Mean out-degree | — | 2.47 |
| Test suite | 1158 passed | **1174 passed**, 0 failed |

## Hub threshold is not an arbitrary constant

Edge count as the in-degree cut varies:

| threshold | edges | nodes | coverage |
|---:|---:|---:|---:|
| disabled | 588 | 502 | 54% |
| 10 | 149 | 106 | 11% |
| 20 | 188 | 112 | 12% |
| 50 (chosen) | 188 | 112 | 12% |
| 200 | 188 | 112 | 12% |

The curve is **flat from 20 to 200**: the degree distribution is sharply
bimodal, a handful of map-of-content notes far above 200 and everything else
below 20. The chosen value sits in the middle of that insensitive band, so the
result does not depend on picking it precisely.

The cut costs coverage and buys signal. Disabled, 502 notes are connected but
mean out-degree is 1.26 — a star centred on the hubs. A node every note links
to carries almost no retrieval information: activation reaches it from any
seed and from it reaches everything.

## The r005 gate still says NO-GO, and that is correct

The gate counts `origin="declared"` edges only. It now reports **70**, up from
8, still under its threshold of 100.

The gate was **not modified**. Its threshold was not lowered and wikilink
edges were not relabelled to slip past it. Raising declared edges above 100 is
real remaining work, not a measurement to be renegotiated.

## Remaining gaps

1. **86% of notes still have no edge.** 390 notes connect *only* to hubs, so
   they are semantically isolated even though Obsidian draws them as linked.
   Orphan count and connectivity are different metrics; the recent drop from
   656 to 39 orphans improved navigation, not retrieval.
2. **Nothing consumes this yet.** `MemoryController` still imports no graph
   module. This work makes edges *readable*; it does not wire expansion into
   the query path — deliberately, since that was r005's NO-GO.
3. **76 unresolved string relations and 33 legacy `relation:`/`target:`
   dicts** remain untouched, reported rather than rewritten.
4. `prune()` treats any `origin != "declared"` edge as prunable when it has no
   activations, so a prune pass would currently remove every wikilink edge.
   Nothing in production calls `prune()`, but the interaction must be settled
   before plasticity (reinforce/decay) is wired.
