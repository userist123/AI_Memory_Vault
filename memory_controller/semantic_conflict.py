"""Conservative semantic contradiction detector.

Detection only: it never mutates, promotes, archives, or deletes memory.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import date
import re
from typing import Any, Mapping, Optional


@dataclass(frozen=True)
class SemanticConflict:
    left_id: str
    right_id: str
    score: float
    reasons: tuple[str, ...]
    status: str = "potential_conflict"


def _as_date(value: Any) -> Optional[date]:
    if not value:
        return None
    if isinstance(value, date):
        return value
    try:
        return date.fromisoformat(str(value)[:10])
    except (TypeError, ValueError):
        return None


def _text(note: Mapping[str, Any]) -> str:
    return str(note.get("content") or "").strip().lower()


def _subject_key(note: Mapping[str, Any]) -> str:
    """Build a semantic subject key without binding it to the source.

    ``source_ref`` identifies provenance, not subject identity. Two different
    sources about the same technology/category must remain comparable.
    """
    tech = str(note.get("technology") or note.get("tech") or "")
    category = str(note.get("category") or "")
    explicit_subject = str(note.get("conflict_subject") or "")
    parts = [explicit_subject or tech, category]
    return "|".join(x.strip().lower() for x in parts if x.strip())


def _temporal_overlap(a: Mapping[str, Any], b: Mapping[str, Any], as_of: Optional[date]) -> bool:
    if as_of is not None:
        def active_at(note: Mapping[str, Any]) -> bool:
            start = _as_date(note.get("valid_from"))
            end = _as_date(note.get("valid_until"))
            return (start is None or as_of >= start) and (end is None or as_of <= end)
        return active_at(a) and active_at(b)

    a_start, b_start = _as_date(a.get("valid_from")), _as_date(b.get("valid_from"))
    a_end, b_end = _as_date(a.get("valid_until")), _as_date(b.get("valid_until"))
    if a_end is not None and b_start is not None and a_end < b_start:
        return False
    if b_end is not None and a_start is not None and b_end < a_start:
        return False
    return True


def _polarity(text: str) -> int:
    positive = {"supported", "allows", "allowed", "enabled", "true", "yes", "valid", "recommended"}
    negative = {"unsupported", "disallowed", "disabled", "false", "no", "invalid", "deprecated", "removed", "not supported"}
    score = 0
    for word in positive:
        if re.search(rf"\b{re.escape(word)}\b", text):
            score += 1
    for word in negative:
        if re.search(rf"\b{re.escape(word)}\b", text):
            score -= 1
    return score


def detect_pair(left: Mapping[str, Any], right: Mapping[str, Any], *, as_of: Optional[date] = None) -> Optional[SemanticConflict]:
    left_id, right_id = str(left.get("id") or ""), str(right.get("id") or "")
    if not left_id or not right_id or left_id == right_id:
        return None
    if not _temporal_overlap(left, right, as_of):
        return None

    left_text, right_text = _text(left), _text(right)
    left_key, right_key = _subject_key(left), _subject_key(right)
    if left_key and right_key and left_key != right_key:
        return None

    left_tokens = set(re.findall(r"[a-z0-9_.-]+", left_text))
    right_tokens = set(re.findall(r"[a-z0-9_.-]+", right_text))
    overlap = len(left_tokens & right_tokens) / max(1, len(left_tokens | right_tokens))
    polarity_left, polarity_right = _polarity(left_text), _polarity(right_text)

    reasons: list[str] = []
    score = 0.0
    if overlap >= 0.55:
        score += 0.55
        reasons.append("high lexical subject overlap")
    if polarity_left * polarity_right < 0:
        score += 0.4
        reasons.append("opposing assertion polarity")
    if left.get("technology") and left.get("technology") == right.get("technology"):
        score += 0.05
        reasons.append("same technology")

    if score < 0.85:
        return None
    return SemanticConflict(left_id, right_id, round(min(score, 1.0), 3), tuple(reasons))
