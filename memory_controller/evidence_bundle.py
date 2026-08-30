"""Read-only evidence bundle construction for review workflows."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
from typing import Any, Iterable, Mapping, Optional


@dataclass(frozen=True)
class EvidenceItem:
    memory_id: str
    content_hash: str
    source_ref: Optional[str]
    source_type: Optional[str]
    extraction_date: Optional[str]
    valid_from: Optional[str]
    valid_until: Optional[str]
    lifecycle: Optional[str]
    verification: Optional[str]

    def as_dict(self) -> dict[str, Any]:
        return {
            "memory_id": self.memory_id,
            "content_hash": self.content_hash,
            "source_ref": self.source_ref,
            "source_type": self.source_type,
            "extraction_date": self.extraction_date,
            "valid_from": self.valid_from,
            "valid_until": self.valid_until,
            "lifecycle": self.lifecycle,
            "verification": self.verification,
        }


def _hash_note(note: Mapping[str, Any]) -> str:
    canonical = json.dumps(dict(note), sort_keys=True, separators=(",", ":"), default=str).encode()
    return hashlib.sha256(canonical).hexdigest()


def build_evidence_bundle(
    notes: Iterable[Mapping[str, Any]],
    *,
    conflict_case_id: Optional[str] = None,
    evidence_ids: Iterable[str] = (),
    as_of: Any = None,
    known_as_of: Any = None,
) -> dict[str, Any]:
    """Build an immutable evidence snapshot; this function never mutates notes."""
    wanted = {str(x) for x in evidence_ids if x}
    selected = [note for note in notes if not wanted or str(note.get("id")) in wanted]
    items: list[EvidenceItem] = []
    for note in selected:
        provenance = note.get("provenance") or {}
        items.append(
            EvidenceItem(
                memory_id=str(note.get("id") or ""),
                content_hash=_hash_note(note),
                source_ref=provenance.get("source_ref"),
                source_type=provenance.get("source_type"),
                extraction_date=provenance.get("extraction_date"),
                valid_from=note.get("valid_from"),
                valid_until=note.get("valid_until"),
                lifecycle=note.get("lifecycle"),
                verification=note.get("verification"),
            )
        )
    bundle_payload = {
        "conflict_case_id": conflict_case_id,
        "as_of": str(as_of) if as_of is not None else None,
        "known_as_of": str(known_as_of) if known_as_of is not None else None,
        "items": [item.as_dict() for item in items],
    }
    bundle_hash = hashlib.sha256(
        json.dumps(bundle_payload, sort_keys=True, separators=(",", ":")).encode()
    ).hexdigest()
    return {
        "bundle_id": f"EB-{bundle_hash[:16]}",
        "bundle_hash": bundle_hash,
        "acquired_at": datetime.now(timezone.utc).isoformat(),
        **bundle_payload,
    }
