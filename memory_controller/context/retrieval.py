from typing import List, Dict, Any

class RetrievalEngine:
    """Retrieve memory entries based on classified query and context budget.

    This simplified engine works with an abstract StorageEngine (injected at runtime).
    It respects lifecycle filters and max notes limits.
    """

    def __init__(self, storage_engine, cache=None):
        self.storage = storage_engine
        self.cache = cache
    def retrieve(self, classified_query: Dict[str, Any], principal=None, query_fp: str = None, disclosure_level: str = None, budget=None) -> List[Dict[str, Any]]:
        """Return a list of memory notes matching the query.

        Parameters:
            classified_query: The classified query dict.
            principal: Optional Principal for cache key.
            query_fp: Optional query fingerprint for cache key.
            disclosure_level: Optional disclosure level for cache key.
            budget: Optional ContextBudget for cache size checks.
        """
        intent = classified_query.get("intent")
        lifecycle = classified_query.get("lifecycle_filters", [])
        target_types = classified_query.get("target_types", [])
        # If cache and all cache parameters are provided, attempt cache lookup
        if self.cache and principal is not None and query_fp is not None and disclosure_level is not None and budget is not None:
            cached = self.cache.get(principal, query_fp, lifecycle, target_types, disclosure_level)
            if cached is not None:
                import json
                usage = sum(len(json.dumps(item, default=str)) for item in cached)
                if usage <= budget.hard_context_budget:
                    return cached
        # Query storage (abstract API expected)
        results = self.storage.query(intent=intent, lifecycle=lifecycle, types=target_types)
        max_notes = classified_query.get("max_notes")
        if max_notes is not None:
            results = results[:max_notes]
        if self.cache and principal is not None and query_fp is not None and disclosure_level is not None:
            self.cache.set(results, principal, query_fp, lifecycle, target_types, disclosure_level, events=["memory_updated"])  # type: ignore
        return results
