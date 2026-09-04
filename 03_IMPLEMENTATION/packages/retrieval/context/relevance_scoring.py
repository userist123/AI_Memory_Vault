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
