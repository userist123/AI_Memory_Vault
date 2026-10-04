# The remaining 65 typed relations in the live graph, audited

Evaluator: **perplexity** — wrote neither the notes, the proposer, nor the sampling script.  
Sample: All 65 remaining unjudged typed relations in the graph (completing the full 114-relation census), frozen at `06bfd856e4b794be…` before labelling.

| Relation | Accepted | Precision |
|---|---:|---:|
| `applies_to` | 4/10 | 40.0% |
| `depends_on` | 4/13 | 30.8% |
| `part_of` | 2/42 | 4.8% |
| **Total** | **10/65** | **15.4%** |

## Why they were rejected

| Reason | Rows |
|---|---:|
| `wrong_type` | 23 |
| `unsupported` | 17 |
| `unrelated` | 12 |
| `shared_terms_only` | 2 |
| `wrong_direction` | 1 |
| **Total Rejected** | **55** |

## Complete Graph Population Census (Wave 1 + Wave 2)

With this final batch evaluated, **100% of all 114 declared typed relations** in the live graph have now undergone independent model audit:

| Audit Batch | Sample Size | Evaluated | Accepted | Rejected | Precision |
|---|---:|---:|---:|---:|---:|
| Wave 1 (`edge_audit_v2`) | 50 | 49 | 20 | 29 | 40.8% |
| Wave 2 (`edge_audit_v2_remaining`) | 65 | 65 | 10 | 55 | 15.4% |
| **Live Graph Grand Total** | **115** | **114** | **30** | **84** | **26.3%** |

*(Note: Row 50 from Wave 1 remains unjudged due to evaluator chat cut-off, giving 114 judged edges out of 115 sampled rows).*

## What this says

1. **Extreme Dilution in Meronymy (`part_of`)**:
   `part_of` has a devastating 95.2% rejection rate (40 of 42 rejected). The evaluator repeatedly identified notes declared as `part_of` ontology slots (like `Ontology Slot: Procedures`, `Ontology Slot: Routing`, `Ontology Slot: State`) when they are actually operational logs, task ledgers, cybernetics concepts, or audit reconciliations. Ontological slots are not generic catch-all buckets for notes.

2. **Theoretical and Operational Conflation (`depends_on`)**:
   9 of 13 `depends_on` edges failed because abstract theoretical frameworks (e.g. reinforcement learning, dual-loop ultrastability, Markov transitions) were falsely asserted as operational dependencies of code modules, or because two parallel psychological theories were asserted to depend structurally on one another.

3. **Governing Scope Oversreach (`applies_to`)**:
   6 of 10 `applies_to` edges were rejected because concepts were defined in parallel rather than governing one another (e.g. variety vs. deterministic transition, transducer vs. feedback).

## Disposition and Plasticity

The 55 rejected relations represent noise that corrupts graph expansion (1-hop traversals pulling irrelevant context into prompts). Following the precedent established in Phase 0 / Wave 1, these 55 edges are scheduled for removal via `graph.plasticity.PlasticityEngine` with full byte-for-byte rollback guarantees, leaving only verified semantic relationships in the active memory vault.
