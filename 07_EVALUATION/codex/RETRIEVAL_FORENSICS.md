# Retrieval forensics

| Question | Finding | Evidence |
|---|---|---|
| default retrieval | `MemoryController.search()` calls `RetrievalEngine.retrieve()` then `RelevanceScorer`; default scoring is token/field based | CODE_VERIFIED |
| semantic provider | `DeterministicSemanticProvider` uses token-set Jaccard, explicitly not embeddings | CODE_VERIFIED |
| live vector retrieval | `SemanticRetrieval` can use Ollama embeddings + Qdrant, but is optional and not wired into `MemoryController.search()` | CODE_VERIFIED |
| graph activation | `ranked_search()` is an opt-in wrapper; it reorders returned results and does not alter base search | CODE_VERIFIED |
| lifecycle | default search excludes RAW; recall marks REVIEW `_cognitive_unverified`; supersession lineage is handled in `RecallEngine` | CODE_VERIFIED / TEST_VERIFIED |
| abstention | `RecallEngine` has a threshold and returns no results for irrelevant queries; explicit controller-level abstention contract was not established | TEST_VERIFIED / UNVERIFIED |

Targeted retrieval command:

```text
python -m pytest -q cognitive_core/tests/test_recall.py cognitive_core/tests/test_recall_review_safe.py cognitive_core/tests/test_ranked_search.py cognitive_core/tests/test_multi_graph.py cognitive_core/tests/test_qdrant_retrieval.py tests/test_review_retrieval_challenge_v1.py memory_controller/tests/test_supersession_phase43.py
32 passed in 0.33s
```

Conclusion: lexical/deterministic retrieval and review-safe recall are proven by code and tests. A production hybrid semantic retrieval path is only PARTIAL/UNVERIFIED.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
