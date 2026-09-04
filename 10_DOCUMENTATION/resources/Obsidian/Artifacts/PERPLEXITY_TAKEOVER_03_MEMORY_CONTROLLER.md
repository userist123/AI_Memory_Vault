---
id: "2da34064-52c2-487d-b6fa-fe172b7215c4"
type: artifact
lifecycle: ACTIVE
category: conversation-artifact
tags: [artifact, obsidian-sync, conversation-evidence]
created: 2026-08-24T21:30:00Z
updated: 2026-08-24T18:31:36.389103+00:00
provenance:
  source_type: execution
  source_ref: "PERPLEXITY_TAKEOVER_03_MEMORY_CONTROLLER.md"
confidence: high
verification: verified
relations: []
---

# Artifact: PERPLEXITY_TAKEOVER_03_MEMORY_CONTROLLER

# PERPLEXITY TAKEOVER 03 MEMORY CONTROLLER


============================================================
FILE: memory_controller/controller.py
============================================================

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
from .context.budget import ContextBudget, load_agent_budget
from .context.pack_builder import ContextPackBuilder

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
        # Exclude RAW notes from normal queries
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
    SUPERSEDED = "SUPERSEDED"
    ARCHIVED = "ARCHIVED"

class MemoryController:
    _global_review_counter = 2
    def __init__(self, storage: StorageEngine, authorizer: Authorizer = None):
        self.storage = storage
        self.authorizer = authorizer or DefaultAuthorizer()
        self.cache = Cache()
        self.supersession_enforcer = SupersessionEnforcer(self.storage)
        # Initialize pipeline components
        self.query_classifier = QueryClassifier()
        self.retrieval_engine = RetrievalEngine(storage, cache=self.cache)
        self.scorer = RelevanceScorer()
        self.pack_builder = ContextPackBuilder()
        # Counter for generating review note IDs (r2, r3, ...)
        self._review_counter = 2
    def _check_auth(self, principal: Principal, operation: Operation) -> None:
        if not self.authorizer.is_allowed(principal, operation):
            raise PermissionError(f"{principal.value} not allowed to perform {operation.value}")
    def query(self, principal: Principal, lifecycles: Optional[List[Lifecycle]] = None, types: Optional[List[str]] = None) -> List[Dict[str, Any]]:
        self._check_auth(principal, Operation.READ)
        results = list(self.storage.store.values())
        if lifecycles:
            results = [n for n in results if n.get('lifecycle') in lifecycles]
        if types:
            results = [n for n in results if n.get('type') in types]
        return results

    def _validate_note(self, note: Dict[str, Any]) -> None:
        validation_note = {k: v for k, v in note.items() if k != "content"}
        validate_frontmatter(validation_note)
        # Only validate provenance if present to allow notes without provenance in tests
        validate_provenance(validation_note['provenance'])
        # Transition validation
        old_note = self.storage.get(note.get('id', ''))
        if old_note:
            old_lifecycle = Lifecycle(old_note.get('lifecycle'))
            new_lifecycle = Lifecycle(note.get('lifecycle'))
            if old_lifecycle != new_lifecycle:
                # Basic transition rules
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
            # Retrieve note (use storage directly; cache is for search results)
            note = self.storage.get(note_id)
            if not note:
                raise ValueError(f"Note {note_id} not found")
            if note.get('lifecycle') != Lifecycle.ACTIVE:
                raise ValueError("Only ACTIVE notes are readable via public API")
            # Apply progressive disclosure based on requested level (default metadata)
            budget = ContextBudget({})
            pd = ProgressiveDisclosure(budget)
            disclosure_level = 'metadata' if not hasattr(self, 'default_disclosure') else self.default_disclosure
            # Define hierarchy of disclosure levels for degradation
            hierarchy = ['full', 'sections', 'snippet', 'metadata']
            if disclosure_level not in hierarchy:
                disclosure_level = 'metadata'
            # Helper to perform disclosure based on level
            def _disclose(level):
                if level == 'metadata':
                    return pd.metadata_only([note])
                elif level == 'snippet':
                    return pd.snippet([note])
                elif level == 'sections':
                    return pd.sections([note], "")
                elif level == 'full':
                    return pd.full_document([note])
                else:
                    return pd.metadata_only([note])
    
            disclosed = _disclose(disclosure_level)
            import json
            usage = sum(len(json.dumps(item, default=str)) for item in disclosed)
            # Enforce hard budget
            budget.check_budget(usage)
            # Soft budget graceful degradation
            while usage > budget.soft_context_budget and disclosure_level != 'metadata':
                # downgrade to next lower level
                current_index = hierarchy.index(disclosure_level)
                disclosure_level = hierarchy[current_index + 1]
                disclosed = _disclose(disclosure_level)
                usage = sum(len(json.dumps(item, default=str)) for item in disclosed)
            # If still exceeds soft after reaching metadata, apply compression on content if present
            if usage > budget.soft_context_budget:
                from .context.compression import summarize_note
                compressed = []
                for item in disclosed:
                    if isinstance(item, dict) and 'content' in item:
                        item = item.copy()
                        item['content'] = summarize_note(item, max_chars=budget.soft_context_budget // 2)
                    compressed.append(item)
                disclosed = compressed
                usage = sum(len(json.dumps(item, default=str)) for item in disclosed)
            # Ensure minimal provenance retained for each result
            for res in disclosed:
                prov = note.get('provenance', {})
                res.setdefault('provenance', {})
                res['provenance'].setdefault('source_type', prov.get('source_type'))
                res['provenance'].setdefault('source_ref', prov.get('source_ref'))
            # Build context pack
            pack = self.pack_builder.build(
                request_id="read", agent_id=principal.value, budget={}, results=disclosed,
                disclosure_level=disclosure_level, minimal_provenance=None, next_page_token=None, audit_ref=None
            )
            audit_event('read', principal, note_id, success=True)
            return pack
        except Exception as e:
            audit_event('read', principal, note_id, success=False, details={'error': str(e)})
            raise

    # Cognitive Core retrieval — extends read() to include REVIEW notes.
    # Does NOT modify the existing read() contract (P0 preserved).
    _COGNITIVE_ELIGIBLE = {Lifecycle.ACTIVE, Lifecycle.REVIEW}

    def cognitive_read(self, principal: Principal, note_id: str) -> Dict[str, Any]:
        """Read a note for cognitive operations. Returns ACTIVE and REVIEW notes.
        REVIEW notes are tagged with _cognitive_unverified=True.
        RAW and other restricted lifecycle states are excluded.
        """
        try:
            self._check_auth(principal, Operation.READ)
            check_path_traversal(note_id)
            note = self.storage.get(note_id)
            if not note:
                raise ValueError(f"Note {note_id} not found")
            lc = note.get('lifecycle')
            if lc not in {lv.value for lv in self._COGNITIVE_ELIGIBLE}:
                raise ValueError(f"Note {note_id} not eligible for cognitive retrieval (lifecycle={lc})")
            result = note.copy()
            if lc == Lifecycle.REVIEW.value:
                result['_cognitive_unverified'] = True
            pack = self.pack_builder.build(
                request_id="cognitive_read", agent_id=principal.value, budget={},
                results=[result], disclosure_level='full',
                minimal_provenance=None, next_page_token=None, audit_ref=None
            )
            audit_event('cognitive_read', principal, note_id, success=True)
            return pack
        except Exception as e:
            audit_event('cognitive_read', principal, note_id, success=False, details={'error': str(e)})
            raise

    def search(self, principal: Principal, query: str, page_size: int = 10, page_token: Optional[str] = None, lifecycles: Optional[List[Lifecycle]] = None, types: Optional[List[str]] = None) -> Dict[str, Any]:
        """Execute a full search pipeline and return a Context Pack."""
        target_id = "unknown_query"
        try:
            # Determine disclosure level early for token validation
            disclosure_level = getattr(self, 'default_disclosure', 'metadata')
            # Check query size (hard boundary)
            check_query_size(query)
            # Sanitize query
            sanitized = sanitize_query(query)
            # Compute fingerprint of current sanitized query
            query_fp = hashlib.sha256(sanitized.encode()).hexdigest()
            target_id = query_fp
            # Load budget for this agent
            budget = load_agent_budget(principal.value)
            # Classify query
            classified = self.query_classifier.classify(sanitized)
            if lifecycles is not None:
                classified['lifecycle_filters'] = [l.value if isinstance(l, Lifecycle) else l for l in lifecycles]
            if types is not None:
                classified['target_types'] = types
            # Ensure we have a max_notes limit from budget
            classified['max_notes'] = budget.max_notes
            # Handle pagination token decoding if provided
            offset = 0
            if page_token:
                payload = PaginationToken.decode(page_token)
                if payload.get('query_fp') != query_fp:
                    raise InvalidPaginationTokenError('Token query fingerprint does not match current request')
                if payload.get('agent_id') != principal.value:
                    raise InvalidPaginationTokenError('Token principal does not match current request')
                # Validate lifecycle filters binding
                token_lifecycles = payload.get('lifecycles', [])
                req_lifecycles = [l.value if isinstance(l, Lifecycle) else l for l in (lifecycles or [])]
                if token_lifecycles != req_lifecycles:
                    raise InvalidPaginationTokenError('Token lifecycle filters do not match current request')
                # Validate type filters binding
                token_types = payload.get('types', [])
                req_types = types or []
                if token_types != req_types:
                    raise InvalidPaginationTokenError('Token type filters do not match current request')
                # Validate disclosure binding
                if payload.get('disclosure') != disclosure_level:
                    raise InvalidPaginationTokenError('Token disclosure level does not match current request')
                # Validate page size binding
                if payload.get('page_size') != page_size:
                    raise InvalidPaginationTokenError('Token page size does not match current request')
                offset = payload.get('offset', 0)
            # Retrieval
            notes = self.retrieval_engine.retrieve(classified, principal, query_fp, disclosure_level, budget)
    
            # Score relevance (correct argument order)
            scored = self.scorer.score(sanitized, notes)
            score_map = {s['id']: s['score'] for s in scored}
            notes = sorted(notes, key=lambda n: score_map.get(n.get('id'), 0), reverse=True)
            # Apply progressive disclosure
            pd = ProgressiveDisclosure(budget)
            disclosure_level = getattr(self, 'default_disclosure', 'metadata')
            if disclosure_level == 'metadata':
                disclosed = pd.metadata_only(notes)
            elif disclosure_level == 'snippet':
                disclosed = pd.snippet(notes)
            elif disclosure_level == 'sections':
                disclosed = pd.sections(notes, sanitized)
            else:
                disclosed = pd.full_document(notes)
            # Pagination slicing
            total = len(disclosed)
            end = min(offset + page_size, total)
            page_results = disclosed[offset:end]
            next_token = None
            if end < total:
                payload = {
                    'offset': end,
                                    'query_fp': hashlib.sha256(sanitized.encode()).hexdigest(),
                    'agent_id': principal.value,
                    'page_size': page_size,
                    # Bind lifecycle filters (as list of values)
                    'lifecycles': [l.value if isinstance(l, Lifecycle) else l for l in (lifecycles or [])],
                    # Bind type filters
                    'types': types or [],
                    # Bind disclosure level
                    'disclosure': disclosure_level,
                    'expiration': int((datetime.now(timezone.utc) + timedelta(minutes=10)).timestamp())
                }
                secret = os.getenv('MEMORY_CONTROLLER_HMAC_SECRET')
                if not secret:
                    raise MissingHMACSecretError('HMAC secret not configured')
                token_obj = PaginationToken(payload, secret.encode())
                next_token = token_obj.encode()
            # Build context pack
            pack = self.pack_builder.build(
                request_id='search',
                agent_id=principal.value,
                budget={'soft': budget.soft_context_budget, 'hard': budget.hard_context_budget},
                results=page_results,
                disclosure_level=disclosure_level,
                minimal_provenance=None,
                next_page_token=next_token,
                audit_ref=None
    
            )
            pack['next_page_token'] = next_token
            audit_event('search', principal, target_id, success=True, details={'page_size': page_size, 'offset': offset})
            return pack
        except Exception as e:
            audit_event('search', principal, target_id, success=False, details={'error': str(e)})
            raise
    def propose(self, principal: Principal, note_data: Dict[str, Any]) -> str:
        note_id = note_data.get('id', 'unknown')
        try:
            self._check_auth(principal, Operation.PROPOSE)
            if not note_data.get('id'):
                raise ValueError('Note must include an id')
            check_path_traversal(note_id)
            # Build note using canonical defaults and overlay caller data
            now_date = datetime.now(timezone.utc).date().isoformat()
            defaults = {
                'type': 'knowledge',
                'category': 'test',  # free‑text allowed
                'tags': [],
                'created': now_date,
                'updated': now_date,
                'provenance': {
                    'source_type': 'user',
                    'source_ref': 'generated',
                },
                'confidence': 'high',
                'verification': 'unverified',
                'relations': [],
                'lifecycle': Lifecycle.RAW.value,
            }
            # Start with defaults
            note = defaults.copy()
            # Overlay all provided fields
            note.update(note_data)
            # Merge provenance specially to allow partial overrides
            prov = defaults['provenance'].copy()
            prov.update(note_data.get('provenance', {}))
            note['provenance'] = prov
            # Ensure id remains note_id
            note['id'] = note_id
            
            # Build a copy without extra fields for validation
            validation_note = {k: v for k, v in note.items() if k != "content"}
            self._validate_note(validation_note)
            # Store the full note (including possible extra fields like content)
            self.storage.set(note_id, note)
            self.cache.invalidate_by_event('memory_updated')
            audit_event('propose', principal, note_id, success=True)
            return note_id
        except Exception as e:
            audit_event('propose', principal, note_id, success=False, details={'error': str(e)})
            raise

    def review(self, principal: Principal, note_id: str, decision: str, comments: Optional[str] = None) -> None:
        try:
            self._check_auth(principal, Operation.REVIEW)
            check_path_traversal(note_id)
            note = self.storage.get(note_id)
            if not note:
                raise ValueError('Note not found')
            if note['lifecycle'] not in {Lifecycle.RAW, Lifecycle.CLASSIFIED, Lifecycle.NORMALIZED, Lifecycle.REVIEW}:
                raise ValueError('Only RAW/CLASSIFIED/NORMALIZED/REVIEW notes can be reviewed')
            if decision not in {'agree', 'approve', 'reject'}:
                # Keep original strict set but allow 'agree' for compatibility
                raise ValueError('Decision must be approve or reject')
            # Update original note lifecycle to REVIEW (if not already)
            note['lifecycle'] = Lifecycle.REVIEW
            self.storage.set(note_id, note)
            # Create a separate review record note
            review_id = f"r{MemoryController._global_review_counter}"
            MemoryController._global_review_counter += 1
            review_note = {
                'id': review_id,
                'review': {'by': principal.value, 'decision': decision, 'comments': comments}
            }
            self.storage.set(review_id, review_note)
            self.cache.invalidate_by_event('memory_updated')
            audit_event('review', principal, note_id, success=True, details={'decision': decision})
        except Exception as e:
            audit_event('review', principal, note_id, success=False, details={'decision': decision, 'error': str(e)})
            raise

    def promote(self, principal: Principal, note_id: str) -> None:
        try:
            self._check_auth(principal, Operation.PROMOTE)
            check_path_traversal(note_id)
            note = self.storage.get(note_id)
            if not note:
                raise ValueError('Note not found')
            if note['lifecycle'] != Lifecycle.REVIEW:
                raise ValueError('Only REVIEW notes can be promoted')
            note['lifecycle'] = Lifecycle.ACTIVE
            self.storage.set(note_id, note)
            self.cache.invalidate_by_event('memory_updated')
            audit_event('promote', principal, note_id, success=True)
        except Exception as e:
            audit_event('promote', principal, note_id, success=False, details={'error': str(e)})
            raise

    def update(self, principal: Principal, note_id: str, updates: Dict[str, Any]) -> None:
        try:
            self._check_auth(principal, Operation.UPDATE)
            check_path_traversal(note_id)
            note = self.storage.get(note_id)
            if not note:
                raise ValueError('Note not found')
            if note['lifecycle'] != Lifecycle.ACTIVE:
                if principal == Principal.AI_AGENT and note['lifecycle'] in {Lifecycle.RAW, Lifecycle.CLASSIFIED, Lifecycle.NORMALIZED}:
                    pass
                else:
                    raise ValueError('Updates not permitted for this lifecycle and principal')
            immutable = {'id', 'lifecycle'}
            for k in immutable:
                if k in updates and updates[k] != note.get(k):
                    raise ValueError(f'Field {k} is immutable')
            
            old_valid_until = note.get('valid_until')
            new_valid_until = updates.get('valid_until')
            has_valid_until_changed = 'valid_until' in updates and old_valid_until != new_valid_until
            
            note.update(updates)
            self._validate_note(note)
            self.storage.set(note_id, note)
            self.cache.invalidate_by_event('memory_updated')
            
            if has_valid_until_changed:
                audit_event('valid_until_update', principal, note_id, success=True, 
                            details={'old_valid_until': old_valid_until, 'new_valid_until': new_valid_until})
            else:
                audit_event('update', principal, note_id, success=True)
        except Exception as e:
            audit_event('update', principal, note_id, success=False, details={'error': str(e)})
            raise

    def archive(self, principal: Principal, note_id: str, reason: str) -> None:
        try:
            self._check_auth(principal, Operation.ARCHIVE)
            check_path_traversal(note_id)
            note = self.storage.get(note_id)
            if not note:
                raise ValueError('Note not found')
            note['lifecycle'] = Lifecycle.ARCHIVED
            note['archive_reason'] = reason
            self.storage.set(note_id, note)
            self.cache.invalidate_by_event('memory_updated')
            audit_event('archive', principal, note_id, success=True, details={'reason': reason})
        except Exception as e:
            audit_event('archive', principal, note_id, success=False, details={'reason': reason, 'error': str(e)})
            raise

    def supersede(self, principal: Principal, old_id: str, new_id: str, evidence: str = "") -> None:
        try:
            self._check_auth(principal, Operation.SUPERSEDE)
            check_path_traversal(old_id)
            check_path_traversal(new_id)
            
            # 1. Validate invariants
            self.supersession_enforcer.validate_supersession(principal, old_id, new_id)
            
            old_note = self.storage.get(old_id)
            new_note = self.storage.get(new_id)
            
            # Keep original state for atomic rollback on failure
            old_note_orig = old_note.copy()
            new_note_orig = new_note.copy()
            
            now_date = datetime.now(timezone.utc).date().isoformat()
            
            # Prepare updates for OLD note (only allowed field modifications to keep content intact)
            old_note["lifecycle"] = Lifecycle.SUPERSEDED.value
            old_note["superseded_by"] = new_id
            old_note["updated"] = now_date
            
            # Add reciprocal relation in OLD note
            if not any(r.get("target_id") == new_id and r.get("relation") == "replaced_by" for r in old_note.get("relations", [])):
                old_note.setdefault("relations", []).append({
                    "relation": "replaced_by",
                    "target": new_note.get("type", "knowledge"),
                    "target_id": new_id
                })
                
            # Prepare updates for NEW note
            new_note["supersedes"] = old_id
            new_note["updated"] = now_date
            
            # Add reciprocal relation in NEW note
            if not any(r.get("target_id") == old_id and r.get("relation") == "replaces" for r in new_note.get("relations", [])):
                new_note.setdefault("relations", []).append({
                    "relation": "replaces",
                    "target": old_note.get("type", "knowledge"),
                    "target_id": old_id
                })
                
            # Transactional atomic persistence
            try:
                self.storage.set(old_id, old_note)
                try:
                    self.storage.set(new_id, new_note)
                except Exception as e:
                    # Rollback first set operation on failure
                    self.storage.set(old_id, old_note_orig)
                    raise e
            except Exception as e:
                raise ValueError(f"Atomic supersession write failed: {str(e)}")
                
            self.cache.invalidate_by_event('memory_updated')
            
            # Audit logging: supersede operation and archive_superseded
            audit_event('supersede', principal, new_id, success=True, details={'old_id': old_id, 'evidence': evidence})
            audit_event('archive_superseded', principal, old_id, success=True, details={'new_id': new_id})
        except Exception as e:
            audit_event('supersede', principal, new_id, success=False, details={'old_id': old_id, 'evidence': evidence, 'error': str(e)})
            raise


# Export singleton
from .storage.file_engine import FileStorageEngine
_vault_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
_storage_engine = FileStorageEngine(_vault_root)

controller = MemoryController(_storage_engine)


============================================================
FILE: memory_controller/core.py
============================================================

# core.py
"""Backwards-compatibility shim.

All canonical definitions live in memory_controller.controller.
This module re-exports the symbols that downstream code imports via
`from memory_controller.core import Lifecycle`.
"""

from memory_controller.controller import Lifecycle, StorageEngine, MemoryController  # noqa: F401


============================================================
FILE: memory_controller/authorizer.py
============================================================

# authorizer.py
"""Pluggable authorizer for Memory Controller.
Defines an abstract interface and a default implementation based on the
policy matrix required by the specification.
"""

from enum import Enum
from typing import Protocol, Set

class Principal(Enum):
    HUMAN = "human"
    AI_AGENT = "ai_agent"
    ADMIN = "admin"

class Operation(Enum):
    READ = "read"
    SEARCH = "search"
    PROPOSE = "propose"
    REVIEW = "review"
    PROMOTE = "promote"
    ARCHIVE = "archive"
    UPDATE = "update"
    SUPERSEDE = "supersede"

class Authorizer(Protocol):
    """Authorizer protocol – objects must implement `is_allowed`.
    """
    def is_allowed(self, principal: Principal, operation: Operation) -> bool:
        ...

class DefaultAuthorizer:
    """Default policy implementation.

    The matrix follows the specification:
    * READ / SEARCH – Human, AI Agent
    * PROPOSE – Human, AI Agent
    * REVIEW – Human only
    * PROMOTE – Human only
    * ARCHIVE – Human (Admin may override later)
    * UPDATE – depends on lifecycle – handled in core, but permission
      is granted to Human and Admin for ACTIVE notes; AI can update
      non‑ACTIVE drafts.
    """

    _policy = {
        Operation.READ: {Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN},
        Operation.SEARCH: {Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN},
        Operation.PROPOSE: {Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN},
        Operation.REVIEW: {Principal.HUMAN, Principal.ADMIN},
        Operation.PROMOTE: {Principal.HUMAN, Principal.ADMIN},
        Operation.ARCHIVE: {Principal.HUMAN, Principal.ADMIN},
        Operation.UPDATE: {Principal.HUMAN, Principal.ADMIN, Principal.AI_AGENT},
        Operation.SUPERSEDE: {Principal.HUMAN, Principal.ADMIN, Principal.AI_AGENT},
    }

    def is_allowed(self, principal: Principal, operation: Operation) -> bool:
        allowed: Set[Principal] = self._policy.get(operation, set())
        return principal in allowed


============================================================
FILE: memory_controller/authority.py
============================================================

'''Authority scoring utilities.

Provides a deterministic mapping from provenance.source_type to a numeric authority score.
The score is derived at runtime and is not persisted in note frontmatter.
'''

# Mapping of source_type to authority score (higher = more authoritative)
_SOURCE_AUTHORITY_MAP = {
    "user": 0.5,
    "official": 0.9,
    "execution": 0.7,
    "experience": 0.6,
    "ai": 0.4,
    "inference": 0.3,
    "import": 0.8,
    "unknown": 0.2,
}

def get_authority_score(note: dict) -> float:
    """Return the authority score for a note based on its provenance.

    The function looks at ``note['provenance']['source_type']`` and returns a
    deterministic float in the range [0, 1]. If the field is missing or unknown,
    ``unknown`` mapping is used.
    """
    provenance = note.get('provenance', {})
    source_type = provenance.get('source_type', 'unknown')
    return _SOURCE_AUTHORITY_MAP.get(source_type, _SOURCE_AUTHORITY_MAP['unknown'])


============================================================
FILE: memory_controller/security.py
============================================================

import re
from typing import List

MAX_QUERY_LENGTH = 4096  # characters, configurable elsewhere if needed

def sanitize_query(query: str) -> str:
    """Basic sanitization to prevent prompt injection.
    Removes suspicious patterns like "{{" "}}" and stray markdown.
    """
    # Remove mustache-like placeholders
    sanitized = re.sub(r"\{\{.*?\}\}", "", query)
    # Remove HTML/script tags
    sanitized = re.sub(r"<script.*?>.*?</script>", "", sanitized, flags=re.DOTALL | re.IGNORECASE)
    sanitized = re.sub(r"<[^>]+>", "", sanitized)
    return sanitized.strip()

def check_query_size(query: str) -> None:
    """Raise ValueError if query exceeds soft/hard limits (hard enforced)."""
    if len(query) > MAX_QUERY_LENGTH:
        raise ValueError(f"Query length {len(query)} exceeds maximum allowed {MAX_QUERY_LENGTH}")

def check_path_traversal(path: str) -> None:
    """Prevent paths that escape the repository root.
    Simple check: no '..' segments and must be absolute within workspace.
    """
    if ".." in path.replace("\\", "/"):
        raise ValueError("Path traversal detected in path: " + path)

def detect_cache_poisoning(key: str, value) -> None:
    """Placeholder for detecting suspicious cache entries.
    For now, ensure key is a valid SHA256 hex string and value is not excessively large.
    """
    if not re.fullmatch(r"[a-f0-9]{64}", key):
        raise ValueError("Invalid cache key format")
    # Simple size guard
    if isinstance(value, (str, bytes)) and len(value) > 1_000_000:
        raise ValueError("Cache entry value exceeds size limit")


============================================================
FILE: memory_controller/git_integration.py
============================================================

import subprocess
import os
from typing import List, Tuple

class GitTransactionError(RuntimeError):
    pass

class GitIntegration:
    """Helper for staging changes, validating, and atomic commits.

    Uses git commands; assumes the repository root is the workspace root.
    Does NOT automatically push or create commits without explicit call.
    """

    def __init__(self, repo_path: str = None):
        self.repo_path = repo_path or os.getcwd()
        if not self._is_git_repo():
            raise GitTransactionError("Not a git repository at {}".format(self.repo_path))

    def _run(self, args: List[str]) -> Tuple[int, str]:
        result = subprocess.run(args, cwd=self.repo_path, capture_output=True, text=True)
        return result.returncode, result.stdout.strip() + result.stderr.strip()

    def _is_git_repo(self) -> bool:
        code, _ = self._run(["git", "rev-parse", "--is-inside-work-tree"])
        return code == 0

    def status(self) -> str:
        code, out = self._run(["git", "status", "--porcelain"])
        if code != 0:
            raise GitTransactionError("git status failed: " + out)
        return out

    def stage(self, paths: List[str]) -> None:
        # Stage files for commit
        args = ["git", "add"] + paths
        code, out = self._run(args)
        if code != 0:
            raise GitTransactionError("git add failed: " + out)

    def validate(self) -> None:
        # Simple validation: ensure no deleted files staged unintentionally
        # Here we just run git diff --cached --name-status and check for D entries
        code, out = self._run(["git", "diff", "--cached", "--name-status"])
        if code != 0:
            raise GitTransactionError("git diff failed: " + out)
        for line in out.splitlines():
            if line.startswith("D "):
                raise GitTransactionError("Attempted to delete file in transaction: " + line)

    def commit(self, message: str) -> None:
        # Perform atomic commit after staging and validation
        self.validate()
        code, out = self._run(["git", "commit", "-m", message])
        if code != 0:
            raise GitTransactionError("git commit failed: " + out)

    def revert_last(self) -> None:
        # Revert the most recent commit (does not use reset --hard)
        code, out = self._run(["git", "revert", "--no-edit", "HEAD"])
        if code != 0:
            raise GitTransactionError("git revert failed: " + out)


============================================================
FILE: memory_controller/__init__.py
============================================================

# memory_controller package
"""Top‑level package for the Memory Controller implementation.
Provides core modules, authorizer, context economy, validation, audit logging
and git integration.
"""


============================================================
FILE: memory_controller/audit/logger.py
============================================================

import json
import os
import time
from typing import Dict, Any, List

class EnumEncoder(json.JSONEncoder):
    def default(self, obj):
        if hasattr(obj, "value"):
            return obj.value
        return super().default(obj)

class AuditLogger:
    """Writes audit entries as JSON lines to a log file.

    Each entry contains:
        - actor (e.g., 'agent', 'human')
        - operation (e.g., 'READ', 'PROPOSE')
        - target_id (note id)
        - timestamp (ISO 8601)
        - outcome ('success' or 'error')
        - error_details (optional)
        - metadata (optional dict for additional info)
    """

    def __init__(self, log_path: str = None):
        if log_path is None:
            # Default to a per‑conversation log inside the artifact directory
            log_dir = os.getenv("ANTIGRAVITY_ARTIFACT_DIR", ".")
            os.makedirs(log_dir, exist_ok=True)
            log_path = os.path.join(log_dir, "audit_log.jsonl")
        self.log_path = log_path
        # Ensure file exists
        open(self.log_path, "a", encoding="utf-8").close()

    def _write_entry(self, entry: Dict[str, Any]):
        with open(self.log_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(entry, ensure_ascii=False, cls=EnumEncoder) + "\n")

    def log(self,
            actor: str,
            operation: str,
            target_id: str,
            outcome: str = "success",
            error_details: str = None,
            metadata: Dict[str, Any] = None):
        entry = {
            "actor": actor,
            "operation": operation,
            "target_id": target_id,
            "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "outcome": outcome,
        }
        if error_details:
            entry["error_details"] = error_details
        if metadata:
            entry["metadata"] = metadata
        self._write_entry(entry)

# Helper singleton for easy import
_logger_instance = None

def get_logger() -> AuditLogger:
    global _logger_instance
    if _logger_instance is None:
        _logger_instance = AuditLogger()
    return _logger_instance

def audit_event(operation: str, principal, target_id: str, success: bool = True, details: dict = None):
    """Convenient wrapper used by the controller.
    principal is a Principal enum; we store the .value as actor.
    """
    logger = get_logger()
    logger.log(
        actor=principal.value if hasattr(principal, "value") else str(principal),
        operation=operation,
        target_id=target_id,
        outcome="success" if success else "error",
        metadata=details,
    )


============================================================
FILE: memory_controller/validation/supersession.py
============================================================

# supersession.py
"""Supersession enforcer to validate and execute explicit supersession of notes.
"""
from typing import Dict, Any
from memory_controller.authorizer import Principal

class SupersessionEnforcer:
    def __init__(self, storage):
        self.storage = storage

    def validate_supersession(self, principal: Principal, old_id: str, new_id: str) -> None:
        if old_id == new_id:
            raise ValueError("Self-supersession is not allowed")
            
        old_note = self.storage.get(old_id)
        if not old_note:
            raise ValueError(f"Predecessor note {old_id} does not exist")
            
        new_note = self.storage.get(new_id)
        if not new_note:
            raise ValueError(f"Successor note {new_id} does not exist")
            
        # Do not allow superseding if already superseded
        if old_note.get("lifecycle") == "SUPERSEDED":
            raise ValueError(f"Predecessor note {old_id} is already SUPERSEDED")
            
        # Invariant: human-verified memory cannot be automatically superseded
        is_human_verified = (
            old_note.get("verification") == "verified" or 
            old_note.get("provenance", {}).get("source_type") == "user"
        )
        if is_human_verified and principal == Principal.AI_AGENT:
            raise PermissionError("Human-verified memory cannot be automatically superseded by an AI Agent")
            
        # Check for cycles
        if self._has_cycle(old_id, new_id):
            raise ValueError("Supersession would create a cycle")

    def _has_cycle(self, old_id: str, new_id: str) -> bool:
        def has_path(start: str, target: str, visited: set) -> bool:
            if start == target:
                return True
            if start in visited:
                return False
            visited.add(start)
            note = self.storage.get(start)
            if not note:
                return False
            
            # Check direct supersedes field
            pred = note.get("supersedes")
            if pred and has_path(pred, target, visited):
                return True
                
            # Check relations of type "replaces"
            for rel in note.get("relations", []):
                r_type = rel.get("relation") or rel.get("type")
                if r_type == "replaces":
                    t_id = rel.get("target_id")
                    if t_id and has_path(t_id, target, visited):
                        return True
            return False

        return has_path(old_id, new_id, set())


============================================================
FILE: memory_controller/validation/schema.py
============================================================

# schema.py
"""Canonical front‑matter validation.
Implements `validate_frontmatter` using the JSON Schema derived from
`99_SYSTEM/Canonical_Frontmatter.md`. The schema captures all required
fields, enum constraints and format checks required by the Vault.
"""

import json
from jsonschema import Draft7Validator, FormatChecker
from jsonschema.exceptions import ValidationError

# JSON Schema derived from the canonical front‑matter specification.
_CANONICAL_SCHEMA = {
    "type": "object",
    "required": ["id", "type", "lifecycle", "category", "tags", "created", "updated", "provenance", "confidence", "verification", "relations"],
    "properties": {
        "id": {"type": "string", "format": "uuid"},
        "type": {"type": "string", "enum": [
            "knowledge", "project", "procedure", "decision", "experience", "error",
            "lesson", "preference", "resource", "hypothesis", "system", "core", "index"
        ]},
        "lifecycle": {"type": "string", "enum": [
            "RAW", "CLASSIFIED", "NORMALIZED", "REVIEW", "VERIFIED", "ACTIVE", "SUPERSEDED", "ARCHIVED"
        ]},
        "category": {"type": "string"},
        "tags": {"type": "array", "items": {"type": "string"}},
        "created": {"type": "string", "format": "date"},
        "updated": {"type": "string", "format": "date"},
        "provenance": {
            "type": "object",
            "required": ["source_type", "source_ref"],
            "properties": {
                "source_type": {"type": "string", "enum": ["user", "official", "execution", "experience", "ai", "inference", "import", "unknown"]},
                "source_ref": {"type": "string"},
                "source_date": {"type": "string", "format": "date"},
                "original_path": {"type": "string"},
                "extraction_date": {"type": "string", "format": "date"},
                "redaction": {"type": "string", "enum": ["none", "applied", "not_applicable"]},
                "provenance_status": {"type": "string", "enum": ["complete", "incomplete"]}
            },
            "additionalProperties": False
        },
        "confidence": {"type": "string", "enum": ["very_high", "high", "medium", "low", "unknown"]},
        "verification": {"type": "string", "enum": ["verified", "partially_verified", "unverified", "inferred"]},
        "valid_from": {"type": "string", "format": "date"},
        "valid_until": {"type": "string", "format": "date"},
        "version_range": {"type": "string"},
        "applies_to": {"type": "string"},
        "supersedes": {"type": "string", "format": "uuid"},
        "superseded_by": {"type": "string", "format": "uuid"},
        "conflicts_with": {"type": "string", "format": "uuid"},
        "last_verified": {"type": "string", "format": "date"},
        "verification_source": {"type": "string"},
        "relations": {
            "type": "array",
            "items": {
                "type": "object",
                "required": ["relation", "target"],
                "properties": {
                    "relation": {"type": "string"},
                    "target": {"type": "string"},
                    "target_id": {"type": "string", "format": "uuid"}
                },
                "additionalProperties": False
            }
        }
    },
    "additionalProperties": False
}

def validate_frontmatter(data):
    """Validate a note's front‑matter against the canonical schema.
    Returns True if validation passes; raises jsonschema.ValidationError otherwise.
    """
    validator = Draft7Validator(_CANONICAL_SCHEMA, format_checker=FormatChecker())
    validator.validate(data)
    return True


============================================================
FILE: memory_controller/validation/provenance.py
============================================================

# provenance.py
"""Validation of provenance fields in a note.
Ensures required provenance keys exist and minimal redaction rules.
"""

def validate_provenance(prov: dict) -> None:
    required = {"source_type", "source_ref"}
    missing = required - set(prov.keys())
    if missing:
        raise ValueError(f"Provenance missing required fields: {missing}")


============================================================
FILE: memory_controller/validation/__init__.py
============================================================

ERROR: File not found: memory_controller/validation/__init__.py


============================================================
FILE: memory_controller/security/pagination_token.py
============================================================

import os
import json
import base64
import hmac
import hashlib
from datetime import datetime, timezone, timedelta

class MissingHMACSecretError(RuntimeError):
    """Raised when the required HMAC secret is not set in the environment."""
    pass

class InvalidPaginationTokenError(RuntimeError):
    """Raised when a pagination token is malformed, tampered or expired."""
    pass

class PaginationToken:
    """Opaque, tamper‑evident pagination token.

    The payload is a JSON object containing the fields required by the specification.
    The token is encoded as ``base64url(payload)`` + ``.`` + ``base64url(signature)``.
    The signature is an HMAC‑SHA256 over the payload using the secret.
    """

    def __init__(self, payload: dict, secret: bytes):
        self.payload = payload
        self.secret = secret
        self.signature = hmac.new(secret, json.dumps(payload, separators=(',', ':'), sort_keys=True).encode(), hashlib.sha256).digest()

    def encode(self) -> str:
        payload_b = base64.urlsafe_b64encode(json.dumps(self.payload, separators=(',', ':'), sort_keys=True).encode()).rstrip(b'=')
        sig_b = base64.urlsafe_b64encode(self.signature).rstrip(b'=')
        token = payload_b + b'.' + sig_b
        if len(token) > 2048:  # 2 KB limit
            raise ValueError("Pagination token exceeds maximum size of 2 KB")
        return token.decode()

    @classmethod
    def decode(cls, token: str) -> dict:
        # Helper to retrieve HMAC secret from environment without fallback
        def _get_secret() -> bytes:
            secret = os.getenv('MEMORY_CONTROLLER_HMAC_SECRET')
            if not secret:
                raise MissingHMACSecretError('HMAC secret not configured in MEMORY_CONTROLLER_HMAC_SECRET')
            return secret.encode()

        secret_b = _get_secret()
        try:
            payload_b, sig_b = token.encode().split(b'.')
            # Ensure proper base64 padding before decoding
            payload_json = base64.urlsafe_b64decode(payload_b + b'=' * (-len(payload_b) % 4))
            payload = json.loads(payload_json)
            expected_sig = hmac.new(secret_b, json.dumps(payload, separators=(',', ':'), sort_keys=True).encode(), hashlib.sha256).digest()
            actual_sig = base64.urlsafe_b64decode(sig_b + b'=' * (-len(sig_b) % 4))
        except Exception as e:
            raise InvalidPaginationTokenError(f"Failed to parse token: {e}")
        # Verify HMAC signature
        if not hmac.compare_digest(expected_sig, actual_sig):
            raise InvalidPaginationTokenError('Token signature mismatch')
        # Expiration handling (optional)
        exp_ts = payload.get('expiration')
        if exp_ts is not None:
            exp = datetime.fromtimestamp(exp_ts, tz=timezone.utc)
            if datetime.now(tz=timezone.utc) > exp:
                raise InvalidPaginationTokenError('Token has expired')
        return payload


============================================================
FILE: memory_controller/security/utils.py
============================================================

import re

MAX_QUERY_LENGTH = 4096  # characters, configurable elsewhere if needed

def sanitize_query(query: str) -> str:
    """Basic sanitization to prevent prompt injection.
    Removes suspicious patterns like "{{" "}}" and stray markdown.
    """
    # Remove mustache-like placeholders
    sanitized = re.sub(r"\{\{.*?\}\}", "", query)
    # Remove HTML/script tags
    sanitized = re.sub(r"<script.*?>.*?</script>", "", sanitized, flags=re.DOTALL | re.IGNORECASE)
    sanitized = re.sub(r"<[^>]+>", "", sanitized)
    return sanitized.strip()

def check_query_size(query: str) -> None:
    """Raise ValueError if query exceeds soft/hard limits (hard enforced)."""
    if len(query) > MAX_QUERY_LENGTH:
        raise ValueError(f"Query length {len(query)} exceeds maximum allowed {MAX_QUERY_LENGTH}")

def check_path_traversal(path: str) -> None:
    """Prevent paths that escape the repository root or use absolute paths."""
    normalized = path.replace("\\", "/")
    if ".." in normalized:
        raise ValueError("Path traversal detected in path: " + path)
    if normalized.startswith("/") or re.match(r"^[a-zA-Z]:/", normalized) or re.match(r"^[a-zA-Z]:\\", path):
        raise ValueError("Absolute paths not allowed in note_id: " + path)

def detect_cache_poisoning(key: str, value) -> None:
    """Detect anomalous cache entries or poisoned keys."""
    if not re.fullmatch(r"[a-f0-9]{64}", key):
        raise ValueError("Invalid cache key format")
    
    # Calculate approximate size for lists and dicts
    size = 0
    if isinstance(value, (str, bytes)):
        size = len(value)
    elif isinstance(value, (list, dict)):
        import json
        try:
            size = len(json.dumps(value))
        except Exception:
            pass
            
    if size > 1_000_000:
        raise ValueError("Cache entry value exceeds size limit")


============================================================
FILE: memory_controller/security/__init__.py
============================================================

# Initialize security package
from .utils import sanitize_query, check_path_traversal, detect_cache_poisoning, check_query_size


============================================================
FILE: memory_controller/storage/file_engine.py
============================================================

import os
import glob
import tempfile
import sys
from typing import Dict, Any, List, Optional
# Lazy import of Lifecycle moved inside query method to avoid circular import
from memory_controller.audit.logger import audit_event
from .serializer import serialize, deserialize
from .path_resolver import resolve_path

class FileStorageEngine:
    def __init__(self, vault_root: str):
        self.vault_root = vault_root
        self.id_to_path: Dict[str, str] = {}
        self._initialize_index()

    def _initialize_index(self):
        # Scan canonical folders to build the UUID -> Path index
        # EXPLICIT EXCLUSIONS: "06_INBOX" and "90_TEMPLATES" are NOT included
        canonical_folders = [
            "00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES",
            "04_MEMORY", "05_RESOURCES", "99_SYSTEM"
        ]
        for folder in canonical_folders:
            folder_path = os.path.join(self.vault_root, folder)
            if not os.path.exists(folder_path):
                continue
            
            for filepath in glob.glob(os.path.join(folder_path, "**", "*.md"), recursive=True):
                # Double check to prevent RAW_IMPORTS leakage
                if "RAW_IMPORTS" in filepath:
                    continue
                try:
                    with open(filepath, 'r', encoding='utf-8') as f:
                        content = f.read()
                    data = deserialize(content)
                    note_id = data.get("id")
                    if note_id:
                        if note_id in self.id_to_path:
                            # DUPLICATE UUID => FATAL INTEGRITY ERROR
                            raise ValueError(f"Duplicate UUID found: {note_id} in {filepath} and {self.id_to_path[note_id]}")
                        self.id_to_path[note_id] = filepath
                except Exception as e:
                    if "Duplicate UUID" in str(e):
                        raise e
                    if "Malformed YAML" in str(e):
                        # SKIP + AUDIT
                        audit_event("storage_error", "system", "unknown", success=False, 
                                    details={"error": "Malformed YAML", "path": filepath, "message": str(e)})
                        continue
                    # Ignored
                    continue

    def get(self, note_id: str) -> Optional[Dict[str, Any]]:
        filepath = self.id_to_path.get(note_id)
        if not filepath or not os.path.exists(filepath):
            return None
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
        return deserialize(content)

    def set(self, note_id: str, data: Dict[str, Any]) -> None:
        # INVARIANT: storage key == data["id"]
        yaml_id = data.get("id")
        if str(note_id) != str(yaml_id):
            raise ValueError(f"ID mismatch: storage key '{note_id}' must equal YAML id '{yaml_id}'")
            
        target_path = resolve_path(self.vault_root, data)
        serialized_content = serialize(data)
        
        # Ensure directory exists
        os.makedirs(os.path.dirname(target_path), exist_ok=True)
        
        # ATOMIC WRITE: Write to a temporary file in the same directory, then replace
        dir_name = os.path.dirname(target_path)
        fd, temp_path = tempfile.mkstemp(dir=dir_name, prefix=".tmp_")
        try:
            with os.fdopen(fd, 'w', encoding='utf-8') as f:
                f.write(serialized_content)
                f.flush()
                os.fsync(f.fileno())
            # Replace target atomically
            os.replace(temp_path, target_path)
        except Exception as e:
            if os.path.exists(temp_path):
                os.remove(temp_path)
            raise e
            
        # Update/Rename semantic: if old path is different, delete old file
        existing_path = self.id_to_path.get(note_id)
        if existing_path and existing_path != target_path:
            if os.path.exists(existing_path):
                os.remove(existing_path)
                
        self.id_to_path[note_id] = target_path

    def delete(self, note_id: str) -> None:
        filepath = self.id_to_path.get(note_id)
        if filepath:
            # Re-verify we don't accidentally delete outside
            if "06_INBOX" in filepath:
                raise ValueError("Cannot delete from RAW_IMPORTS")
            if os.path.exists(filepath):
                os.remove(filepath)
            del self.id_to_path[note_id]

    def query(self, intent: str, lifecycle: List[str] = None, types: List[str] = None) -> List[Dict[str, Any]]:
        """Query notes, excluding RAW notes."""
        # Lazy import to avoid circular dependency
        from memory_controller.controller import Lifecycle

        results = []
        for note_id, filepath in self.id_to_path.items():
            try:
                note = self.get(note_id)
                if not note:
                    continue
                # Exclude RAW notes from normal queries
                if note.get('lifecycle') == Lifecycle.RAW.value:
                    continue
                if lifecycle and note.get('lifecycle') not in lifecycle:
                    continue
                if types and note.get('type') not in types:
                    continue
                results.append(note)
            except Exception:
                continue
        return results



============================================================
FILE: memory_controller/storage/path_resolver.py
============================================================

import os
import re

def sanitize_filename(category: str) -> str:
    """Sanitizes the category to ensure safe filenames."""
    if not category or not isinstance(category, str):
        category = "untitled"
        
    # Remove characters that are unsafe on Windows/Unix
    # : * ? " < > | \ /
    safe = re.sub(r'[:*?"<>|\\/]', '_', category)
    
    # Remove non-printable characters
    safe = re.sub(r'[\x00-\x1f\x7f-\x9f]', '', safe)
    
    # Trim trailing dots and spaces (Windows issue)
    safe = safe.strip('. ')
    
    # Windows reserved names
    reserved = {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    }
    
    if safe.upper() in reserved:
        safe = f"{safe}_"
        
    # Enforce maximum length to prevent PathTooLong errors
    # NTFS max filename is 255 chars, we limit category to 100 to leave room for UUID
    if len(safe) > 100:
        safe = safe[:100].strip('. ')
        
    if not safe:
        safe = "untitled"
        
    return safe

def resolve_path(vault_root: str, note: dict) -> str:
    """Resolves the physical directory for a note based on its type and guarantees containment."""
    note_type = str(note.get("type", "knowledge")).lower()
    
    mapping = {
        "knowledge": "01_KNOWLEDGE",
        "project": "02_PROJECTS",
        "procedure": "03_PROCEDURES",
        "decision": "04_MEMORY",
        "experience": "04_MEMORY",
        "error": "04_MEMORY",
        "lesson": "04_MEMORY",
        "preference": "04_MEMORY",
        "hypothesis": "04_MEMORY",
        "resource": "05_RESOURCES",
        "system": "99_SYSTEM",
        "index": "99_SYSTEM",
        "core": "00_CORE"
    }
    
    folder = mapping.get(note_type, "04_MEMORY") # default to memory if unknown
    
    # Strict exclusion for RAW_IMPORTS mutation
    if "06_INBOX" in folder:
        raise ValueError("FileStorageEngine cannot mutate RAW_IMPORTS")
    
    note_id = str(note.get("id", ""))
    if not note_id:
        raise ValueError("Cannot resolve path for note without id")
        
    # Sanitize category for the filename
    category = sanitize_filename(str(note.get("category", "unknown")))
    
    # Prevent traversal payload in ID
    if ".." in note_id or "/" in note_id or "\\" in note_id:
        raise ValueError("Path traversal attempt in note id")
        
    filename = f"{category}_{note_id[:8]}.md"
    
    # Compute paths
    target_path = os.path.join(vault_root, folder, filename)
    resolved_target = os.path.realpath(target_path)
    resolved_root = os.path.realpath(vault_root)
    
    # Path Containment check
    try:
        common = os.path.commonpath([resolved_target, resolved_root])
        if common != resolved_root:
            raise ValueError("Path traversal attempt detected: Resolved path outside Vault root")
    except ValueError as e:
        # commonpath raises ValueError if paths are on different drives on Windows
        raise ValueError(f"Path traversal attempt detected: {e}")
        
    return target_path


============================================================
FILE: memory_controller/storage/serializer.py
============================================================

import yaml
import re
from enum import Enum

# Configure PyYAML to serialize Enums using their .value
def enum_representer(dumper, data):
    return dumper.represent_scalar('tag:yaml.org,2002:str', str(data.value))

yaml.add_multi_representer(Enum, enum_representer, Dumper=yaml.SafeDumper)

def serialize(note: dict) -> str:
    """Serializes a dictionary into YAML Frontmatter + Markdown Body."""
    note_copy = note.copy()
    content = note_copy.pop("content", "")
    
    # We must ensure we use safe dumping
    frontmatter = yaml.safe_dump(note_copy, default_flow_style=False, sort_keys=False, allow_unicode=True)
    
    # Construct the final Markdown file string
    result = f"---\n{frontmatter}---\n{content}"
    return result

def deserialize(file_content: str) -> dict:
    """Deserializes YAML Frontmatter + Markdown Body into a dictionary."""
    # Robust regex to extract YAML frontmatter exactly between the first two ---
    # Handles LF, CRLF, and empty body
    match = re.match(r'^---\r?\n(.*?)\r?\n---\r?\n?(.*)', file_content, re.DOTALL)
    
    if not match:
        raise ValueError("Malformed YAML: Missing opening or closing --- delimiters at the start of the file")
        
    yaml_text = match.group(1)
    body = match.group(2)
    
    try:
        data = yaml.safe_load(yaml_text)
    except yaml.YAMLError as e:
        raise ValueError(f"Malformed YAML: {e}")
        
    if not isinstance(data, dict):
        raise ValueError("Malformed YAML: Frontmatter must be a dictionary")
        
    data["content"] = body
    return data


============================================================
FILE: memory_controller/storage/__init__.py
============================================================

ERROR: File not found: memory_controller/storage/__init__.py


============================================================
FILE: memory_controller/context/progressive_disclosure.py
============================================================

from typing import List, Dict, Any

class ProgressiveDisclosure:
    """Utility to progressively disclose memory content based on budget.

    The workflow:
        1. metadata_only – returns identifiers and minimal metadata.
        2. snippet – returns a short excerpt (e.g., first 200 chars).
        3. sections – returns relevant sections based on query highlights.
        4. full_document – returns the full note content.
        5. provenance_on_demand – fetches raw provenance when requested.
    """

    def __init__(self, budget):
        self.budget = budget  # Instance of ContextBudget or similar

    def _within_budget(self, usage: int) -> bool:
        try:
            self.budget.check_budget(usage)
            return True
        except Exception:
            return False

    def metadata_only(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        # Return only id, type, lifecycle, and confidence
        result = []
        usage = 0
        for note in notes:
            entry = {
                "id": note.get("id"),
                "type": note.get("type"),
                "lifecycle": note.get("lifecycle"),
                "confidence": note.get("confidence"),
                "relations": note.get("relations", [])
            }
            result.append(entry)
            usage += 1  # Count each metadata as 1 unit
            if not self._within_budget(usage):
                break
        return result

    def snippet(self, notes: List[Dict[str, Any]], chars: int = 200) -> List[Dict[str, Any]]:
        result = []
        usage = 0
        for note in notes:
            content = note.get("content", "")
            snippet = content[:chars]
            entry = {"id": note.get("id"), "snippet": snippet}
            result.append(entry)
            usage += chars
            if not self._within_budget(usage):
                break
        return result

    def sections(self, notes: List[Dict[str, Any]], query: str) -> List[Dict[str, Any]]:
        # Very naive: return lines containing any query token
        tokens = set(query.lower().split())
        result = []
        usage = 0
        for note in notes:
            content = note.get("content", "")
            lines = content.split("\n")
            matched = [ln for ln in lines if any(tok in ln.lower() for tok in tokens)]
            entry = {"id": note.get("id"), "sections": matched[:5]}
            result.append(entry)
            usage += len(matched)
            if not self._within_budget(usage):
                break
        return result

    def full_document(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        # Return full content respecting hard budget (bytes)
        result = []
        usage = 0
        for note in notes:
            content = note.get("content", "")
            size = len(content.encode("utf-8"))
            if not self._within_budget(usage + size):
                break
            result.append({"id": note.get("id"), "content": content})
            usage += size
        return result

    def provenance_on_demand(self, note_ids: List[str], storage_engine) -> List[Dict[str, Any]]:
        # Retrieve raw provenance records for given ids via storage engine
        prov = []
        for nid in note_ids:
            prov.append(storage_engine.get_provenance(nid))
        return prov


============================================================
FILE: memory_controller/context/query_classifier.py
============================================================

from enum import Enum
from typing import List, Dict, Any

class Intent(Enum):
    READ = "read"
    SEARCH = "search"
    PROPOSE = "propose"
    UPDATE = "update"
    REVIEW = "review"
    PROMOTE = "promote"
    ARCHIVE = "archive"

class QueryClassifier:
    """Classify a raw query string into intent and target memory types.

    This is a rule‑based lightweight classifier; can be extended with
    LLM‑based intent detection later.
    """

    def __init__(self, intent_map: Dict[str, Intent] = None):
        # Simple keyword mapping; defaults cover main operations.
        self.intent_map = intent_map or {
            "read": Intent.READ,
            "search": Intent.SEARCH,
            "propose": Intent.PROPOSE,
            "update": Intent.UPDATE,
            "review": Intent.REVIEW,
            "promote": Intent.PROMOTE,
            "archive": Intent.ARCHIVE,
        }

    def classify(self, query: str) -> Dict[str, Any]:
        """Return a dict with intent, target_types, lifecycle_filters, confidence.

        - intent: inferred Intent enum (default READ)
        - target_types: list of memory types (knowledge, project, …)
        - lifecycle_filters: optional list of lifecycle stages to limit
        - confidence: soft estimate (0‑1) based on keyword match count
        """
        lowered = query.lower()
        # Determine intent by first matching keyword.
        intent = Intent.READ
        for kw, val in self.intent_map.items():
            if kw in lowered:
                intent = val
                break
        # Very naive extraction of target types – look for known nouns.
        target_types = []
        for t in ["knowledge", "project", "procedure", "decision", "error", "lesson", "experience", "resource", "hypothesis"]:
            if t in lowered:
                target_types.append(t)
        # Lifecycle filters – e.g., "active", "verified".
        lifecycle_filters = []
        for stage in ["raw", "classified", "normalized", "review", "verified", "active", "superseded", "archived"]:
            if stage in lowered:
                lifecycle_filters.append(stage.upper())
        confidence = 0.9 if intent != Intent.READ else 0.5
        return {
            "intent": intent,
            "target_types": target_types,
            "lifecycle_filters": lifecycle_filters,
            "confidence": confidence,
        }


============================================================
FILE: memory_controller/context/relevance_scoring.py
============================================================

from typing import List, Dict
import math

class RelevanceScorer:
    """Simple relevance scoring based on token overlap and confidence.

    The score is a float between 0 and 1.
    """

    def __init__(self):
        pass

    def score(self, query: str, notes: List[Dict[str, any]]) -> List[Dict[str, any]]:
        query_tokens = set(query.lower().split())
        scored = []
        for note in notes:
            content = note.get("content", "")
            note_tokens = set(content.lower().split())
            overlap = query_tokens.intersection(note_tokens)
            overlap_ratio = len(overlap) / max(len(query_tokens), 1)
            confidence = note.get("confidence", 0.5)
            # Convert confidence string to numeric if needed
            if isinstance(confidence, str):
                confidence_map = {
                    'very_high': 1.0,
                    'high': 0.9,
                    'medium': 0.5,
                    'low': 0.2,
                    'unknown': 0.0
                }
                confidence = confidence_map.get(confidence.lower(), 0.5)
            final = (overlap_ratio + confidence) / 2
            scored.append({"id": note.get("id"), "score": final})
        scored.sort(key=lambda x: x["score"], reverse=True)
        return scored


============================================================
FILE: memory_controller/context/retrieval.py
============================================================

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


============================================================
FILE: memory_controller/context/budget.py
============================================================

import json
import zlib
from typing import Dict, Any, List


class BudgetExceededError(RuntimeError):
    """Raised when the context cannot be fitted within the hard byte limit."""

    pass

class ContextBudgetError(RuntimeError):
    """Raised when the context exceeds the hard limit (alias for BudgetExceededError)."""
    pass


class ContextBudget:
    """Manage context budgets per request using UTF-8 byte measurements.

    Parameters
    ----------
    config: Dict[str, Any]
        Expected keys:
        - soft_limit_bytes (int): advisory soft limit.
        - hard_limit_bytes (int): hard limit that must never be exceeded.
        - max_full_documents (int): maximum number of notes allowed full disclosure.
    """

    def __init__(self, config: Dict[str, Any]):
        # Default values align with previous character‑based defaults but expressed in bytes.
        # Initialize budget limits and note limits
        self.max_notes = config.get("max_notes", 50)  # default max notes for a query
        # Alias for backward compatibility
        self.max_full_documents = config.get("max_full_documents", 3)
        self.soft_limit_bytes = config.get("soft_limit_bytes", config.get("soft_context_budget", 16 * 1024))
        self.hard_limit_bytes = config.get("hard_limit_bytes", config.get("hard_context_budget", 32 * 1024))

    # ---------------------------------------------------------------------
    # Compatibility properties (used by existing controller code)
    # ---------------------------------------------------------------------
    @property
    def soft_context_budget(self) -> int:
        """Alias for backward compatibility with existing controller expectations."""
        return self.soft_limit_bytes

    @property
    def hard_context_budget(self) -> int:
        """Alias for backward compatibility with existing controller expectations."""
        return self.hard_limit_bytes

    # ---------------------------------------------------------------------
    # Budget enforcement helpers
    # ---------------------------------------------------------------------
    def _size_of(self, note: Dict[str, Any]) -> int:
        """Return the UTF‑8 byte size of a note's content.

        The note is expected to contain a ``content`` field (string). If the field is missing,
        size is considered 0. Provenance fields are *not* counted toward the budget because they
        are stored separately in the final pack.
        """
        content = note.get("content", "")
        if isinstance(content, bytes):
            # Already compressed – use its length directly.
            return len(content)
        return len(str(content).encode("utf-8"))

    def check_hard_limit(self, usage: int) -> None:
        """Raise :class:`BudgetExceededError` if ``usage`` exceeds the hard byte limit.
        """
        if usage > self.hard_limit_bytes:
            raise BudgetExceededError(
                f"Context usage {usage} exceeds hard limit {self.hard_limit_bytes} bytes"
            )

    def check_budget(self, usage: int) -> None:
        """Alias for backward compatibility: raise :class:`ContextBudgetError` if usage exceeds hard limit.
        """
        if usage > self.hard_limit_bytes:
            raise ContextBudgetError(
                f"Context usage {usage} exceeds hard limit {self.hard_limit_bytes} bytes"
            )

    # ---------------------------------------------------------------------
    # Degradation algorithm
    def apply_degradation(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """Apply deterministic degradation to fit within soft and hard limits.

        Steps:
        1. Sort notes by relevance descending.
        2. Enforce max_full_documents: allow at most N notes to stay FULL.
        3. If soft limit exceeded, downgrade lower‑relevance notes first.
        4. Degrade FULL → PARTIAL (truncated with marker) → METADATA_ONLY as needed.
        5. Compress large contents (>1 KiB) internally with zlib.
        6. Enforce hard limit (raise BudgetExceededError).
        """
        # Step 1: sort notes by relevance descending
        ordered = sorted(notes, key=lambda n: n.get("relevance", 0), reverse=True)

        # Step 2: enforce max_full_documents (initially keep full for top N)
        for i, note in enumerate(ordered):
            if i >= self.max_full_documents:
                note["content"] = ""

        def total_usage(ns: List[Dict[str, Any]]) -> int:
            return sum(self._size_of(n) for n in ns)

        # Step 3: drop notes entirely if still over soft and we have more than max_full_documents notes
        while total_usage(ordered) > self.soft_limit_bytes and len(ordered) > self.max_full_documents:
            ordered.pop()  # remove lowest‑relevance note

        # Step 4: degrade remaining top notes if still over soft limit
        for note in ordered[:self.max_full_documents]:
            if total_usage(ordered) <= self.soft_limit_bytes:
                break
            content = note.get("content", "")
            if isinstance(content, str) and len(content) > 0:
                # Create a PARTIAL version: truncate to 50 chars and add marker
                truncated = content[:50] + "...[PARTIAL]"
                note["content"] = truncated
                # If still exceeds soft after truncation, fall back to METADATA_ONLY
                if total_usage(ordered) > self.soft_limit_bytes:
                    note["content"] = ""
            else:
                # Already empty, nothing to do
                continue

        # Step 5: compress large contents internally (after possible truncation)
        for note in ordered:
            content = note.get("content", "")
            if isinstance(content, str) and len(content.encode("utf-8")) > 1024:
                note["content"] = zlib.compress(content.encode("utf-8"))

        # Step 6: enforce hard limit
        self.check_hard_limit(total_usage(ordered))
        return ordered
        """Apply deterministic degradation to fit within soft and hard limits.

        Steps:
        1. Sort notes by relevance descending.
        2. Enforce max_full_documents: keep full content for top N, clear others.
        3. If still over soft limit, drop lowest‑relevant notes until within soft or only max_full_documents remain.
        4. If still over soft, clear content of remaining notes (starting from lowest relevance) until within soft.
        5. Compress large contents (>1 KiB) internally with zlib.
        6. Enforce hard limit; raise BudgetExceededError if exceeded.
        """
        # Step 1: sort notes by relevance descending
        ordered = sorted(notes, key=lambda n: n.get("relevance", 0), reverse=True)

        # Step 2: enforce max_full_documents
        for i, note in enumerate(ordered):
            if i >= self.max_full_documents:
                note["content"] = ""

        def total_usage(ns: List[Dict[str, Any]]) -> int:
            return sum(self._size_of(n) for n in ns)

        # Step 3: drop notes if over soft limit
        while total_usage(ordered) > self.soft_limit_bytes and len(ordered) > self.max_full_documents:
            ordered.pop()  # remove least relevant note

        # Step 4: clear content of remaining notes if still over soft limit
        for note in ordered[:self.max_full_documents]:
            if total_usage(ordered) <= self.soft_limit_bytes:
                break
            note["content"] = ""

        # Step 5: compress large contents internally
        for note in ordered:
            content = note.get("content", "")
            if isinstance(content, str) and len(content.encode("utf-8")) > 1024:
                note["content"] = zlib.compress(content.encode("utf-8"))

        # Step 6: enforce hard limit
        self.check_hard_limit(total_usage(ordered))
        return ordered
    

    # ---------------------------------------------------------------------
    # Utility for max_full_documents enforcement (called by callers as needed).
    # ---------------------------------------------------------------------
    def enforce_max_full(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """Ensure no more than ``max_full_documents`` notes retain full content.

        Higher‑relevance notes keep full content; lower‑relevance notes have their ``content``
        replaced with an empty string (metadata only). The function returns the mutated list.
        """
        ordered = sorted(notes, key=lambda n: n.get("relevance", 0), reverse=True)
        for i, note in enumerate(ordered):
            if i >= self.max_full_documents:
                note["content"] = ""
        return ordered


def load_agent_budget(agent_id: str, config_path: str = "config/agent_budgets.json") -> ContextBudget:
    """Load a JSON config for the given agent and return a :class:`ContextBudget`.

    Missing files or entries fall back to defaults defined in :class:`ContextBudget`.
    """
    try:
        with open(config_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        agent_cfg = data.get(agent_id, {})
    except FileNotFoundError:
        agent_cfg = {}
    return ContextBudget(agent_cfg)

    # Removed duplicated legacy definitions that conflicted with the primary ContextBudget implementation.


============================================================
FILE: memory_controller/context/compression.py
============================================================

# -*- coding: utf-8 -*-
"""Context compression utilities.

Provides simple summarization and claim extraction for notes.
"""

from typing import List, Dict

def summarize_note(note: Dict[str, any], max_chars: int = 200) -> str:
    """Return a truncated summary of the note content.
    If the content is shorter than max_chars, return it unchanged.
    """
    content = note.get("content", "")
    return content[:max_chars].rstrip()

def extract_claims(note: Dict[str, any]) -> List[str]:
    """Very naive claim extraction: split sentences and return those ending with a period.
    In real system this would use NLP; here we keep it simple.
    """
    content = note.get("content", "")
    sentences = [s.strip() for s in content.split('.') if s.strip()]
    # Return first few sentences as claims
    return sentences[:3]


============================================================
FILE: memory_controller/context/metrics.py
============================================================

# -*- coding: utf-8 -*-
"""Metrics utilities for the Context Economy layer.

Provides simple counters for retrieval operations, cache hits/misses, and
budget usage. In a full implementation these would be exported to a
monitoring system; here we keep an in‑memory dict.
"""

from collections import defaultdict
from typing import Dict

class Metrics:
    def __init__(self):
        self.counters: Dict[str, int] = defaultdict(int)

    def inc(self, metric_name: str, amount: int = 1) -> None:
        self.counters[metric_name] += amount

    def get(self, metric_name: str) -> int:
        return self.counters.get(metric_name, 0)

    def snapshot(self) -> Dict[str, int]:
        """Return a shallow copy of all counters."""
        return dict(self.counters)


============================================================
FILE: memory_controller/context/pack_builder.py
============================================================

from typing import List, Dict, Any, Optional

class ContextPackBuilder:
    """Assemble the final context payload sent back to the requester.

    The contract fields:
        - requestId: identifier of the request (string).
        - agentId: identifier of the calling agent.
        - budget: dict with 'soft' and 'hard' limits used for this request.
        - results: list of disclosed note objects (already processed).
        - disclosureLevel: one of ['metadata', 'snippet', 'sections', 'full']
        - provenance: minimal provenance (source_type, source_ref) included in each result.
        - nextPageToken: optional string if pagination is needed.
        - auditRef: optional reference to an audit log entry (only if requested).
    """

    def __init__(self):
        pass

    def build(
        self,
        request_id: str,
        agent_id: str,
        budget: Dict[str, Any],
        results: List[Dict[str, Any]],
        disclosure_level: str,
        minimal_provenance: List[Dict[str, Any]] = None,
        next_page_token: Optional[str] = None,
        audit_ref: Optional[str] = None,
    ) -> Dict[str, Any]:
        pack: Dict[str, Any] = {
            "requestId": request_id,
            "agentId": agent_id,
            "budget": budget,
            "disclosureLevel": disclosure_level,
            "results": results,
        }
        if minimal_provenance:
            # Attach provenance directly to each result (already expected to have it)
            for res, prov in zip(pack["results"], minimal_provenance):
                res.setdefault("provenance", {})
                res["provenance"].setdefault("source_type", prov.get("source_type"))
                res["provenance"].setdefault("source_ref", prov.get("source_ref"))
        if next_page_token:
            pack["nextPageToken"] = next_page_token
        if audit_ref:
            pack["auditRef"] = audit_ref
        return pack


============================================================
FILE: memory_controller/context/__init__.py
============================================================

# -*- coding: utf-8 -*-
"""Context package init – expose public classes for easy import."""

from .budget import ContextBudget
from .query_classifier import QueryClassifier, Intent
from .retrieval import RetrievalEngine
from .progressive_disclosure import ProgressiveDisclosure
from .relevance_scoring import RelevanceScorer

__all__ = [
    "ContextBudget",
    "QueryClassifier",
    "Intent",
    "RetrievalEngine",
    "ProgressiveDisclosure",
    "RelevanceScorer",
]


============================================================
FILE: memory_controller/cache/lru_cache.py
============================================================

import hashlib
import time
from typing import Any, Dict, Tuple, List
from memory_controller.security.utils import detect_cache_poisoning

class LRUCacheEntry:
    def __init__(self, value: Any, ttl_seconds: int):
        self.value = value
        self.expiry = time.time() + ttl_seconds
        self.last_used = time.time()

    def is_expired(self) -> bool:
        return time.time() > self.expiry

class LRUCache:
    """Deterministic LRU cache with TTL fallback.

    - max_items: hard limit for number of cache entries.
    - default_ttl: seconds after which an entry is considered stale.
    - event_map: maps event names to sets of cache keys for invalidation.
    """

    def __init__(self, max_items: int = 256, default_ttl: int = 300):
        self.max_items = max_items
        self.default_ttl = default_ttl
        self.store: Dict[str, LRUCacheEntry] = {}
        self.event_map: Dict[str, set] = {}
        self.hit_count: int = 0
        self.miss_count: int = 0

    def _make_key(self, *parts: Any) -> str:
        """Create a deterministic hash key from arbitrary serializable parts."""
        raw = "|".join(str(p) for p in parts)
        return hashlib.sha256(raw.encode()).hexdigest()

    def get(self, *key_parts: Any) -> Any:
        key = self._make_key(*key_parts)
        entry = self.store.get(key)
        if entry is None:
            self.miss_count += 1
            return None
        if entry.is_expired():
            # Remove stale entry
            del self.store[key]
            self.miss_count += 1
            return None
            
        try:
            detect_cache_poisoning(key, entry.value)
        except ValueError:
            # Poisoned cache entry, invalidate and treat as miss
            self.invalidate(*key_parts) # using invalidate handles event_map cleanup
            self.miss_count += 1
            return None
        # Update LRU order
        entry.last_used = time.time()
        self.hit_count += 1
        return entry.value

    def set(self, value: Any, *key_parts: Any, ttl: int = None, events: list = None) -> None:
        key = self._make_key(*key_parts)
        ttl_seconds = ttl if ttl is not None else self.default_ttl
        entry = LRUCacheEntry(value, ttl_seconds)
        self.store[key] = entry
        self._enforce_limits()
        if events:
            for ev in events:
                self.event_map.setdefault(ev, set()).add(key)

    def invalidate_by_event(self, event_name: str) -> None:
        keys = self.event_map.pop(event_name, set())
        for k in keys:
            self.store.pop(k, None)
    def invalidate(self, *key_parts: Any) -> None:
        """Invalidate cache entry for the given key parts.

        Removes the entry from the store and cleans up any event mappings.
        """
        key = self._make_key(*key_parts)
        self.store.pop(key, None)
        # Remove key from all event sets to avoid stale references
        for ev_keys in self.event_map.values():
            ev_keys.discard(key)

    def _enforce_limits(self) -> None:
        # Enforce max_items using LRU eviction
        if len(self.store) <= self.max_items:
            return
        # Sort by last_used ascending (oldest first)
        sorted_items: List[Tuple[str, LRUCacheEntry]] = sorted(
            self.store.items(), key=lambda item: item[1].last_used
        )
        for key, _ in sorted_items[: len(self.store) - self.max_items]:
            del self.store[key]


============================================================
FILE: memory_controller/cache/__init__.py
============================================================

# -*- coding: utf-8 -*-
"""Cache package init – expose LRUCache class with deterministic composite keys.

The Cache class extends LRUCache to provide isolation across principal,
query fingerprint, lifecycle filters, target‑type filters and disclosure level.
It also proxies the hit and miss counters.
"""

import hashlib
from .lru_cache import LRUCache

class Cache(LRUCache):
    """Public Cache interface used by MemoryController.

    The composite key is built from:
        principal.value, query_fp, lifecycle tuple, target‑type tuple, disclosure_level.
    This guarantees isolation and prevents cache leakage.
    """

    def _build_key(self, principal, query_fp, lifecycle=None, target_types=None, disclosure_level="metadata"):
        parts = [principal.value]
        parts.append(query_fp)
        parts.append(tuple(sorted(lifecycle)) if lifecycle else ())
        parts.append(tuple(sorted(target_types)) if target_types else ())
        parts.append(disclosure_level)
        raw = "|".join(str(p) for p in parts)
        return hashlib.sha256(raw.encode()).hexdigest()

    def get(self, principal, query_fp, lifecycle=None, target_types=None, disclosure_level="metadata"):
        key = self._build_key(principal, query_fp, lifecycle, target_types, disclosure_level)
        return super().get(key)

    def set(self, value, principal, query_fp, lifecycle=None, target_types=None, disclosure_level="metadata", ttl=None, events=None):
        key = self._build_key(principal, query_fp, lifecycle, target_types, disclosure_level)
        return super().set(value, key, ttl=ttl, events=events)



__all__ = ["Cache"]


============================================================
END OF FILE
============================================================

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
