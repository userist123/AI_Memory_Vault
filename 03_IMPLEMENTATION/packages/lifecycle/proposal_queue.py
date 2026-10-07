"""Durable queue for extracted RAW candidates; no automatic canonical promotion.

Approval is an owner attestation, not a label. ``mark(..., "APPROVED", ...)`` only stamps a
candidate ``verified`` when it is given an *owner principal* (a typed ``Principal`` that the
vault's own authorizer allows to ``ATTEST``: HUMAN and ADMIN, never AI_AGENT), an explicit
reviewer name and an explicit evidence reference. A string such as ``"human"`` satisfies
nothing, and there is no default reviewer: the gate in ``QueuePromoter`` cannot be passed by
typing, or defaulting to, the right word.
"""
from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional
import json

from .authorizer import DefaultAuthorizer, Operation, Principal
from .extraction import MemoryCandidate


def owner_principal_values() -> frozenset:
    """Values of the principals the vault allows to ATTEST (the owner attestation matrix)."""
    authorizer = DefaultAuthorizer()
    return frozenset(p.value for p in Principal if authorizer.is_allowed(p, Operation.ATTEST))


def require_owner_principal(approver: Any) -> str:
    """Return the principal value when `approver` is a typed owner principal, else raise.

    The check is on the type, not on spelling: a plain string, a missing principal and an
    AI_AGENT are all refused. ``Principal`` is compared by class name and value because the
    ``cognitive_core`` and ``memory_controller`` import shims each load their own copy of the
    enum, so identity cannot be used.
    """
    if isinstance(approver, str) or type(approver).__name__ != "Principal":
        raise PermissionError(
            "approval requires an owner Principal (HUMAN or ADMIN); a reviewer name is only a label"
        )
    value = str(getattr(approver, "value", "")).lower()
    if value not in owner_principal_values():
        raise PermissionError(f"principal '{value}' is not allowed to attest an approval")
    return value


class MemoryProposalQueue:
    def __init__(self, queue_path: str | Path):
        self.path = Path(queue_path)
        self.path.parent.mkdir(parents=True, exist_ok=True)

    def _load(self) -> List[dict]:
        if not self.path.exists():
            return []
        records = []
        for line in self.path.read_text(encoding="utf-8").splitlines():
            if line.strip():
                records.append(json.loads(line))
        return records

    def _write(self, records: Iterable[dict]) -> None:
        payload = "\n".join(json.dumps(item, ensure_ascii=False, sort_keys=True) for item in records)
        self.path.write_text(payload + ("\n" if payload else ""), encoding="utf-8")

    def enqueue(self, candidates: Iterable[MemoryCandidate]) -> int:
        records = self._load()
        hashes = {item.get("content_hash") for item in records}
        added = 0
        for candidate in candidates:
            if candidate.content_hash in hashes:
                continue
            record = candidate.to_dict()
            record["queue_status"] = "PENDING_REVIEW"
            record["queued_at"] = datetime.now(timezone.utc).isoformat()
            records.append(record)
            hashes.add(candidate.content_hash)
            added += 1
        self._write(records)
        return added

    def pending(self) -> List[dict]:
        return [item for item in self._load() if item.get("queue_status") == "PENDING_REVIEW"]

    def mark(
        self,
        candidate_id: str,
        status: str,
        reviewer: str = "",
        evidence_reference: str = "",
        *,
        approver: Optional[Principal] = None,
    ) -> None:
        """Record a review decision.

        ``APPROVED`` needs ``approver`` (an owner Principal), a ``reviewer`` name and an
        ``evidence_reference``: together they are the attestation that ``QueuePromoter``
        later requires. ``REJECTED`` needs a ``reviewer`` name. ``PROMOTED`` is recorded by
        the promoter. There is no default reviewer for any of them.
        """
        allowed = {"APPROVED", "REJECTED", "PROMOTED"}
        if status not in allowed:
            raise ValueError(f"status must be one of {sorted(allowed)}")
        reviewer = str(reviewer or "").strip()
        if not reviewer:
            raise ValueError("a reviewer is required: there is no default reviewer")
        attested_by = ""
        evidence = str(evidence_reference or "").strip()
        if status == "APPROVED":
            attested_by = require_owner_principal(approver)
            if not evidence:
                raise ValueError("approval requires an explicit evidence_reference")
        records = self._load()
        for item in records:
            if item.get("candidate_id") == candidate_id:
                if item.get("queue_status") == "PROMOTED" and status != "PROMOTED":
                    # A promoted candidate is a note now. Approving it again would make the next
                    # promote_approved() propose the same id a second time and overwrite that note.
                    raise ValueError(f"candidate already promoted: {candidate_id}")
                item["queue_status"] = status
                item["reviewed_by"] = reviewer
                item["reviewed_at"] = datetime.now(timezone.utc).isoformat()
                if status == "APPROVED":
                    item["verification"] = "verified"
                    item["verification_source"] = attested_by
                    item["evidence_reference"] = evidence
                elif status == "REJECTED":
                    # a rejection withdraws any earlier attestation
                    item["verification"] = "unverified"
                    for key in ("verification_source", "evidence_reference"):
                        item.pop(key, None)
                self._write(records)
                return
        raise KeyError(f"candidate not found: {candidate_id}")

    def status(self) -> Dict[str, int]:
        counts: Dict[str, int] = {}
        for item in self._load():
            key = item.get("queue_status", "UNKNOWN")
            counts[key] = counts.get(key, 0) + 1
        return counts
