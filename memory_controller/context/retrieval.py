from typing import List, Dict, Any


class RetrievalEngine:
    """Retrieve a bounded candidate set without loading whole-memory context."""

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
                    return list(cached)[:budget.max_notes]

        results = self.storage.query(intent=intent, lifecycle=lifecycle, types=target_types)

        if "max_notes" in classified_query:
            return results[:int(classified_query["max_notes"])]

        candidate_limit = int(classified_query.get("candidate_limit", 20))
        candidate_limit = max(1, min(candidate_limit, max(budget.max_notes * 4, budget.max_notes) if budget else 20))
        results = results[:candidate_limit]

        if self.cache and principal is not None and query_fp is not None and disclosure_level is not None and offset == 0:
            cache_limit = budget.max_notes if budget is not None else 5
            self.cache.set(results[:cache_limit], principal, query_fp, lifecycle, target_types, disclosure_level, events=["memory_updated"])  # type: ignore

        return results
