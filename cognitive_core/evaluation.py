import math
from typing import List, Dict, Any, Set, Optional
from cognitive_core.semantic import SemanticProvider

class RetrievalEvaluator:
    """Evaluates retrieval quality using the TRACe framework
    (Utilization, Relevance, Adherence, Completeness) and standard IR metrics (MRR, NDCG@K, Precision@K, Recall@K).
    """

    def __init__(self, semantic_provider: Optional[SemanticProvider] = None):
        self.semantic_provider = semantic_provider

    # TRACe Metrics

    def utilization(self, retrieved_notes: List[Dict[str, Any]], generated_response: str) -> float:
        """Measures the fraction of retrieved notes whose content actually appears in or contributed to the response."""
        if not retrieved_notes or not generated_response:
            return 0.0
        used_count = 0
        resp_lower = generated_response.lower()
        for note in retrieved_notes:
            content = note.get("content", "").lower()
            # Simple keyword / substring presence check or n-gram overlap
            keywords = [w for w in content.split() if len(w) > 4]
            if not keywords:
                continue
            matched_keywords = sum(1 for kw in keywords if kw in resp_lower)
            if matched_keywords / len(keywords) >= 0.2:
                used_count += 1
        return min(1.0, used_count / len(retrieved_notes))

    def relevance(self, retrieved_notes: List[Dict[str, Any]], query: str) -> float:
        """Measures average semantic relevance of retrieved notes against the query."""
        if not retrieved_notes or not query or not self.semantic_provider:
            return 0.0
        scores = [
            self.semantic_provider.compute_similarity(query, note.get("content", ""))
            for note in retrieved_notes
        ]
        return sum(scores) / len(scores) if scores else 0.0

    def adherence(self, generated_response: str, retrieved_notes: List[Dict[str, Any]]) -> float:
        """Measures factual fidelity of response against retrieved ground truth (checking provenance & source claims)."""
        if not generated_response or not retrieved_notes:
            return 0.0
        # High adherence if claims in response align with retrieved note content
        if not self.semantic_provider:
            return 1.0
        combined_sources = " ".join([n.get("content", "") for n in retrieved_notes])
        return self.semantic_provider.compute_similarity(generated_response, combined_sources)

    def completeness(self, retrieved_notes: List[Dict[str, Any]], gold_reference_ids: List[str]) -> float:
        """Measures whether all gold standard reference notes were successfully retrieved."""
        if not gold_reference_ids:
            return 1.0
        retrieved_ids = {n.get("id") for n in retrieved_notes if n.get("id")}
        matched = sum(1 for gid in gold_reference_ids if gid in retrieved_ids)
        return matched / len(gold_reference_ids)

    # Standard Information Retrieval (IR) Metrics

    @staticmethod
    def precision_at_k(retrieved_ids: List[str], relevant_ids: Set[str], k: int = 5) -> float:
        """Calculate Precision@K."""
        if k <= 0:
            return 0.0
        top_k = retrieved_ids[:k]
        if not top_k:
            return 0.0
        relevant_count = sum(1 for doc_id in top_k if doc_id in relevant_ids)
        return relevant_count / len(top_k)

    @staticmethod
    def recall_at_k(retrieved_ids: List[str], relevant_ids: Set[str], k: int = 5) -> float:
        """Calculate Recall@K."""
        if not relevant_ids:
            return 1.0
        top_k = retrieved_ids[:k]
        relevant_count = sum(1 for doc_id in top_k if doc_id in relevant_ids)
        return relevant_count / len(relevant_ids)

    @staticmethod
    def reciprocal_rank(retrieved_ids: List[str], relevant_ids: Set[str]) -> float:
        """Calculate Reciprocal Rank (RR)."""
        for rank, doc_id in enumerate(retrieved_ids, start=1):
            if doc_id in relevant_ids:
                return 1.0 / rank
        return 0.0

    @classmethod
    def mean_reciprocal_rank(cls, rankings: List[List[str]], relevant_sets: List[Set[str]]) -> float:
        """Calculate Mean Reciprocal Rank (MRR) across multiple queries."""
        if not rankings or not relevant_sets or len(rankings) != len(relevant_sets):
            return 0.0
        rrs = [cls.reciprocal_rank(r, rel) for r, rel in zip(rankings, relevant_sets)]
        return sum(rrs) / len(rrs)

    @staticmethod
    def ndcg_at_k(retrieved_ids: List[str], relevance_scores: Dict[str, float], k: int = 5) -> float:
        """Calculate Normalized Discounted Cumulative Gain (NDCG@K)."""
        top_k = retrieved_ids[:k]
        if not top_k:
            return 0.0

        # DCG
        dcg = 0.0
        for i, doc_id in enumerate(top_k, start=1):
            rel = relevance_scores.get(doc_id, 0.0)
            dcg += rel / math.log2(i + 1)

        # Ideal DCG
        ideal_scores = sorted(relevance_scores.values(), reverse=True)[:k]
        idcg = sum(rel / math.log2(i + 1) for i, rel in enumerate(ideal_scores, start=1))

        if idcg == 0.0:
            return 0.0
        return dcg / idcg
