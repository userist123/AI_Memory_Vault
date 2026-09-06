# R001 C4 — Graph activation forensics

Evidence level: `CODE_VERIFIED` + `TEST_VERIFIED` on the CODEX branch.

Baseline: `e43cc81e09789e284ef35a7e326297194f429a9e`

## Finding

`cognitive_core/spreading_activation.py` computed an edge-weighted propagation value and immediately overwrote it with `score * decay ** (hop + 1)`. Therefore edge weights did not influence activation in the runtime path.

This was reproduced by inspection of `_propagate_on_graph`; it is not inferred from the module name or documentation.

## Change

The propagation value now uses a bounded positive edge factor. Weights in `(0, 1]` scale the signal, while values above `1` retain full strength. Zero and negative weights do not propagate activation. No graph topology, base ranking, or storage behavior was changed.

## Regression evidence

Test command:

```text
python -m pytest -q cognitive_core/tests/test_multi_graph.py
```

Observed result:

```text
..........                                                               [100%]
10 passed in 0.04s
```

`test_spreading_activation_respects_edge_weight` constructs identical seed paths with weights `0.1` and `0.9` and asserts the stronger edge receives greater activation.

## Remaining limitation

This proves edge-weight sensitivity in the activation unit, not end-to-end retrieval usefulness or causal memory benefit. Those require a fixed candidate set and runtime evaluation traces.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
