"""Bridges reviewed MemoryProposalQueue entries into MemoryController.propose().

This module never bypasses MemoryController authorization, provenance validation,
or lifecycle rules. It only translates an APPROVED queue record into a propose() call,
and only after an explicit human/admin review step recorded in the queue.
"""
from __future__ import annotations

from typing import Any, Dict, List, Optional

from .conflict_detector import ConflictDetector
from .proposal_queue import MemoryProposalQueue


class QueuePromoter:
    def __init__(self, queue: MemoryProposalQueue, controller, principal,
                 detector: Optional[ConflictDetector] = None):
        self.queue = queue
        self.controller = controller
        self.principal = principal
        self.detector = detector or ConflictDetector()

    @staticmethod
    def _note_from_candidate(record: Dict[str, Any]) -> Dict[str, Any]:
        return {
            "id": f"candidate-{record.get('candidate_id')}",
            "type": record.get("type", "knowledge"),
            "category": record.get("category", "session"),
            "tags": record.get("tags", []),
            "content": record.get("content", ""),
            "confidence": record.get("confidence", "medium"),
            "provenance": record.get("provenance", {"source_type": "inference", "source_ref": "queue"}),
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

    def promote_approved(self) -> List[str]:
        """Call controller.propose() for every APPROVED record; mark PROMOTED on success."""
        promoted: List[str] = []
        for record in self.queue._load():
            if record.get("queue_status") != "APPROVED":
                continue
            note = self._note_from_candidate(record)
            new_id = self.controller.propose(self.principal, note)
            self.queue.mark(record["candidate_id"], "PROMOTED", reviewer=self.principal.value)
            promoted.append(new_id)
        return promoted
