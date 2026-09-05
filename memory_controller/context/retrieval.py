from typing import List, Dict, Any


PUBLIC_LIFECYCLE = "ACTIVE"
PUBLIC_VERIFICATION = "verified"


class RetrievalSecurityError(PermissionError):
    """Raised when a public retrieval request attempts to widen the trust boundary."""


class RetrievalEngine:
    """Retrieve through the production hybrid retriever when backed by a vault filesystem.

    Public retrieval is fail-closed: only ACTIVE + verified notes may enter the
    candidate set. In-memory/test storage keeps the lightweight storage query path.
    """

    _HYBRID_ROOTS = (
        "00_GOVERNANCE", "01_ARCHITECTURE", "02_PRODUCT", "03_IMPLEMENTATION",
        "04_CONFIG", "05_DATA", "07_EVALUATION", "08_OBSERVABILITY", "09_SECURITY",
        "10_DOCUMENTATION", "01_KNOWLEDGE", "04_MEMORY", "05_RESOURCES",
    )

    def __init__(self, storage_engine, cache=None):
        self.storage = storage_engine
        self.cache = cache
        self._hybrid = None
        self._hybrid_error = None
        vault_root = getattr(storage_engine, "vault_root", None)
        if vault_root:
            try:
                from cognitive_core.vault_index import VaultIndex
                from cognitive_core.hybrid_retrieval import HybridRetriever
                index = VaultIndex.load(vault_root, roots=self._HYBRID_ROOTS)
                self._hybrid = HybridRetriever(index)
            except Exception as exc:
                self._hybrid_error = exc

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
                "Public retrieval may only access ACTIVE lifecycle; callers cannot widen the lifecycle trust boundary."
            )
        return [PUBLIC_LIFECYCLE]

    @staticmethod
    def _hit_to_record(hit) -> Dict[str, Any]:
        note = hit.note
        record = dict(note.meta)
        record.setdefault("id", note.id)
        record.setdefault("title", note.title)
        record["content"] = note.body
        record["_retrieval_score"] = hit.score
        record["_retrieval_signals"] = dict(hit.signals)
        return record

    def retrieve(self, classified_query: Dict[str, Any], principal=None, query_fp: str = None,
                 disclosure_level: str = None, budget=None, offset: int = 0) -> List[Dict[str, Any]]:
        intent = classified_query.get("intent")
        lifecycle = self._validate_lifecycle_filters(classified_query)
        target_types = classified_query.get("target_types", [])

        if self._hybrid is not None or self._hybrid_error is not None:
            if self._hybrid_error is not None:
                raise RetrievalSecurityError(
                    f"Production hybrid retrieval initialization failed: {self._hybrid_error}"
                )
            query = str(classified_query.get("query", "")).strip()
            if not query:
                raise RetrievalSecurityError("Production hybrid retrieval requires the original query text")
            candidate_limit = int(classified_query.get("candidate_limit", 20))
            max_notes = int(getattr(budget, "max_notes", 10) or 10)
            top_k = max(1, min(1000, max(candidate_limit, max_notes)))
            try:
                hits = self._hybrid.secure_search(
                    query=query,
                    top_k=top_k,
                    allowed_lifecycles=lifecycle,
                    allowed_types=target_types or None,
                )
            except Exception as exc:
                from cognitive_core.hybrid_retrieval import SecureFilterViolation
                if isinstance(exc, SecureFilterViolation):
                    raise RetrievalSecurityError(str(exc)) from exc
                raise
            return [self._hit_to_record(hit) for hit in hits]

        if self.cache and principal is not None and query_fp is not None and disclosure_level is not None and budget is not None and offset == 0:
            cached = self.cache.get(principal, query_fp, lifecycle, target_types, disclosure_level)
            if cached is not None and budget.serialized_size(cached) <= budget.soft_limit_bytes:
                return [note for note in list(cached)[:budget.max_notes]
                        if str(note.get("lifecycle", "")).upper() == PUBLIC_LIFECYCLE
                        and str(note.get("verification", "")).strip().lower() == PUBLIC_VERIFICATION]

        results = self.storage.query(intent=intent, lifecycle=lifecycle, types=target_types)
        results = [note for note in results
                   if str(note.get("lifecycle", "")).upper() == PUBLIC_LIFECYCLE
                   and str(note.get("verification", "")).strip().lower() == PUBLIC_VERIFICATION]
        if "max_notes" in classified_query:
            return results[:int(classified_query["max_notes"])]
        candidate_limit = int(classified_query.get("candidate_limit", 20))
        candidate_limit = max(1, min(candidate_limit, max(budget.max_notes * 4, budget.max_notes) if budget else 20))
        results = results[:candidate_limit]
        if self.cache and principal is not None and query_fp is not None and disclosure_level is not None and offset == 0:
            cache_limit = budget.max_notes if budget is not None else 5
            self.cache.set(results[:cache_limit], principal, query_fp, lifecycle, target_types, disclosure_level, events=["memory_updated"])
        return results
