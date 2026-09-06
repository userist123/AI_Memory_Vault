from typing import List, Dict, Any, Optional

from .candidate_generation import generate_candidates, DEFAULT_CANDIDATE_LIMIT


class RetrievalEngine:
    """Retrieve a bounded candidate set without loading whole-memory context.

    Candidate selection is query-driven: the hard gate (RAW exclusion,
    lifecycle filter, type filter) is applied first via
    ``self.storage.query()``, and only the notes that survive that gate are
    ever handed to ``generate_candidates()`` for lexical + entity ranking.
    See ``candidate_generation.py`` for the ranking contract and
    ``20_TESTS/regression/test_candidate_generation_call_path.py`` for an
    AST-level proof that the gate always runs first.
    """

    def __init__(self, storage_engine, cache=None):
        self.storage = storage_engine
        self.cache = cache

    def retrieve(
        self,
        classified_query: Dict[str, Any],
        principal=None,
        query_fp: str = None,
        disclosure_level: str = None,
        budget=None,
        offset: int = 0,
        query: Optional[str] = None,
        trace_sink: Optional[Dict[str, Any]] = None,
    ) -> List[Dict[str, Any]]:
        intent = classified_query.get("intent")
        lifecycle = classified_query.get("lifecycle_filters", [])
        target_types = classified_query.get("target_types", [])

        if (
            self.cache
            and principal is not None
            and query_fp is not None
            and disclosure_level is not None
            and budget is not None
            and offset == 0
        ):
            cached = self.cache.get(principal, query_fp, lifecycle, target_types, disclosure_level)
            if cached is not None:
                # Never let stale/oversized cache entries bypass the current budget.
                if budget.serialized_size(cached) <= budget.soft_limit_bytes:
                    if trace_sink is not None:
                        trace_sink.update({
                            "source": "cache",
                            "query": query or "",
                            "candidate_limit": None,
                            "candidates_considered": None,
                            "per_generator": {},
                            "fused_ranking": [],
                        })
                    return list(cached)[:budget.max_notes]

        # HARD GATE: RAW exclusion, lifecycle filter, and type filter are all
        # enforced inside storage.query() itself (see StorageEngine.query /
        # FileStorageEngine.query). Everything below this line only reorders
        # and truncates `results` -- it never re-queries storage and never
        # unions in notes from any other source, so nothing this gate would
        # have excluded can re-enter through candidate ranking.
        results = self.storage.query(intent=intent, lifecycle=lifecycle, types=target_types)

        if "max_notes" in classified_query:
            # Legacy raw-truncation contract for direct engine callers that
            # bypass ranking entirely (see test_retrieval_engine_respects_max_notes).
            # Deliberately left untouched: MemoryController.search() never
            # sets `max_notes`, so this branch is unreachable from the
            # production query path this task changes.
            sliced = results[: int(classified_query["max_notes"])]
            if trace_sink is not None:
                trace_sink.update({
                    "source": "max_notes",
                    "query": query or "",
                    "candidate_limit": int(classified_query["max_notes"]),
                    "candidates_considered": len(results),
                    "per_generator": {},
                    "fused_ranking": [],
                })
            return sliced

        requested_limit = int(classified_query.get("candidate_limit", DEFAULT_CANDIDATE_LIMIT))
        ceiling = max(budget.max_notes * 4, budget.max_notes) if budget else DEFAULT_CANDIDATE_LIMIT
        candidate_limit = max(1, min(requested_limit, ceiling))

        # Fail closed, no silent fallback: generate_candidates() is a pure
        # function over the already-gated `results` list. If it (or the
        # BM25/tokenize/entities primitives it reuses) raises, that exception
        # propagates unchanged -- there is no except-and-degrade-to-head-N
        # here. See CandidateGenerationError and
        # test_candidate_generation_fails_closed.
        candidates, trace = generate_candidates(query or "", results, candidate_limit)
        if trace_sink is not None:
            trace_sink.update({"source": "generated", **trace.to_dict()})
        results = candidates

        if self.cache and principal is not None and query_fp is not None and disclosure_level is not None and offset == 0:
            cache_limit = budget.max_notes if budget is not None else 5
            self.cache.set(results[:cache_limit], principal, query_fp, lifecycle, target_types, disclosure_level, events=["memory_updated"])  # type: ignore

        return results
