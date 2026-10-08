"""Bridges reviewed MemoryProposalQueue entries into MemoryController.propose().

This module never bypasses MemoryController authorization, provenance validation,
or lifecycle rules. It only translates an APPROVED queue record into a propose() call,
and only after an explicit human/admin review step recorded in the queue.
"""
from __future__ import annotations

import uuid
from typing import Any, Dict, List, Optional

from .conflict_detector import ConflictDetector
from .proposal_queue import MemoryProposalQueue, owner_principal_values


class QueuePromoter:
    def __init__(self, queue: MemoryProposalQueue, controller, principal,
                 detector: Optional[ConflictDetector] = None):
        self.queue = queue
        self.controller = controller
        self.principal = principal
        self.detector = detector or ConflictDetector()
        #: candidates the last promote_approved() left APPROVED because they carry no owner
        #: attestation: [{"candidate_id": ..., "reason": ...}]. They are reported, never lost.
        self.skipped: List[Dict[str, str]] = []

    @staticmethod
    def _note_id(record: Dict[str, Any]) -> str:
        """The note id for a candidate: the vault's schema requires a uuid.

        A candidate id already is one (``MemoryCandidate`` mints a uuid4), and is reused so a
        promoted note stays traceable to its queue record. Any other id is mapped to a stable
        uuid5. The old ``candidate-<id>`` form failed schema validation, so no real controller
        ever accepted a promoted candidate.
        """
        candidate_id = str(record.get("candidate_id") or "")
        try:
            return str(uuid.UUID(candidate_id))
        except ValueError:
            return str(uuid.uuid5(uuid.NAMESPACE_URL, f"memory-vault:queue-candidate:{candidate_id}"))

    #: Candidate kinds (providers/extraction.py CANDIDATE_TYPES) that are not note types of the
    #: canonical frontmatter schema; every other kind is already a valid note type.
    _NOTE_TYPE_FOR_KIND = {"fact": "knowledge", "task": "project"}
    #: The provenance keys the canonical schema accepts from a candidate (it forbids the rest,
    #: e.g. the extractor name and digest the queue record carries).
    _PROVENANCE_KEYS = ("source_type", "source_ref")

    @classmethod
    def _note_from_candidate(cls, record: Dict[str, Any]) -> Dict[str, Any]:
        """A schema-valid note for controller.propose(): uuid id, a real note type, a
        provenance object limited to the schema's keys. propose() still decides everything
        else (lifecycle RAW, verification unverified): promotion here is a proposal, not trust."""
        kind = str(record.get("type") or "knowledge")
        provenance = record.get("provenance") if isinstance(record.get("provenance"), dict) else {}
        provenance = {key: str(provenance[key]) for key in cls._PROVENANCE_KEYS if provenance.get(key)}
        provenance.setdefault("source_type", "inference")
        provenance.setdefault("source_ref", "queue")
        tags = [str(tag) for tag in (record.get("tags") or [])]
        if "queue-candidate" not in tags:
            tags.append("queue-candidate")
        return {
            "id": cls._note_id(record),
            "type": cls._NOTE_TYPE_FOR_KIND.get(kind, kind),
            "category": record.get("category", "session"),
            "tags": tags,
            "content": record.get("content", ""),
            "confidence": record.get("confidence", "medium"),
            "provenance": provenance,
        }

    def scan_conflicts(self) -> Dict[str, List[Dict[str, Any]]]:
        """Advisory-only conflict scan over all PENDING_REVIEW candidates."""
        existing_notes = list(self.controller.storage.store.values())
        report: Dict[str, List[Dict[str, Any]]] = {}
        for record in self.queue.pending():
            flags = self.detector.detect(record, existing_notes)
            if flags:
                report[record["candidate_id"]] = flags
        return report

    @staticmethod
    def _attestation_failure(record: Dict[str, Any]) -> Optional[str]:
        """Why an APPROVED record is not an owner attestation, or None when it is."""
        if record.get("verification") != "verified":
            return "approval is not verified"
        if not str(record.get("evidence_reference") or "").strip():
            return "approval has no evidence reference"
        if str(record.get("verification_source") or "").lower() not in owner_principal_values():
            return "approval was not attested by an owner principal"
        return None

    def promote_approved(self) -> List[str]:
        """Call controller.propose() for every attested APPROVED record; mark PROMOTED on success.

        A record whose approval is not an owner attestation (a legacy record, a hand-edited
        one) is not promoted and does not stop the others: it stays APPROVED and is listed in
        ``self.skipped``. When *nothing* could be promoted because of such records, the call
        raises, so a lone unattested approval is still a hard error.
        """
        promoted: List[str] = []
        self.skipped = []
        for record in self.queue._load():
            if record.get("queue_status") != "APPROVED":
                continue
            failure = self._attestation_failure(record)
            if failure:
                self.skipped.append({"candidate_id": str(record.get("candidate_id")), "reason": failure})
                continue
            note = self._note_from_candidate(record)
            new_id = self.controller.propose(self.principal, note)
            self.queue.mark(record["candidate_id"], "PROMOTED", reviewer=self.principal.value)
            promoted.append(new_id)
        if self.skipped and not promoted:
            ids = ", ".join(item["candidate_id"] for item in self.skipped)
            raise ValueError(f"approved candidate lacks verified evidence ({self.skipped[0]['reason']}): {ids}")
        return promoted
