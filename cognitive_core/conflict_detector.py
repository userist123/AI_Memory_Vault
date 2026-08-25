"""Advisory conflict detection between candidates and existing ACTIVE notes."""
from __future__ import annotations

import re
from typing import Any, Dict, Iterable, List

_NEGATION_TOKENS = {"nu", "niciodata", "niciodat\u0103", "fara", "f\u0103r\u0103", "opus", "contrazice"}
_STOPWORDS = {"si", "\u0219i", "de", "la", "in", "\u00een", "pe", "cu", "un", "o", "the", "and", "to", "of", "a"}


def _tokenize(text: str) -> List[str]:
    return [t for t in re.findall(r"[a-zA-Z\u0103\u00e2\u00ee\u0219\u021b\u0102\u00c2\u00ce\u0218\u021a0-9]+", text.lower())
            if t not in _STOPWORDS and len(t) > 2]


def _jaccard(a: Iterable[str], b: Iterable[str]) -> float:
    sa, sb = set(a), set(b)
    if not sa or not sb:
        return 0.0
    return len(sa & sb) / len(sa | sb)


class ConflictDetector:
    """Heuristic, advisory-only conflict detection. Never blocks or auto-resolves."""

    def __init__(self, overlap_threshold: float = 0.35):
        self.overlap_threshold = overlap_threshold

    def _is_negated(self, tokens: List[str]) -> bool:
        return any(tok in _NEGATION_TOKENS for tok in tokens)

    def detect(self, candidate: Dict[str, Any], existing_notes: Iterable[Dict[str, Any]]) -> List[Dict[str, Any]]:
        candidate_tokens = _tokenize(str(candidate.get("content", "")))
        candidate_negated = self._is_negated(candidate_tokens)
        flags: List[Dict[str, Any]] = []
        for note in existing_notes:
            if note.get("lifecycle") not in {"ACTIVE", "VERIFIED"}:
                continue
            if note.get("category") != candidate.get("category"):
                continue
            note_tokens = _tokenize(str(note.get("content", "")))
            overlap = _jaccard(candidate_tokens, note_tokens)
            if overlap < self.overlap_threshold:
                continue
            note_negated = self._is_negated(note_tokens)
            severity = "contradiction" if candidate_negated != note_negated else "overlap"
            flags.append({"note_id": note.get("id"), "overlap": round(overlap, 3), "severity": severity})
        return sorted(flags, key=lambda item: item["overlap"], reverse=True)
