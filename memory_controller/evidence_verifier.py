"""Verification of read-only evidence bundle snapshots."""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
from datetime import datetime, timezone
from typing import Any, Iterable, Mapping

from .evidence_bundle import _hash_note


@dataclass(frozen=True)
class EvidenceVerification:
    bundle_id: str
    valid: bool
    stale_memory_ids: tuple[str, ...]
    missing_memory_ids: tuple[str, ...]
    bundle_hash_matches: bool
    verified_at: str

    def as_dict(self) -> dict[str, Any]:
        return {
            "bundle_id": self.bundle_id,
            "valid": self.valid,
            "stale_memory_ids": list(self.stale_memory_ids),
            "missing_memory_ids": list(self.missing_memory_ids),
            "bundle_hash_matches": self.bundle_hash_matches,
            "verified_at": self.verified_at,
        }


def _bundle_hash(bundle: Mapping[str, Any]) -> str:
    payload = {
        "conflict_case_id": bundle.get("conflict_case_id"),
        "as_of": bundle.get("as_of"),
        "known_as_of": bundle.get("known_as_of"),
        "items": list(bundle.get("items", [])),
    }
    return hashlib.sha256(
        json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()
    ).hexdigest()


def verify_evidence_bundle(bundle: Mapping[str, Any], current_notes: Iterable[Mapping[str, Any]]) -> EvidenceVerification:
    current = {str(note.get("id")): note for note in current_notes if note.get("id")}
    stale: list[str] = []
    missing: list[str] = []
    for item in bundle.get("items", []):
        memory_id = str(item.get("memory_id") or "")
        note = current.get(memory_id)
        if note is None:
            missing.append(memory_id)
            continue
        if _hash_note(note) != item.get("content_hash"):
            stale.append(memory_id)

    bundle_hash_matches = _bundle_hash(bundle) == bundle.get("bundle_hash")
    return EvidenceVerification(
        bundle_id=str(bundle.get("bundle_id") or ""),
        valid=bundle_hash_matches and not stale and not missing,
        stale_memory_ids=tuple(sorted(stale)),
        missing_memory_ids=tuple(sorted(missing)),
        bundle_hash_matches=bundle_hash_matches,
        verified_at=datetime.now(timezone.utc).isoformat(),
    )
