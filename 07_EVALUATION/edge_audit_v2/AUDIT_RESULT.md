# The typed relations in the live graph, audited

Evaluator: **perplexity** — wrote neither the notes, the proposer, nor the sampling script.
Sample: 50 of the 114 typed relations in the graph, stratified with seed 42, frozen at `815d00d131297670…` before labelling.

| Relation | Accepted | Precision |
|---|---:|---:|
| `part_of` | 11/30 | 37% |
| `depends_on` | 5/11 | 45% |
| `applies_to` | 3/7 | 43% |
| `caused` | 1/1 | 100% |
| **Total** | **20/49** | **41%** |

## Why they were rejected

| Reason | Rows |
|---|---:|
| `wrong_type` | 14 |
| `unrelated` | 10 |
| `unsupported` | 2 |
| `wrong_direction` | 2 |
| `shared_terms_only` | 1 |

## What this says

Three of every five typed relations a graph traversal can follow are wrong.
`wrong_type` and `unrelated` account for 24 of the 29 rejections: these are not
near misses, they are relations asserted between notes that have no such
relationship.

The pattern the evaluator found repeatedly: a cybernetics concept declared
`part_of` an ontology slot it has nothing to do with, and Phase 0 audit
documents — the baseline, the module matrix, the decision register — declared
`part_of` the slot for operational procedures. Those are reports about the
repository, not procedures.

This is a different population from the 3/25 measured in PR #172. That sample
was drawn from the proposer's output, which was never promoted. This one is
what the graph actually holds, and it is declared in note frontmatter by
whoever wrote the note.

## What happens next, and what must not

29 rejected rows are 29 specific edges, not a licence to delete by proportion.
The purge removes **only the audited rows**, through `graph/plasticity.py`,
with a journal and a rollback proved byte for byte. The remaining 65 typed
relations stay until they are audited too.

## Deviations, declared

- The evaluator saw 700-character excerpts, not the 1,200 in the frozen sample:
  the 128 KB file did not fit in its chat and went as five batches of ten.
- Row 50 came back without a verdict. It counts as unjudged, not as either
  outcome, so every figure above is over 49 rows.
