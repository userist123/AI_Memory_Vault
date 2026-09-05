"""Canonical reconsolidation mutation boundary.

This service owns the security and lifecycle checks for reconsolidation so
cognitive_core.Consolidator does not mutate storage directly.
"""

from __future__ import annotations

from copy import deepcopy
from datetime import datetime, timezone
from typing import Any, Dict, Optional

from memory_controller.authorizer import Operation, Principal
from memory_controller.lifecycle_policy import Mutation, is_transition_allowed
from memory_controller.security import check_path_traversal
from memory_controller.audit.logger import audit_event


class ReconsolidationService:
    """Perform authorized reconsolidation challenge/resolve mutations."""

    def __init__(self, controller):
        self.controller = controller

    def challenge(
        self,
        note_id: str,
        conflicting_evidence: Dict[str, Any],
        principal: Principal,
    ) -> Optional[Dict[str, Any]]:
        self.controller._check_auth(principal, Operation.RECONSOLIDATE_CHALLENGE)
        check_path_traversal(note_id)
        note = self.controller.storage.get(note_id)
        if not note:
            return None

        current = note.get("lifecycle")
        if current not in {"ACTIVE", "VERIFIED"}:
            return None
        if not is_transition_allowed(
            current,
            "RECONSOLIDATING",
            mutation=Mutation.RECONSOLIDATE_CHALLENGE,
            verification=note.get("verification"),
        ):
            raise ValueError(f"Cannot reconsolidate note from lifecycle={current!r}")

        previous_version = {
            "content": note.get("content"),
            "timestamp": note.get(
                "updated", datetime.now(timezone.utc).isoformat()
            ),
            "lifecycle": current,
            "verification": note.get("verification"),
        }
        updated = deepcopy(note)
        updated["previous_version"] = previous_version
        updated["lifecycle"] = "RECONSOLIDATING"
        updated["conflicting_evidence"] = deepcopy(conflicting_evidence)
        updated["updated"] = datetime.now(timezone.utc).isoformat()
        self.controller.storage.set(note_id, updated)
        self.controller.cache.invalidate_by_event("memory_updated")
        audit_event(
            operation="reconsolidation_challenge",
            principal=principal,
            target_id=note_id,
            success=True,
            details={"previous_lifecycle": current, "new_lifecycle": "RECONSOLIDATING"},
        )
        return updated

    def resolve(
        self,
        note_id: str,
        resolved_node: Optional[Dict[str, Any]],
        principal: Principal,
    ) -> Dict[str, Any]:
        self.controller._check_auth(principal, Operation.RECONSOLIDATE_RESOLVE)
        check_path_traversal(note_id)
        note = self.controller.storage.get(note_id)
        if not note:
            raise ValueError(f"Note with ID {note_id} not found")
        if note.get("lifecycle") != "RECONSOLIDATING":
            return note
        if not is_transition_allowed(
            "RECONSOLIDATING",
            "REVIEW",
            mutation=Mutation.RECONSOLIDATE_RESOLVE,
            verification=None,
        ):
            raise ValueError("Canonical policy does not permit reconsolidation resolution")

        updated = deepcopy(note)
        if resolved_node:
            if "content" in resolved_node:
                updated["content"] = resolved_node["content"]
            if "relations" in resolved_node:
                updated["relations"] = resolved_node["relations"]
        updated["lifecycle"] = "REVIEW"
        updated["verification"] = "unverified"
        updated["conflicting_evidence"] = None
        updated["updated"] = datetime.now(timezone.utc).isoformat()
        self.controller.storage.set(note_id, updated)
        self.controller.cache.invalidate_by_event("memory_updated")
        audit_event(
            operation="reconsolidation_resolved",
            principal=principal,
            target_id=note_id,
            success=True,
            details={"resolved": bool(resolved_node), "final_lifecycle": "REVIEW"},
        )
        return updated
