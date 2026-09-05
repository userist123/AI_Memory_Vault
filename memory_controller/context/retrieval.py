from typing import List, Dict, Any


PUBLIC_LIFECYCLE = "ACTIVE"
PUBLIC_VERIFICATION = "verified"


class RetrievalSecurityError(PermissionError):
    """Raised when a public retrieval request attempts to widen the trust boundary."""


class RetrievalEngine:
    """Retrieve a bounded candidate set behind the public memory trust boundary.

    Public retrieval is fail-closed: only ACTIVE + verified notes may enter the
    candidate set. Callers may not request REVIEW, RAW, ARCHIVED, or unverified
    material through this path. Cognitive/internal paths that legitimately need
    REVIEW must use their explicit internal APIs instead of weakening this one.
    """

    def __init__(self, storage_engine, cache=None):
        self.storage = storage_engine
        self.cache = cache

    @staticmethod
    def _validate_lifecycle_filters(classified_query: Dict[str, Any]) -> List[str]:
        requested = classified_query.get("lifecycle_filters", [])
        if requested is None:
            return [PUBLIC_LIFECYCLE]
        normalized = [
            str(value.value if hasattr(value, "value") else value).upper()
            for value in requested
        ]
        if not normalized:
            return [PUBLIC_LIFECYCLE]
        if any(value != PUBLIC_LIFECYCLE for value in normalized):
            raise RetrievalSecurityError(
                "Public retrieval may only access ACTIVE lifecycle; "
                "callers cannot widen the lifecycle trust boundary."
            )
        return [PUBLIC_LIFECYCLE]

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
        lifecycle = self._validate_lifecycle_filters(classified_query)
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
                    return [
                        note for note in list(cached)[:budget.max_notes]
                        if str(note.get("lifecycle", "")).upper() == PUBLIC_LIFECYCLE
                        and str(note.get("verification", "")).strip().lower() == PUBLIC_VERIFICATION
                    ]

        results = self.storage.query(intent=intent, lifecycle=lifecycle, types=target_types)
        results = [
            note for note in results
            if str(note.get("lifecycle", "")).upper() == PUBLIC_LIFECYCLE
            and str(note.get("verification", "")).strip().lower() == PUBLIC_VERIFICATION
        ]

        if "max_notes" in classified_query:
            return results[:int(classified_query["max_notes"])]

        candidate_limit = int(classified_query.get("candidate_limit", 20))
        candidate_limit = max(1, min(candidate_limit, max(budget.max_notes * 4, budget.max_notes) if budget else 20))
        results = results[:candidate_limit]

        if self.cache and principal is not None and query_fp is not None and disclosure_level is not None and offset == 0:
            cache_limit = budget.max_notes if budget is not None else 5
            self.cache.set(results[:cache_limit], principal, query_fp, lifecycle, target_types, disclosure_level, events=["memory_updated"])  # type: ignore

        return results
