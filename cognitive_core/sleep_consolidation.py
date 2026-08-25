"""Sleep-phase consolidation: a read-only, advisory maintenance analyzer.

Never mutates canonical memory. Produces a report a human/admin can act on
through the existing MemoryController API (review/promote/archive/attest).
"""
from __future__ import annotations

from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional
import json
import math

from .conflict_detector import ConflictDetector

_DEFAULT_DORMANT_DAYS = 60
_DEFAULT_STALE_REVIEW_DAYS = 14


def _parse_date(value: Any) -> datetime:
    if not value:
        return datetime.now(timezone.utc)
    try:
        parsed = datetime.fromisoformat(str(value))
        if parsed.tzinfo is None:
            parsed = parsed.replace(tzinfo=timezone.utc)
        return parsed
    except ValueError:
        return datetime.now(timezone.utc)


def _age_days(value: Any, now: datetime) -> float:
    return max((now - _parse_date(value)).total_seconds() / 86400.0, 0.0)


def _heuristic_activation(note: Dict[str, Any], now: datetime, decay: float = 0.5) -> float:
    """Simplified ACT-R-style decay: B ~= -decay * ln(age_days + 1).

    Independent, documented heuristic. Does not call into
    cognitive_core.activation.py and never writes back to notes.
    """
    age = _age_days(note.get("updated") or note.get("created"), now)
    return -decay * math.log(age + 1.0)


@dataclass
class SleepConsolidationReport:
    generated_at: str
    dormant_candidates: List[Dict[str, Any]] = field(default_factory=list)
    stale_review_candidates: List[Dict[str, Any]] = field(default_factory=list)
    conflict_pairs: List[Dict[str, Any]] = field(default_factory=list)
    stats: Dict[str, int] = field(default_factory=dict)

    def to_dict(self) -> dict:
        return asdict(self)


class SleepConsolidator:
    """Advisory maintenance pass over canonical memory. Read-only by design."""

    def __init__(self, controller, dormant_days: int = _DEFAULT_DORMANT_DAYS,
                 stale_review_days: int = _DEFAULT_STALE_REVIEW_DAYS,
                 detector: Optional[ConflictDetector] = None):
        self.controller = controller
        self.dormant_days = dormant_days
        self.stale_review_days = stale_review_days
        self.detector = detector or ConflictDetector()

    def run(self) -> SleepConsolidationReport:
        now = datetime.now(timezone.utc)
        notes = list(self.controller.storage.store.values())

        report = SleepConsolidationReport(generated_at=now.isoformat())

        active_notes = [n for n in notes if n.get("lifecycle") in {"ACTIVE", "VERIFIED"}]
        review_notes = [n for n in notes if n.get("lifecycle") == "REVIEW"]

        for note in active_notes:
            age = _age_days(note.get("updated") or note.get("created"), now)
            if age >= self.dormant_days:
                report.dormant_candidates.append({
                    "id": note.get("id"), "age_days": round(age, 1),
                    "activation": round(_heuristic_activation(note, now), 4),
                })

        for note in review_notes:
            age = _age_days(note.get("updated") or note.get("created"), now)
            if age >= self.stale_review_days:
                report.stale_review_candidates.append({
                    "id": note.get("id"), "age_days": round(age, 1),
                })

        seen_pairs = set()
        for note in active_notes:
            others = [n for n in active_notes if n.get("id") != note.get("id")]
            flags = self.detector.detect(note, others)
            for flag in flags:
                pair_key = tuple(sorted([note.get("id"), flag["note_id"]]))
                if pair_key in seen_pairs:
                    continue
                seen_pairs.add(pair_key)
                report.conflict_pairs.append({
                    "note_a": pair_key[0], "note_b": pair_key[1],
                    "overlap": flag["overlap"], "severity": flag["severity"],
                })

        report.stats = {
            "total_notes": len(notes),
            "active_notes": len(active_notes),
            "review_notes": len(review_notes),
            "dormant_candidates": len(report.dormant_candidates),
            "stale_review_candidates": len(report.stale_review_candidates),
            "conflict_pairs": len(report.conflict_pairs),
        }
        return report

    def save_report(self, output_path) -> Path:
        report = self.run()
        target = Path(output_path)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(report.to_dict(), ensure_ascii=False, indent=2), encoding="utf-8")
        return target
