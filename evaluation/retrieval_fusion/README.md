# Retrieval Fusion Laboratory (R1 → R4)

This laboratory provides an isolated, reproducible experimental testbed to measure candidate discovery and evidence coverage across four layers of multi-signal retrieval over the real AI Memory Vault.

## Retrieval Strategies Under Test

- **R1 (Semantic Only)**: Dense token overlap and confidence scoring via [`RelevanceScorer`](file:///memory_controller/context/relevance_scoring.py).
- **R2 (Semantic + Lexical BM25)**: R1 fused with Okapi BM25 score ranking via [`BM25Ranker`](file:///memory_controller/financial_search.py).
- **R3 (Semantic + Lexical + Entity)**: R2 enriched with exact entity anchor and tag match boosting.
- **R4 (Semantic + Lexical + Entity + Graph)**: R3 expanded with 1-hop / 2-hop relational graph neighbor discovery via [`MultiGraphMemory`](file:///cognitive_core/multi_graph.py).

## Experimental Rules & Invariants
- **No Mock Corpora**: Ingests real Markdown vault notes from `00_CORE` through `99_SYSTEM`.
- **Protected Core Safe**: Does not modify production `ContextPackBuilder`, `RetrievalEngine`, or `Council_Runtime_Profile.yaml`.
- **Decoupled Evaluation**: Measures `candidate_recall`, `required_fact_recall` (Evidence Coverage), `final_context_fact_recall`, and `answer_correctness` separately.
