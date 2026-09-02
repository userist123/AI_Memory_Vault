# P1 Context Packing Laboratory

This laboratory investigates and mitigates the primary bottleneck discovered during the R1→R4 Multi-Signal Retrieval evaluation: **`PACKING_FAILURE`**.

## Background & Problem Formulation
In the R4 Multi-Signal benchmark:
- **Candidate Discovery**: `70.0%` candidate recall, `76.7%` required fact coverage.
- **Final Context Delivery**: Dropped to `10.0%` context fact recall due to production `ContextPackBuilder` whole-document degradation and zlib compression.
- **Full-Context Benchmark**: `71.1%` context fact recall and `68.3%` answer accuracy on 7B.

## Packing Strategies Under Test
- **P0 (Current Production Baseline)**: `ContextPackBuilder` + `ProgressiveDisclosure.full_document()` with `apply_degradation()` whole-note pruning and byte compression.
- **P1 (Full Context Diagnostic Oracle)**: Ingests all candidate note contents without progressive clipping as a reference upper bound.
- **P2 (Section-Aware Extractive Packing)**: Parses Markdown headings, calculates deterministic section utility scores, and packs highest-scoring sections within the token budget.
- **P3 (Section-Aware + Fact & Invariant Protection)**: P2 + preserves critical invariant clauses, negation keywords (`NOT`, `NEVER`, `MUST NOT`, `CANNOT`, `GATED`, `IMMUTABLE`), and required facts.
- **P4 (Section-Aware + Fact Protection + Deduplication)**: P3 + provenance-preserving deduplication across overlapping notes.

## Invariants & Rules
- **No Production Modifications**: `ContextPackBuilder` and cognitive core modules remain frozen.
- **Lossless Semantic Selection**: Extraction only, zero creative paraphrasing or lossy LLM summarization.
- **Isolated Evaluation**: Same 15 LoCoMo queries, same local Ollama models (`qwen2.5-coder:3b`, `qwen2.5-coder:7b`), same R4 candidates.
