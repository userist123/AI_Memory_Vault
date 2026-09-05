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

# Canonical lifecycle policy
from .lifecycle_policy import Mutation as LifecycleMutation, evaluate as evaluate_lifecycle_mutation

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
                transition_mutations = {
                    (Lifecycle.RAW, Lifecycle.CLASSIFIED): LifecycleMutation.CLASSIFY,
                    (Lifecycle.CLASSIFIED, Lifecycle.NORMALIZED): LifecycleMutation.NORMALIZE,
                    (Lifecycle.NORMALIZED, Lifecycle.REVIEW): LifecycleMutation.REVIEW,
                    (Lifecycle.REVIEW, Lifecycle.VERIFIED): LifecycleMutation.VERIFY,
                    (Lifecycle.REVIEW, Lifecycle.ACTIVE): LifecycleMutation.PROMOTE,
                    (Lifecycle.VERIFIED, Lifecycle.ACTIVE): LifecycleMutation.PROMOTE,
                    (Lifecycle.ACTIVE, Lifecycle.RECONSOLIDATING): LifecycleMutation.RECONSOLIDATE_CHALLENGE,
                    (Lifecycle.VERIFIED, Lifecycle.RECONSOLIDATING): LifecycleMutation.RECONSOLIDATE_CHALLENGE,
                    (Lifecycle.RECONSOLIDATING, Lifecycle.REVIEW): LifecycleMutation.RECONSOLIDATE_RESOLVE,
                    (Lifecycle.REVIEW, Lifecycle.ARCHIVED): LifecycleMutation.ARCHIVE,
                    (Lifecycle.ACTIVE, Lifecycle.ARCHIVED): LifecycleMutation.ARCHIVE,
                    (Lifecycle.ACTIVE, Lifecycle.SUPERSEDED): LifecycleMutation.SUPERSEDE,
                }
                mutation = transition_mutations.get((old_lifecycle, new_lifecycle))
                if mutation is None:
                    raise ValueError(f"Invalid transition from {old_lifecycle} to {new_lifecycle}")
                if not evaluate_lifecycle_mutation(
                    old_lifecycle,
                    new_lifecycle,
                    mutation=mutation,
                    verification=note.get('verification'),
                ):
                    raise ValueError(f"Invalid lifecycle transition from {old_lifecycle} to {new_lifecycle} for {mutation.value}")

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
                if level == 'metadata': return pd.metadata_only([note])
                if level == 'snippet': return pd.snippet([note])
                if level == 'sections': return pd.sections([note], "")
                if level == 'full': return pd.full_document([note])
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
                        item = item.copy(); item['content'] = summarize_note(item, max_chars=budget.soft_context_budget // 2)
                    compressed.append(item)
                disclosed = compressed
            for res in disclosed:
                prov = note.get('provenance', {})
                res.setdefault('provenance', {})
                res['provenance'].setdefault('source_type', prov.get('source_type'))
                res['provenance'].setdefault('source_ref', prov.get('source_ref'))
            pack = self.pack_builder.build(request_id="read", agent_id=principal.value, budget={}, results=disclosed, disclosure_level=disclosure_level, minimal_provenance=None, next_page_token=None, audit_ref=None)
            audit_event('read', principal, note_id, success=True)
            return pack
        except Exception as e:
            audit_event('read', principal, note_id, success=False, details={'error': str(e)})
            raise

    _COGNITIVE_ELIGIBLE = {Lifecycle.ACTIVE, Lifecycle.REVIEW}

    def cognitive_read(self, principal: Principal, note_id: str) -> Dict[str, Any]:
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
            if lc == Lifecycle.REVIEW.value: result['_cognitive_unverified'] = True
            pack = self.pack_builder.build(request_id="cognitive_read", agent_id=principal.value, budget={}, results=[result], disclosure_level='full', minimal_provenance=None, next_page_token=None, audit_ref=None)
            audit_event('cognitive_read', principal, note_id, success=True)
            return pack
        except Exception as e:
            audit_event('cognitive_read', principal, note_id, success=False, details={'error': str(e)})
            raise

    def search(self, principal: Principal, query: str, page_size: int = 10, page_token: Optional[str] = None, lifecycles: Optional[List[Lifecycle]] = None, types: Optional[List[str]] = None) -> Dict[str, Any]:
        target_id = "unknown_query"
        try:
            self._check_auth(principal, Operation.SEARCH)
            disclosure_level = getattr(self, 'default_disclosure', 'metadata')
            check_query_size(query); sanitized = sanitize_query(query)
            query_fp = hashlib.sha256(sanitized.encode()).hexdigest(); target_id = query_fp
            budget = load_agent_budget(principal.value); classified = self.query_classifier.classify(sanitized)
            if lifecycles is not None: classified['lifecycle_filters'] = [l.value if isinstance(l, Lifecycle) else l for l in lifecycles]
            if types is not None: classified['target_types'] = types
            offset = 0
            if page_token:
                payload = PaginationToken.decode(page_token)
                if payload.get('query_fp') != query_fp: raise InvalidPaginationTokenError('Token query fingerprint does not match current request')
                if payload.get('agent_id') != principal.value: raise InvalidPaginationTokenError('Token principal does not match current request')
                token_lifecycles = payload.get('lifecycles', []); req_lifecycles = [l.value if isinstance(l, Lifecycle) else l for l in (lifecycles or [])]
                if token_lifecycles != req_lifecycles: raise InvalidPaginationTokenError('Token lifecycle filters do not match current request')
                token_types = payload.get('types', []); req_types = types or []
                if token_types != req_types: raise InvalidPaginationTokenError('Token type filters do not match current request')
                if payload.get('disclosure') != disclosure_level: raise InvalidPaginationTokenError('Token disclosure level does not match current request')
                if payload.get('page_size') != page_size: raise InvalidPaginationTokenError('Token page size does not match current request')
                offset = payload.get('offset', 0)
            notes = self.retrieval_engine.retrieve(classified, principal, query_fp, disclosure_level, budget, offset=offset)
            scored = self.scorer.score(sanitized, notes); score_map = {s['id']: s['score'] for s in scored}
            notes = sorted(notes, key=lambda n: score_map.get(n.get('id'), 0), reverse=True)
            pd = ProgressiveDisclosure(budget)
            if disclosure_level == 'metadata': disclosed = pd.metadata_only(notes)
            elif disclosure_level == 'snippet': disclosed = pd.snippet(notes)
            elif disclosure_level == 'sections': disclosed = pd.sections(notes, sanitized)
            else: disclosed = pd.full_document(notes)
            total = len(disclosed); end = min(offset + page_size, total); page_results = disclosed[offset:end]; next_token = None
            if end < total:
                payload = {'offset': end, 'query_fp': hashlib.sha256(sanitized.encode()).hexdigest(), 'agent_id': principal.value, 'page_size': page_size, 'lifecycles': [l.value if isinstance(l, Lifecycle) else l for l in (lifecycles or [])], 'types': types or [], 'disclosure': disclosure_level, 'expiration': int((datetime.now(timezone.utc) + timedelta(minutes=10)).timestamp())}
                secret = os.getenv('MEMORY_CONTROLLER_HMAC_SECRET')
                if not secret: raise MissingHMACSecretError('HMAC secret not configured')
                next_token = PaginationToken(payload, secret.encode()).encode()
            try:
                pack = self.pack_builder.build(request_id='search', agent_id=principal.value, budget={'soft': budget.soft_context_budget, 'hard': budget.hard_context_budget}, results=page_results, disclosure_level=disclosure_level, minimal_provenance=None, next_page_token=next_token, audit_ref=None)
            except BudgetExceededError:
                pack = {'requestId': 'search', 'agentId': principal.value, 'budget': {'soft': budget.soft_context_budget, 'hard': budget.hard_context_budget}, 'disclosureLevel': disclosure_level, 'results': []}
            pack['next_page_token'] = next_token
            audit_event('search', principal, target_id, success=True, details={'page_size': page_size, 'offset': offset})
            return pack
        except Exception as e:
            audit_event('search', principal, target_id, success=False, details={'error': str(e)}); raise

    def search_financial(self, principal: Principal = Principal.AI_AGENT, query: str = "", symbol: Optional[str] = None, symbols: Optional[List[str]] = None, asset_symbol: Optional[str] = None, category: Optional[str] = None, asset_classes: Optional[List[str]] = None, min_confidence: Optional[str] = None, confidence_min: Optional[str] = None, verification_state: Optional[str] = None, verification_states: Optional[List[str]] = None, date_from: Optional[str] = None, date_to: Optional[str] = None, types: Optional[List[str]] = None, lifecycles: Optional[List[Lifecycle]] = None, page_size: int = 10, limit: Optional[int] = None, page_token: Optional[str] = None, disclosure_level: Optional[str] = None) -> Dict[str, Any]:
        self._check_auth(principal, Operation.SEARCH)
        effective_disclosure = disclosure_level or getattr(self, 'default_disclosure', 'metadata')
        return self.financial_search_engine.execute_search(principal=principal, query=query, symbol=symbol, symbols=symbols, asset_symbol=asset_symbol, category=category, asset_classes=asset_classes, min_confidence=min_confidence, confidence_min=confidence_min, verification_state=verification_state, verification_states=verification_states, date_from=date_from, date_to=date_to, types=types, lifecycles=lifecycles, page_size=page_size, limit=limit, page_token=page_token, disclosure_level=effective_disclosure)

    def propose(self, principal: Principal, note_data: Dict[str, Any]) -> str:
        with self._mutation_lock:
            note_id = note_data.get('id', 'unknown')
            try:
                self._check_auth(principal, Operation.PROPOSE)
                if not note_data.get('id'): raise ValueError('Note must include an id')
                check_path_traversal(note_id)
                if str(note_data.get('verification', '')).strip().lower() == 'verified':
                    raise ValueError("Verification status 'verified' cannot be set via propose. Use attest() instead.")
                now_date = datetime.now(timezone.utc).date().isoformat()
                defaults = {'type': 'knowledge', 'category': 'test', 'tags': [], 'created': now_date, 'updated': now_date, 'provenance': {'source_type': 'user' if principal in {Principal.HUMAN, Principal.ADMIN} else 'inference', 'source_ref': 'generated'}, 'confidence': 'high', 'verification': 'unverified', 'relations': [], 'lifecycle': Lifecycle.RAW.value}
                note = defaults.copy(); note.update(note_data)
                prov = defaults['provenance'].copy(); prov.update(note_data.get('provenance', {})); note['provenance'] = prov; note['id'] = note_id
                if str(note.get('verification', '')).strip().lower() == 'verified': raise ValueError("Verification status 'verified' cannot be set via propose. Use attest() instead.")
                source_type = note['provenance'].get('source_type', 'unknown'); allowed_sources = _ALLOWED_PROVENANCE_SOURCE_TYPES.get(principal, {"unknown"})
                if source_type not in allowed_sources: raise ValueError(f"Principal '{principal.value}' is not permitted to claim provenance source_type '{source_type}'")
                lifecycle_val = note.get('lifecycle')
                if isinstance(lifecycle_val, Lifecycle): lifecycle_val = lifecycle_val.value; note['lifecycle'] = lifecycle_val
                if lifecycle_val not in _PERMITTED_CREATION_LIFECYCLES:
                    raise ValueError(f"Principal '{principal.value}' cannot set lifecycle to '{lifecycle_val}' at creation. Permitted creation states: RAW, CLASSIFIED, NORMALIZED, REVIEW.")
                note['created'] = note_data.get('created', now_date); note['updated'] = note_data.get('updated', now_date)
                validation_note = {k: v for k, v in note.items() if k != 'content'}
                self._validate_note(validation_note); self.storage.set(note_id, note); self.cache.invalidate_by_event('memory_updated')
                if hasattr(self, 'financial_search_engine') and self.financial_search_engine is not None: self.financial_search_engine.index_note(note)
                audit_event('propose', principal, note_id, success=True); return note_id
            except Exception as e:
                audit_event('propose', principal, note_id, success=False, details={'error': str(e)}); raise

    def review(self, principal: Principal, note_id: str, decision: str, comments: Optional[str] = None) -> None:
        with self._mutation_lock:
            try:
                self._check_auth(principal, Operation.REVIEW); check_path_traversal(note_id); note = self.storage.get(note_id)
                if not note: raise ValueError('Note not found')
                if note['lifecycle'] not in {Lifecycle.RAW, Lifecycle.CLASSIFIED, Lifecycle.NORMALIZED, Lifecycle.REVIEW}: raise ValueError('Only RAW/CLASSIFIED/NORMALIZED/REVIEW notes can be reviewed')
                if decision not in {'agree', 'approve', 'reject'}: raise ValueError('Decision must be approve or reject')
                if not evaluate_lifecycle_mutation(note['lifecycle'], Lifecycle.REVIEW.value, mutation=LifecycleMutation.REVIEW, verification=note.get('verification')):
                    raise ValueError(f"Invalid lifecycle transition from {note['lifecycle']} to {Lifecycle.REVIEW.value} for {LifecycleMutation.REVIEW.value}")
                note['lifecycle'] = Lifecycle.REVIEW; self.storage.set(note_id, note); review_id = f"r{MemoryController._global_review_counter}"; MemoryController._global_review_counter += 1
                self.storage.set(review_id, {'id': review_id, 'review': {'by': principal.value, 'decision': decision, 'comments': comments}}); self.cache.invalidate_by_event('memory_updated'); audit_event('review', principal, note_id, success=True, details={'decision': decision})
            except Exception as e: audit_event('review', principal, note_id, success=False, details={'decision': decision, 'error': str(e)}); raise

    def promote(self, principal: Principal, note_id: str) -> None:
        with self._mutation_lock:
            try:
                self._check_auth(principal, Operation.PROMOTE); check_path_traversal(note_id); note = self.storage.get(note_id)
                if not note: raise ValueError('Note not found')
                if note['lifecycle'] != Lifecycle.REVIEW: raise ValueError('Only REVIEW notes can be promoted')
                if not evaluate_lifecycle_mutation(note['lifecycle'], Lifecycle.ACTIVE.value, mutation=LifecycleMutation.PROMOTE, verification=note.get('verification')):
                    raise ValueError('Only VERIFIED notes can be promoted to ACTIVE')
                note['lifecycle'] = Lifecycle.ACTIVE; self.storage.set(note_id, note); self.cache.invalidate_by_event('memory_updated'); audit_event('promote', principal, note_id, success=True)
            except Exception as e: audit_event('promote', principal, note_id, success=False, details={'error': str(e)}); raise

    def update(self, principal: Principal, note_id: str, updates: Optional[Dict[str, Any]] = None, **kwargs) -> None:
        with self._mutation_lock:
            try:
                self._check_auth(principal, Operation.UPDATE); check_path_traversal(note_id); updates = updates or {}; updates = {**updates, **kwargs} if kwargs else updates; note = self.storage.get(note_id)
                if not note: raise ValueError('Note not found')
                if note['lifecycle'] != Lifecycle.ACTIVE:
                    if principal == Principal.AI_AGENT and note['lifecycle'] in {Lifecycle.RAW, Lifecycle.CLASSIFIED, Lifecycle.NORMALIZED}: pass
                    else: raise ValueError('Updates not permitted for this lifecycle and principal')
                immutable = {'id', 'lifecycle'}
                for k in immutable:
                    if k in updates and updates[k] != note.get(k): raise ValueError(f'Field {k} is immutable')
                if str(updates.get('verification', '')).strip().lower() == 'verified': raise ValueError("Verification status 'verified' cannot be escalated via update. Use attest() instead.")
                if 'provenance' in updates and isinstance(updates['provenance'], dict) and 'source_type' in updates['provenance']:
                    new_st = updates['provenance']['source_type']; old_st = note.get('provenance', {}).get('source_type')
                    if new_st != old_st: raise ValueError(f"Field provenance.source_type is immutable post-creation (existing: '{old_st}', attempted: '{new_st}')")
                old_valid_until = note.get('valid_until'); new_valid_until = updates.get('valid_until'); has_valid_until_changed = 'valid_until' in updates and old_valid_until != new_valid_until
                note.update(updates); note['updated'] = datetime.now(timezone.utc).date().isoformat(); self._validate_note(note); self.storage.set(note_id, note); self.cache.invalidate_by_event('memory_updated')
                if hasattr(self, 'financial_search_engine') and self.financial_search_engine is not None: self.financial_search_engine.index_note(note)
                if has_valid_until_changed: audit_event('valid_until_update', principal, note_id, success=True, details={'old_valid_until': old_valid_until, 'new_valid_until': new_valid_until})
                else: audit_event('update', principal, note_id, success=True)
            except Exception as e: audit_event('update', principal, note_id, success=False, details={'error': str(e)}); raise

    def attest(self, principal: Principal, note_id: str, verification_reason: str, evidence_reference: str, verification_state: str = "verified") -> None:
        with self._mutation_lock:
            try:
                self._check_auth(principal, Operation.ATTEST); check_path_traversal(note_id)
                if not verification_reason or not verification_reason.strip(): raise ValueError("Attestation requires a non-empty verification_reason")
                if not evidence_reference or not evidence_reference.strip(): raise ValueError("Attestation requires a non-empty evidence_reference")
                note = self.storage.get(note_id)
                if not note: raise ValueError('Note not found')
                previous_state = note.get('verification', 'unverified')
                if previous_state == verification_state: return
                now_date = datetime.now(timezone.utc).date().isoformat(); note['verification'] = verification_state; note['verification_source'] = principal.value; note['last_verified'] = now_date; note['updated'] = now_date
                validation_note = {k: v for k, v in note.items() if k != 'content'}; self._validate_note(validation_note); self.storage.set(note_id, note); self.cache.invalidate_by_event('memory_updated')
                audit_event('attest', principal, note_id, success=True, details={'attested_by': principal.value, 'reason': verification_reason, 'evidence_reference': evidence_reference, 'previous_verification_state': previous_state, 'new_verification_state': verification_state})
            except Exception as e: audit_event('attest', principal, note_id, success=False, details={'attested_by': principal.value, 'reason': verification_reason if 'verification_reason' in locals() else '', 'evidence_reference': evidence_reference if 'evidence_reference' in locals() else '', 'error': str(e)}); raise

    def archive(self, principal: Principal, note_id: str, reason: str) -> None:
        with self._mutation_lock:
            try:
                self._check_auth(principal, Operation.ARCHIVE); check_path_traversal(note_id); note = self.storage.get(note_id)
                if not note: raise ValueError('Note not found')
                if not reason or not reason.strip(): raise ValueError('Archive requires a non-empty reason')
                if note.get('lifecycle') not in {Lifecycle.REVIEW.value, Lifecycle.ACTIVE.value}: raise ValueError('Only REVIEW or ACTIVE notes can be archived')
                if not evaluate_lifecycle_mutation(note['lifecycle'], Lifecycle.ARCHIVED.value, mutation=LifecycleMutation.ARCHIVE, verification=note.get('verification')):
                    raise ValueError(f"Invalid lifecycle transition from {note['lifecycle']} to {Lifecycle.ARCHIVED.value} for {LifecycleMutation.ARCHIVE.value}")
                note['lifecycle'] = Lifecycle.ARCHIVED; note['archive_reason'] = reason; self.storage.set(note_id, note); self.cache.invalidate_by_event('memory_updated'); audit_event('archive', principal, note_id, success=True, details={'reason': reason})
            except Exception as e: audit_event('archive', principal, note_id, success=False, details={'reason': reason, 'error': str(e)}); raise

    def supersede(self, principal: Principal, old_id: str, new_id: str, evidence: str = "") -> None:
        with self._mutation_lock:
            try:
                self._check_auth(principal, Operation.SUPERSEDE); check_path_traversal(old_id); check_path_traversal(new_id); self.supersession_enforcer.validate_supersession(principal, old_id, new_id)
                old_note = self.storage.get(old_id); new_note = self.storage.get(new_id); old_note_orig = old_note.copy(); new_note_orig = new_note.copy(); now_date = datetime.now(timezone.utc).date().isoformat()
                if not evaluate_lifecycle_mutation(old_note['lifecycle'], Lifecycle.SUPERSEDED.value, mutation=LifecycleMutation.SUPERSEDE, verification=old_note.get('verification')):
                    raise ValueError(f"Invalid lifecycle transition from {old_note['lifecycle']} to {Lifecycle.SUPERSEDED.value} for {LifecycleMutation.SUPERSEDE.value}")
                old_note["lifecycle"] = Lifecycle.SUPERSEDED.value; old_note["superseded_by"] = new_id; old_note["updated"] = now_date
                if not any(r.get("target_id") == new_id and r.get("relation") == "replaced_by" for r in old_note.get("relations", [])): old_note.setdefault("relations", []).append({"relation": "replaced_by", "target": new_note.get("type", "knowledge"), "target_id": new_id})
                new_note["supersedes"] = old_id; new_note["updated"] = now_date
                if not any(r.get("target_id") == old_id and r.get("relation") == "replaces" for r in new_note.get("relations", [])): new_note.setdefault("relations", []).append({"relation": "replaces", "target": old_note.get("type", "knowledge"), "target_id": old_id})
                try:
                    self.storage.set(old_id, old_note)
                    try: self.storage.set(new_id, new_note)
                    except Exception as e: self.storage.set(old_id, old_note_orig); raise e
                except Exception as e: raise ValueError(f"Atomic supersession write failed: {str(e)}")
                self.cache.invalidate_by_event('memory_updated'); audit_event('supersede', principal, new_id, success=True, details={'old_id': old_id, 'evidence': evidence}); audit_event('archive_superseded', principal, old_id, success=True, details={'new_id': new_id})
            except Exception as e: audit_event('supersede', principal, new_id, success=False, details={'old_id': old_id, 'evidence': evidence, 'error': str(e)}); raise

from .storage.file_engine import FileStorageEngine
_vault_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
_storage_engine = FileStorageEngine(_vault_root)
controller = MemoryController(_storage_engine)
