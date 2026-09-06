from typing import List, Dict
import math

CONFIDENCE_MAP = {
    'very_high': 1.0,
    'high': 0.9,
    'medium': 0.5,
    'low': 0.2,
    'unknown': 0.0
}


class RelevanceScorer:
    """Simple relevance scoring based on token overlap and confidence.

    The score is a float between 0 and 1.

    `score()` is unchanged (r024 WP-1 forbids touching graph expansion and
    must not remove confidence from the pack/trace; it stays the production
    default). `score_components()` exposes the same two signals unblended,
    for r024 WP-1 Phase B's ranking arms, which recombine them differently
    without recomputing overlap or the confidence string mapping a second
    time -- there is exactly one place that decides what "confidence" means
    numerically.
    """

    def __init__(self):
        pass

    def score_components(self, query: str, notes: List[Dict[str, any]]) -> List[Dict[str, any]]:
        """Returns {"id", "overlap_ratio", "confidence"} per note, unblended."""
        query_tokens = set(query.lower().split())
        out = []
        for note in notes:
            content = note.get("content", "")
            note_tokens = set(content.lower().split())
            overlap = query_tokens.intersection(note_tokens)
            overlap_ratio = len(overlap) / max(len(query_tokens), 1)
            confidence = note.get("confidence", 0.5)
            if isinstance(confidence, str):
                confidence = CONFIDENCE_MAP.get(confidence.lower(), 0.5)
            out.append({"id": note.get("id"), "overlap_ratio": overlap_ratio, "confidence": confidence})
        return out

    def score(self, query: str, notes: List[Dict[str, any]]) -> List[Dict[str, any]]:
        components = self.score_components(query, notes)
        scored = [
            {"id": c["id"], "score": (c["overlap_ratio"] + c["confidence"]) / 2}
            for c in components
        ]
        scored.sort(key=lambda x: x["score"], reverse=True)
        return scored
