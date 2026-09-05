# controller.py
"""Full Memory Controller implementation with authorizer, validation, provenance, cache, audit logging.
"""
import enum
from typing import Any, Dict, Optional, List
import os
import json
from datetime import datetime, timezone, timedelta

# Core imports
import hashlib
import threading
from .authorizer import Authorizer, DefaultAuthorizer, Principal, Operation
from .validation.schema import validate_frontmatter
from .validation.provenance import validate_provenance
from .validation.supersession import SupersessionEnforcer
from .audit.logger import audit_event
from .cache import Cache

# Security utilities
from .security import sanitize_query, check_path_traversal, detect_cache_poisoning, check_query_size
from .security.pagination_token import PaginationToken, MissingHMACSecretError, InvalidPaginationTokenError

# Context components
from .context.query_classifier import QueryClassifier
from .context.retrieval import RetrievalEngine
from .context.relevance_scoring import RelevanceScorer
from .context.progressive_disclosure import ProgressiveDisclosure
from .context.budget import ContextBudget, load_agent_budget, BudgetExceededError
from .context.pack_builder import ContextPackBuilder
from .financial_search import MultiLayeredFinancialSearchEngine, FinancialEntityResolver

class StorageEngine:
    def __init__(self):
        self.store: Dict[str, Dict[str, Any]] = {}
    def get(self, note_id: str) -> Optional[Dict[str, Any]]:
        return self.store.get(note_id)
    def set(self, note_id: str, data: Dict[str, Any]) -> None:
        self.store[note_id] = data.copy()
    def delete(self, note_id: str) -> None:
        self.store.pop(note_id, None)
    def query(self, intent: str, lifecycle: List[str] = None, types: List[str] = None) -> List[Dict[str, Any]]:
        """Return notes filtered by lifecycle and type, excluding RAW notes.

        The `intent` argument is currently unused but kept for future extensibility.
        """
        results = list(self.store.values())
        results = [n for n in results if n.get('lifecycle') != Lifecycle.RAW.value]
        if lifecycle:
            results = [n for n in results if n.get('lifecycle') in lifecycle]
        if types:
            results = [n for n in results if n.get('type') in types]
        return results

class Lifecycle(str, enum.Enum):
    RAW = "RAW"
    CLASSIFIED = "CLASSIFIED"
    NORMALIZED = "NORMALIZED"
    REVIEW = "REVIEW"
    VERIFIED = "VERIFIED"
    ACTIVE = "ACTIVE"
    RECONSOLIDATING = "RECONSOLIDATING"
    SUPERSEDED = "SUPERSEDED"
    ARCHIVED = "ARCHIVED"

_ALLOWED_PROVENANCE_SOURCE_TYPES = {
    Principal.AI_AGENT: {"execution", "ai", "inference", "unknown"},
    Principal.HUMAN: {"user", "official", "execution", "experience", "inference", "import", "unknown"},
    Principal.ADMIN: {"user", "official", "execution", "experience", "ai", "inference", "import", "unknown"},
}

_PERMITTED_CREATION_LIFECYCLES = {
    Lifecycle.RAW.value,
    Lifecycle.CLASSIFIED.value,
    Lifecycle.NORMALIZED.value,
    Lifecycle.REVIEW.value,
}

class MemoryController:
    _global_review_counter = 2
    def __init__(self, storage: StorageEngine, authorizer: Authorizer = None):
        self.storage = storage
        self.authorizer = authorizer or DefaultAuthorizer()
        self.cache = Cache()
        self.supersession_enforcer = SupersessionEnforcer(self.storage)
        self._mutation_lock = threading.RLock()
        self.query_classifier = QueryClassifier()
        self.retrieval_engine = RetrievalEngine(storage, cache=self.cache)
        self.scorer = RelevanceScorer()
        self.pack_builder = ContextPackBuilder()
        self.financial_search_engine = MultiLayeredFinancialSearchEngine(self.storage)
        self._review_counter = 2
    def _check_auth(self, principal: Principal, operation: Operation) -> None:
        if not self.authorizer.is_allowed(principal, operation):
            raise PermissionError(f"{principal.value} not allowed to perform {operation.value}")
    def query(self, principal: Principal, lifecycles: Optional[List[Lifecycle]] = None, types: Optional[List[str]] = None) -> List[Dict[str, Any]]:
        """Query notes through the storage security boundary.

        RAW notes are never exposed through this API, even when explicitly requested
        in ``lifecycles``. Storage engines are responsible for enforcing that invariant.
        """
        self._check_auth(principal, Operation.READ)
        lifecycle_values = [lc.value if isinstance(lc, Lifecycle) else str(lc) for lc in lifecycles] if lifecycles else None
        return self.storage.query(intent="", lifecycle=lifecycle_values, types=types)

    def _validate_note(self, note: Dict[str, Any]) -> None:
        validation_note = {k: v for k, v in note.items() if k != "content"}
        validate_frontmatter(validation_note)
        validate_provenance(validation_note['provenance'])
        old_note = self.storage.get(note.get('id', ''))
        if old_note:
            old_lifecycle = Lifecycle(old_note.get('lifecycle'))
            new_lifecycle = Lifecycle(note.get('lifecycle'))
            if old_lifecycle != new_lifecycle:
                allowed = {
                    Lifecycle.RAW: [Lifecycle.CLASSIFIED],
                    Lifecycle.CLASSIFIED: [Lifecycle.NORMALIZED],
                    Lifecycle.NORMALIZED: [Lifecycle.REVIEW],
                    Lifecycle.REVIEW: [Lifecycle.VERIFIED],
                    Lifecycle.VERIFIED: [Lifecycle.ACTIVE],
                    Lifecycle.ACTIVE: [Lifecycle.SUPERSEDED, Lifecycle.ARCHIVED]
                }
                if new_lifecycle not in allowed.get(old_lifecycle, []):
                    raise ValueError(f"Invalid transition from {old_lifecycle} to {new_lifecycle}")

    def read(self, principal: Principal, note_id: str, include_provenance: bool = False) -> Dict[str, Any]:
        try:
            self._check_auth(principal, Operation.READ)
            check_path_traversal(note_id)
            note = self.storage.get(note_id)
            if not note:
                raise ValueError(f"Note {note_id} not found")
            if note.get('lifecycle') != Lifecycle.ACTIVE:
                raise ValueError("Only ACTIVE notes are readable via public API")
            budget = ContextBudget({})
            pd = ProgressiveDisclosure(budget)
            disclosure_level = 'metadata' if not hasattr(self, 'default_disclosure') else self.default_disclosure
            hierarchy = ['full', 'sections', 'snippet', 'metadata']
            if disclosure_level not in hierarchy:
                disclosure_level = 'metadata'
            def _disclose(level):
                if level == 'metadata':
                    return pd.metadata_only([note])
                elif level == 'snippet':
                    return pd.snippet([note])
                elif level == 'sections':
                    return pd.sections([note], "")
                elif level == 'full':
                    return pd.full_document([note])
                return pd.metadata_only([note])
            disclosed = _disclose(disclosure_level)
            usage = sum(len(json.dumps(item, default=str)) for item in disclosed)
            budget.check_budget(usage)
            while usage > budget.soft_context_budget and disclosure_level != 'metadata':
                current_index = hierarchy.index(disclosure_level)
                disclosure_level = hierarchy[current_index + 1]
                disclosed = _disclose(disclosure_level)
                usage = sum(len(json.dumps(item, default=str)) for item in disclosed)
            if usage > budget.soft_context_budget:
                from .context.compression import summarize_note
                compressed = []
                for item in disclosed:
                    if isinstance(item, dict) and 'content' in item:
                        item = item.copy()
                        item['content'] = summarize_note(item, max_chars=budget.soft_context_budget // 2)
                    compressed.append(item)
                disclosed = compressed
            for res in disclosed:
                prov = note.get('provenance', {})
                res.setdefault('provenance', {})
                res['provenance'].setdefault('source_type', prov.get('source_type'))
