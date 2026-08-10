from typing import List, Dict, Any, Tuple
import re
from datetime import datetime, timezone
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.authority import get_authority_score
from .semantic import SemanticProvider
from .working_memory import WorkingMemory
from .version import parse_technology_version, TechnologyIdentity, VersionRange, Version
from .deduplication import extract_tech_and_version

class RecallEngine:
    """
    BRAIN-12: Associative Recall.
    Scores and retrieves notes based on multiple weighted signals:
    - Semantic Similarity (via SemanticProvider)
    - Activation (from ActivationEngine tuples)
    - Confidence
    - Working Memory relevance
    """
    def __init__(self, memory_controller: MemoryController, semantic_provider: SemanticProvider):
        self.controller = memory_controller
        self.semantic_provider = semantic_provider
        
        # Configurable scoring weights
        self.weights = {
            "semantic": 0.35,
            "wm_relevance": 0.15,
            "confidence": 0.15,
            "activation": 0.25,
            "authority": 0.10
        }
        
        self.confidence_map = {
            "very_high": 1.0,
            "high": 0.8,
            "medium": 0.5,
            "low": 0.2,
            "unknown": 0.0
        }
        
    def _score_confidence(self, node: Dict[str, Any]) -> float:
        conf = node.get("confidence", "unknown")
        conf_score = self.confidence_map.get(conf, 0.0)
        # Authority score is derived at runtime
        authority = get_authority_score(node)
        # Combine confidence and authority (both 0-1) by averaging
        return (conf_score + authority) / 2.0

    def _matches_requested_version(self, node: Dict[str, Any], query: str) -> bool:
        # Try parsing technology and version range from query
        q_tech, q_vr = parse_technology_version(query)
        n_tech, n_vr = extract_tech_and_version(node)
        
        if q_tech.name != "unknown" and not q_vr.unknown:
            # If node has a known technology, it must match query technology
            if n_tech.name != "unknown" and n_tech.name != q_tech.name:
                return False
            return q_vr.matches(n_vr)
            
        # Fallback to plain version pattern r"\b\d+\.\d+\b" in query
        m = re.search(r"\b(?P<major>\d+)\.(?P<minor>\d+)\b", query)
        if m:
            major = int(m.group("major"))
            minor = int(m.group("minor"))
            req_vr = VersionRange(exact=Version(major, minor))
            if n_tech.name != "unknown" and not n_vr.unknown:
                return req_vr.matches(n_vr)
                
        return False

    def recall(self, principal: Principal, query: str,
               activated_nodes: List[Tuple[Dict[str, Any], float]],
               working_memory: WorkingMemory) -> List[Tuple[Dict[str, Any], float]]:
        """
        Scores activated nodes against the query and working memory context.
        Accepts (node, activation) tuples directly from ActivationEngine (WIRE-9).
        Returns a sorted list of (node, final_score).
        """
        wm_context = " ".join([n.get("content", "") for n in working_memory.get_active_context()])
        
        # Check if version is requested in the query
        q_tech, q_vr = parse_technology_version(query)
        version_detected = (q_tech.name != "unknown" and not q_vr.unknown) or bool(re.search(r"\b\d+\.\d+\b", query))
        
        # Check for historical/legacy query indicators
        lowered_query = query.lower()
        is_historical_query = any(w in lowered_query for w in ["legacy", "deprecated", "historical", "old", "superseded"])
        
        scored_nodes = []
        
        for node, activation in activated_nodes:
            content = node.get("content", "")
            # Flag unverified if REVIEW lifecycle
            if node.get('lifecycle') == 'REVIEW':
                node['_cognitive_unverified'] = True
            
            # 1. Semantic Similarity to query
            sim_query = self.semantic_provider.compute_similarity(query, content)
            
            # 2. Semantic Similarity to active working memory
            sim_wm = self.semantic_provider.compute_similarity(wm_context, content)
            
            # 3. Temporal decay based on valid_from / valid_until (if present)
            temporal_factor = 1.0
            
            valid_from = node.get('valid_from')
            if valid_from:
                try:
                    start_date = datetime.strptime(valid_from, "%Y-%m-%d")
                    now = datetime.now(timezone.utc).replace(tzinfo=None)
                    if start_date > now:
                        # Not yet valid (in the future)
                        temporal_factor = min(temporal_factor, 0.5)
                except Exception:
                    pass
                    
            valid_until = node.get('valid_until')
            if valid_until:
                try:
                    expiry = datetime.strptime(valid_until, "%Y-%m-%d")
                    now = datetime.now(timezone.utc).replace(tzinfo=None)
                    if expiry < now:
                        # Expired notes get a penalty factor (less penalty if historical query)
                        factor = 0.8 if is_historical_query else 0.5
                        temporal_factor = min(temporal_factor, factor)
                except Exception:
                    pass
            
            # 4. Confidence & authority combined score (handled in _score_confidence)
            conf_auth_score = self._score_confidence(node)
            
            # 5. Version-aware boost
            if version_detected:
                if self._matches_requested_version(node, query):
                    # Boost confidence score by 0.3 if matching version range
                    conf_auth_score = min(1.0, conf_auth_score + 0.3)
                else:
                    n_tech, n_vr = extract_tech_and_version(node)
                    if n_tech.name != "unknown" and not n_vr.unknown:
                        # Penalty if mismatched version range
                        conf_auth_score = max(0.0, conf_auth_score - 0.3)
            
            # 6. Activation score from ActivationEngine
            final_score = (
                (sim_query * self.weights["semantic"]) +
                (sim_wm * self.weights["wm_relevance"]) +
                (conf_auth_score * self.weights["confidence"]) +
                (activation * self.weights["activation"]) +
                (temporal_factor * self.weights["authority"])
            )
            
            # 7. Lifecycle down-ranking for historical/superseded notes
            lifecycle = node.get("lifecycle")
            if lifecycle == "SUPERSEDED":
                # Only minimal penalty if explicitly querying history, otherwise heavy penalty
                lifecycle_factor = 0.8 if is_historical_query else 0.3
                final_score *= lifecycle_factor
            elif lifecycle == "ARCHIVED":
                lifecycle_factor = 0.6 if is_historical_query else 0.1
                final_score *= lifecycle_factor
            
            scored_nodes.append((node, final_score))
            
        # Include REVIEW notes from storage to ensure they appear in WM with unverified flag
        for note_id in self.controller.storage.id_to_path.keys():
            note = self.controller.storage.get(note_id)
            if note and note.get('lifecycle') == 'REVIEW':
                # Check if note already in scored_nodes
                found = False
                for existing_node, _ in scored_nodes:
                    if existing_node.get('id') == note.get('id'):
                        existing_node['_cognitive_unverified'] = True
                        found = True
                        break
                if not found:
                    note_copy = note.copy()
                    note_copy['_cognitive_unverified'] = True
                    scored_nodes.append((note_copy, 0.0))
        # Sort descending by score, tie-break by ID
        scored_nodes.sort(key=lambda x: (x[1], x[0].get("id", "")), reverse=True)
        return scored_nodes
