"""Advisory conflict detection between candidates and existing ACTIVE notes."""
from __future__ import annotations

import re
from typing import Any, Dict, Iterable, List

_NEGATION_TOKENS = {"nu", "niciodata", "niciodat\u0103", "fara", "f\u0103r\u0103", "opus", "contrazice"}
_STOPWORDS = {"si", "\u0219i", "de", "la", "in", "\u00een", "pe", "cu", "un", "o", "the", "and", "to", "of", "a"}


def _tokenize(text: str) -> List[str]:
    tokens = re.findall(r"[a-zA-Z\u0103\u00e2\u00ee\u0219\u021b\u0102\u00c2\u00ce\u0218\u021a0-9]+", text.lower())
    return [t for t in tokens if t in _NEGATION_TOKENS or (t not in _STOPWORDS and len(t) > 2)]


def _jaccard(a: Iterable[str], b: Iterable[str]) -> float:
    sa, sb = set(a), set(b)
    if not sa or not sb:
        return 0.0
    return len(sa & sb) / len(sa | sb)


class ConflictDetector:
    """Heuristic, advisory-only conflict detection. Never blocks or auto-resolves."""

    def __init__(self, overlap_threshold: float = 0.35, max_notes: int = 2000):
        self.overlap_threshold = overlap_threshold
        self.max_notes = max_notes
        self.comparisons_count: int = 0

    def _is_negated(self, tokens: List[str]) -> bool:
        return any(tok in _NEGATION_TOKENS for tok in tokens)

    def detect(self, candidate: Dict[str, Any], existing_notes: Iterable[Dict[str, Any]]) -> List[Dict[str, Any]]:
        notes_list = list(existing_notes)
        if len(notes_list) > self.max_notes:
            raise ValueError(f"Note count {len(notes_list)} exceeds max_notes limit of {self.max_notes}")

        candidate_tokens = _tokenize(str(candidate.get("content", "")))
        candidate_negated = self._is_negated(candidate_tokens)
        flags: List[Dict[str, Any]] = []
        for note in notes_list:
            if note.get("lifecycle") not in {"ACTIVE", "VERIFIED"}:
                continue
            if note.get("category") != candidate.get("category"):
                continue
            self.comparisons_count += 1
            note_tokens = _tokenize(str(note.get("content", "")))
            overlap = _jaccard(candidate_tokens, note_tokens)
            if overlap < self.overlap_threshold:
                continue
            note_negated = self._is_negated(note_tokens)
            severity = "contradiction" if candidate_negated != note_negated else "overlap"
            flags.append({"note_id": note.get("id"), "overlap": round(overlap, 3), "severity": severity})
        return sorted(flags, key=lambda item: item["overlap"], reverse=True)

    def detect_pairs(self, notes: Iterable[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """Deduplicated pairwise conflict detection across a collection of notes.

        Computes pairs (a, b) exactly once with a.id < b.id (O(n*(n-1)/2)),
        preventing redundant computation and strictly enforcing max_notes.
        """
        notes_list = list(notes)
        if len(notes_list) > self.max_notes:
            raise ValueError(f"Note count {len(notes_list)} exceeds max_notes limit of {self.max_notes}")

        active_notes = [n for n in notes_list if n.get("lifecycle") in {"ACTIVE", "VERIFIED"}]
        flags: List[Dict[str, Any]] = []
        n = len(active_notes)

        # Pre-tokenize to avoid repeated regex and lowercasing
        precomputed = [
            (
                str(note.get("id", "")),
                note.get("category"),
                _tokenize(str(note.get("content", ""))),
            )
            for note in active_notes
        ]

        for i in range(n):
            id_a, cat_a, tokens_a = precomputed[i]
            neg_a = self._is_negated(tokens_a)

            for j in range(i + 1, n):
                id_b, cat_b, tokens_b = precomputed[j]
                if cat_a != cat_b:
                    continue

                self.comparisons_count += 1
                overlap = _jaccard(tokens_a, tokens_b)
                if overlap < self.overlap_threshold:
                    continue

                neg_b = self._is_negated(tokens_b)
                severity = "contradiction" if neg_a != neg_b else "overlap"
                pair_a, pair_b = (id_a, id_b) if id_a < id_b else (id_b, id_a)
                flags.append({
                    "note_a": pair_a,
                    "note_b": pair_b,
                    "overlap": round(overlap, 3),
                    "severity": severity,
                })

        return sorted(flags, key=lambda item: item["overlap"], reverse=True)

