"""
Multi-Signal Associative Recall Engine.
Combines BM25, Semantic Cosine, ACT-R Decay, Graph Spreading, and Lineage Resolution.
"""

from typing import List, Dict, Any, Tuple, Optional
import re
from datetime import datetime, timezone

from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.activation import (
    SpreadingActivationEngine,
    ActivationTracker,
    DORMANT_THRESHOLD,
)


class MultiSignalRecallEngine:
    """Associative recall combining lexical, semantic, activation, and lineage signals."""

    def __init__(
        self,
        storage_engine: SQLiteStorageEngine,
        weights: Optional[Dict[str, float]] = None,
    ):
        self.storage = storage_engine
        self.spreading_engine = SpreadingActivationEngine()
        self.tracker = ActivationTracker.get_instance()

        self.weights = weights or {
            "semantic": 0.35,
            "activation": 0.25,
            "wm_relevance": 0.15,
            "confidence": 0.15,
            "authority": 0.10,
        }

        self.confidence_map = {
            "very_high": 1.0,
            "high": 0.8,
            "medium": 0.5,
            "low": 0.2,
            "unknown": 0.0,
        }

    def _compute_token_cosine_similarity(self, text_a: str, text_b: str) -> float:
        """Compute token-level cosine similarity as deterministic semantic fallback."""
        if not text_a or not text_b:
            return 0.0

        def get_tokens(text: str) -> Dict[str, int]:
            words = re.findall(r"\w+", text.lower())
            freq = {}
            for w in words:
                freq[w] = freq.get(w, 0) + 1
            return freq

        tf_a = get_tokens(text_a)
        tf_b = get_tokens(text_b)

        common_keys = set(tf_a.keys()).intersection(tf_b.keys())
        if not common_keys:
            return 0.0

        dot_product = sum(tf_a[k] * tf_b[k] for k in common_keys)
        mag_a = math_sqrt = sum(v * v for v in tf_a.values()) ** 0.5
        mag_b = sum(v * v for v in tf_b.values()) ** 0.5

        if mag_a == 0.0 or mag_b == 0.0:
            return 0.0
        return dot_product / (mag_a * mag_b)

    def _score_confidence(self, node: Dict[str, Any]) -> float:
        conf_str = node.get("confidence", "medium")
        base_score = self.confidence_map.get(conf_str, 0.5)

        # Boost if verified
        if node.get("verification") == "verified":
            base_score = min(1.0, base_score + 0.2)
        return base_score

    def _matches_version(self, node: Dict[str, Any], query: str) -> Optional[bool]:
        """Check for version match between query and note metadata/content."""
        q_match = re.search(r"\bv?(\d+\.\d+(?:\.\d+)?)\b", query)
        if not q_match:
            return None

        q_ver = q_match.group(1)
        node_ver_range = node.get("version_range") or ""
        node_content = node.get("content", "")

        if q_ver in node_ver_range or q_ver in node_content:
            return True
        return False

    def retrieve(
        self,
        query: str,
        working_memory_context: Optional[List[Dict[str, Any]]] = None,
        limit: int = 10,
    ) -> List[Tuple[Dict[str, Any], float]]:
        """
        Execute multi-signal associative recall.
        Returns sorted list of (node_dict, composite_score).
        """
        # 1. BM25 / Lexical candidate selection
        bm25_candidates = self.storage.search_bm25(query, limit=limit * 2)

        # 2. Spreading activation across relations and wikilinks
        activated_nodes = self.spreading_engine.spread_activation(
            bm25_candidates, storage_fetch_func=self.storage.get
        )

        wm_context_str = " ".join(
            [n.get("content", "") for n in (working_memory_context or [])]
        )
        is_historical = any(
            w in query.lower() for w in ["legacy", "deprecated", "historical", "old", "superseded"]
        )

        scored_nodes = []
        pre_lifecycle_scores: Dict[str, float] = {}

        for node, act_val in activated_nodes:
            content = node.get("content", "")
            node_id = node.get("id", "")

            # Tag unverified if in REVIEW lifecycle
            if node.get("lifecycle") == "REVIEW":
                node["_cognitive_unverified"] = True

            # Semantic similarity to query
            sim_query = self._compute_token_cosine_similarity(query, content)

            # Semantic similarity to working memory
            sim_wm = (
                self._compute_token_cosine_similarity(wm_context_str, content)
                if wm_context_str
                else 0.0
            )

            # Confidence and authority
            conf_score = self._score_confidence(node)

            # Version matching boost/penalty
            ver_match = self._matches_version(node, query)
            if ver_match is True:
                conf_score = min(1.0, conf_score + 0.3)
            elif ver_match is False:
                conf_score = max(0.0, conf_score - 0.3)

            # Temporal validity factor
            temporal_factor = 1.0
            valid_until = node.get("valid_until")
            if valid_until:
                try:
                    expiry = datetime.strptime(valid_until, "%Y-%m-%d")
                    now = datetime.now(timezone.utc).replace(tzinfo=None)
                    if expiry < now:
                        temporal_factor = 0.8 if is_historical else 0.5
                except Exception:
                    pass

            composite_score = (
                (sim_query * self.weights["semantic"])
                + (act_val * self.weights["activation"])
                + (sim_wm * self.weights["wm_relevance"])
                + (conf_score * self.weights["confidence"])
                + (temporal_factor * self.weights["authority"])
            )

            pre_lifecycle_scores[node_id] = composite_score

            # Lifecycle down-ranking
            lifecycle = node.get("lifecycle")
            if lifecycle == "SUPERSEDED":
                composite_score *= 0.8 if is_historical else 0.3
            elif lifecycle == "ARCHIVED":
                composite_score *= 0.6 if is_historical else 0.1

            scored_nodes.append((node, composite_score))

        # 3. CTE Lineage Resolution: Pull active successor for top superseded notes
        successors_to_add: Dict[str, Tuple[Dict[str, Any], float]] = {}
        for node, score in scored_nodes:
            if node.get("lifecycle") == "SUPERSEDED" and node.get("superseded_by"):
                lineage = self.storage.get_lineage(node.get("id"))
                for succ in lineage:
                    if succ.get("lifecycle") == "ACTIVE":
                        succ_id = succ.get("id")
                        orig_score = pre_lifecycle_scores.get(node.get("id"), score)
                        boosted_score = min(1.0, orig_score * 1.1)
                        if succ_id not in successors_to_add or boosted_score > successors_to_add[succ_id][1]:
                            successors_to_add[succ_id] = (succ, boosted_score)

        for succ_id, (succ_node, succ_score) in successors_to_add.items():
            existing_idx = next(
                (i for i, (n, _) in enumerate(scored_nodes) if n.get("id") == succ_id), None
            )
            if existing_idx is not None:
                if succ_score > scored_nodes[existing_idx][1]:
                    scored_nodes[existing_idx] = (succ_node, succ_score)
            else:
                scored_nodes.append((succ_node, succ_score))

        # Sort descending by score
        scored_nodes.sort(key=lambda x: (x[1], x[0].get("id", "")), reverse=True)

        # Record access for top recalled nodes
        for node, score in scored_nodes[:limit]:
            nid = node.get("id")
            if nid and score > 0.05:
                self.tracker.record_access(nid)

        return scored_nodes[:limit]
