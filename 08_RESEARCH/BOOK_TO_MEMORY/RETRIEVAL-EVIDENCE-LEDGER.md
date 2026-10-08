# Retrieval Evidence Ledger — Book-to-Memory Research

## Scope
This ledger separates historical benchmark evidence from the current main branch. Historical results are not treated as the current baseline.

## Prior evidence
- Retrieval Benchmark v3 was pre-registered on a 948-note snapshot at commit b3ada1b54.
- It contained 160 cases: 60 direct, 40 multi-hop, 30 conceptual, 30 abstain.
- On the graph-off arm, candidate recall was 101/130 and context recall was 39/130.
- The graph budget experiment did not justify adopting budget 5: it gained 1 context-recall case but lost 14 and added 4.69 nodes/query against the pre-registered thresholds.
- The same report showed that graph expansion can move a previously retrieved correct memory out of the final context. This is evidence against assuming that more associative expansion is automatically better.

## Why this does not close H1
The historical snapshot `b3ada1b54` is not the current branch state, so the old numeric results cannot be presented as the current state. Any current comparison must identify and freeze its own corpus commit/hash.
The current retrieval code also contains an experimental HybridRetriever with BM25, entity matching, optional dense embeddings, and RRF, but its module explicitly states that it is not wired into MemoryController.search().
Therefore H1 must measure the actual current production path first, then compare any variant against that frozen baseline.

## Current H1 focus
The experiment targets weak lexical overlap and associative/contextual recoverability. It must distinguish:
- candidate-generation failure
- ranking failure
- context-pack/disclosure failure
- graph expansion regression
- abstention/false retrieval behavior

## Gate
No production retrieval change is justified from the historical v3 result alone.
No biological or cognitive architecture mechanism is promoted directly into production.
