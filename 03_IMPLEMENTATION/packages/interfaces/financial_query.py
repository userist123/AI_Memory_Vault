import json
import hashlib
import uuid
from typing import List, Optional, Dict

import jsonschema
from .financial_schema import FINANCIAL_NOTE_SCHEMA
from .storage.sqlite_engine import SQLiteStorageEngine
from .financial_search import MultiLayeredFinancialSearchEngine

# Configuration: vector search disabled by default
ENABLE_VECTOR_SEARCH = False

class FinancialQueryEngine:
    """Engine for ingesting financial notes and performing layered search.

    * Ingests a validated note dict, adds required front‑matter, stores it in the
      SQLite WAL database via ``SQLiteStorageEngine``.
    * Search runs BM25 lexical retrieval (via ``MultiLayeredFinancialSearchEngine``)
      and optionally falls back to vector similarity when ``ENABLE_VECTOR_SEARCH``
      is enabled.
    """

    def __init__(self, storage: SQLiteStorageEngine):
        self.storage = storage
        self.search_engine = MultiLayeredFinancialSearchEngine(storage)

    def _generate_id(self) -> str:
        return str(uuid.uuid4())

    def _hash_note(self, note: dict) -> str:
        # Deterministic SHA‑256 over canonical JSON representation
        canonical = json.dumps(note, sort_keys=True).encode("utf-8")
        return hashlib.sha256(canonical).hexdigest()

    def ingest_financial_note(self, note: dict) -> str:
        """Validate, enrich and store a financial note.

        Returns the generated UUID of the stored note.
        """
        # 1. Validate against schema (AI may call this with is_ai_agent=False)
        jsonschema.validate(instance=note, schema=FINANCIAL_NOTE_SCHEMA)

        # 2. Enrich with required front‑matter fields
        note_id = self._generate_id()
        now = "{{NOW}}"  # placeholder – the controller will replace at write time
        provenance = {
            "source_type": "execution",  # per trust‑boundary invariants
            "source_ref": "ingest_financial_note",
            "timestamp": now,
        }
        frontmatter = {
            "id": note_id,
            "type": "knowledge",
            "lifecycle": "REVIEW",
            "category": "financial",
            "tags": note.get("tags", []),
            "created": note.get("date") or now,
            "updated": note.get("date") or now,
            "provenance": provenance,
            "confidence": note.get("confidence", "unknown"),
            "verification": note.get("verification", "unverified"),
        }
        stored_note = {"id": note_id, "frontmatter": frontmatter, "content": note, **frontmatter}
        self.storage.set(note_id, stored_note)
        self.search_engine.index_note(stored_note)
        return note_id

    def search(
        self,
        query: str,
        filters: Optional[Dict] = None,
        top_k: int = 10,
        limit: Optional[int] = None,
        **kwargs
    ) -> List[Dict]:
        """Run layered search.

        * Base BM25 lexical search via ``MultiLayeredFinancialSearchEngine``.
        * Optional filtering on ``symbol``, ``date`` range, and ``tags``.
        * Optional vector fallback (currently disabled).
        """
        effective_limit = limit if limit is not None else top_k
        effective_limit = max(0, min(int(effective_limit), 10000))
        results = self.search_engine.search(query, top_k=effective_limit, **kwargs)
        if filters:
            def match(note: Dict) -> bool:
                full_note = self.storage.get(note.get("id")) or note
                fm = full_note.get("frontmatter", {}) if isinstance(full_note.get("frontmatter"), dict) else {}
                content = full_note.get("content", {})
                if isinstance(content, str):
                    try:
                        content = json.loads(content)
                    except Exception:
                        content = {}
                if not isinstance(content, dict):
                    content = {}

                note_symbol = content.get("symbol") or full_note.get("symbol") or fm.get("symbol")
                if "symbol" in filters and note_symbol != filters["symbol"]:
                    return False

                if "date_from" in filters or "date_to" in filters:
                    note_date = content.get("date") or full_note.get("date") or fm.get("date") or full_note.get("created")
                    if not note_date:
                        return False
                    if "date_from" in filters and note_date < filters["date_from"]:
                        return False
                    if "date_to" in filters and note_date > filters["date_to"]:
                        return False

                if "tags" in filters:
                    note_tags = set(content.get("tags") or full_note.get("tags") or fm.get("tags") or [])
                    if not note_tags.intersection(set(filters["tags"])):
                        return False
                return True
            results = [r for r in results if match(r)]
        enriched_results = []
        for r in results[:top_k]:
            full = self.storage.get(r.get("id")) or {}
            content = full.get("content", "")
            content_str = json.dumps(content) if isinstance(content, dict) else str(content)
            enriched = {**r, **full, "content": content_str}
            enriched_results.append(enriched)

        return enriched_results
