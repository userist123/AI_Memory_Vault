import copy
import uuid
import datetime
from typing import List, Dict, Any, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal, Operation
from memory_controller.core import Lifecycle
from memory_controller.lifecycle_policy import Mutation as LifecycleMutation, evaluate as evaluate_lifecycle_mutation
from memory_controller.audit.logger import audit_event
from .tool_router import ToolRouter

class Consolidator:
    """
    BRAIN-10: Memory Consolidation & Reconsolidation Routine.
    Periodically scans ephemeral REVIEW lessons and synthesizes them into concrete knowledge.
    Reconsolidation is a controlled lifecycle operation: challenge may be raised by an
    authorized caller, while resolution is privileged and must re-enter REVIEW before
    any subsequent attestation/promotion can return the note to ACTIVE.
    """
    def __init__(self, memory_controller: MemoryController, tool_router: ToolRouter):
        self.controller = memory_controller
        self.router = tool_router

    def challenge(
        self,
        note_id: str,
        conflicting_evidence: Dict[str, Any],
        principal: Principal,
    ) -> Optional[Dict[str, Any]]:
        """Move an ACTIVE/VERIFIED note into RECONSOLIDATING after authorization."""
        try:
            self.controller._check_auth(principal, Operation.RECONSOLIDATE_CHALLENGE)
            note = self.controller.storage.get(note_id)
            if not note:
                return None

            current_lifecycle = note.get("lifecycle")
            allowed_source_states = {
                Lifecycle.ACTIVE.value,
                Lifecycle.VERIFIED.value,
            }
            if current_lifecycle not in allowed_source_states:
                return None
            if not evaluate_lifecycle_mutation(
                current_lifecycle,
                Lifecycle.RECONSOLIDATING.value,
                mutation=LifecycleMutation.RECONSOLIDATE_CHALLENGE,
                verification=note.get("verification"),
            ):
                raise ValueError(
                    f"Invalid lifecycle transition from {current_lifecycle} "
                    f"to {Lifecycle.RECONSOLIDATING.value} for "
                    f"{LifecycleMutation.RECONSOLIDATE_CHALLENGE.value}"
                )

            previous_version = {
                "content": copy.deepcopy(note.get("content")),
                "timestamp": note.get(
                    "updated",
                    datetime.datetime.now(datetime.timezone.utc).isoformat(),
                ),
                "lifecycle": current_lifecycle,
                "verification": note.get("verification"),
            }

            updated_note = copy.deepcopy(note)
            updated_note["previous_version"] = previous_version
            updated_note["lifecycle"] = Lifecycle.RECONSOLIDATING.value
            updated_note["conflicting_evidence"] = copy.deepcopy(conflicting_evidence)
            updated_note["updated"] = datetime.datetime.now(datetime.timezone.utc).isoformat()

            self.controller.storage.set(note_id, updated_note)
            self.controller.cache.invalidate_by_event("memory_updated")
            audit_event(
                operation="reconsolidation_challenge",
                principal=principal,
                target_id=note_id,
                success=True,
                details={
                    "previous_lifecycle": current_lifecycle,
                    "new_lifecycle": Lifecycle.RECONSOLIDATING.value,
                    "conflicting_evidence": conflicting_evidence,
                },
            )
            return updated_note
        except Exception as e:
            audit_event(
                operation="reconsolidation_challenge",
                principal=principal,
                target_id=note_id,
                success=False,
                details={"error": str(e)},
            )
            raise

    def resolve_challenge(
        self,
        note_id: str,
        resolved_node: Optional[Dict[str, Any]],
        principal: Principal,
    ) -> Dict[str, Any]:
        """
        Resolve a reconsolidation challenge without bypassing verification.

        Even when a resolved node is supplied, the resulting note is returned to REVIEW,
        with verification reset to unverified. A later HUMAN/ADMIN attestation followed
        by promotion is required to reach ACTIVE again.
        """
        try:
            self.controller._check_auth(principal, Operation.RECONSOLIDATE_RESOLVE)
            note = self.controller.storage.get(note_id)
            if not note:
                raise ValueError(f"Note with ID {note_id} not found")

            if note.get("lifecycle") != Lifecycle.RECONSOLIDATING.value:
                return note
            if not evaluate_lifecycle_mutation(
                Lifecycle.RECONSOLIDATING.value,
                Lifecycle.REVIEW.value,
                mutation=LifecycleMutation.RECONSOLIDATE_RESOLVE,
                verification=note.get("verification"),
            ):
                raise ValueError(
                    f"Invalid lifecycle transition from {Lifecycle.RECONSOLIDATING.value} "
                    f"to {Lifecycle.REVIEW.value} for "
                    f"{LifecycleMutation.RECONSOLIDATE_RESOLVE.value}"
                )

            updated_note = copy.deepcopy(note)
            if resolved_node:
                if "content" in resolved_node:
                    updated_note["content"] = copy.deepcopy(resolved_node["content"])
                if "relations" in resolved_node:
                    updated_note["relations"] = copy.deepcopy(resolved_node["relations"])

            updated_note["lifecycle"] = Lifecycle.REVIEW.value
            updated_note["verification"] = "unverified"
            updated_note.pop("verification_source", None)
            updated_note.pop("last_verified", None)
            updated_note["conflicting_evidence"] = None
            updated_note["updated"] = datetime.datetime.now(datetime.timezone.utc).isoformat()

            self.controller.storage.set(note_id, updated_note)
            self.controller.cache.invalidate_by_event("memory_updated")
            audit_event(
                operation="reconsolidation_resolved",
                principal=principal,
                target_id=note_id,
                success=True,
                details={
                    "resolved": bool(resolved_node),
                    "final_lifecycle": updated_note.get("lifecycle"),
                    "verification_reset": True,
                },
            )
            return updated_note
        except Exception as e:
            audit_event(
                operation="reconsolidation_resolved",
                principal=principal,
                target_id=note_id,
                success=False,
                details={"error": str(e)},
            )
            raise

    def consolidate_lessons(self, principal: Principal) -> Optional[str]:
        """
        Finds multiple 'lesson' nodes in REVIEW lifecycle and attempts to consolidate them.
        Returns the ID of the new consolidated knowledge node, if any.
        """
        pack = self.controller.search(principal, "lesson", page_size=20)
        results = pack.get("results", [])

        lessons_to_consolidate = []
        for node in results:
            if node.get("type") == "lesson" and node.get("lifecycle") == Lifecycle.REVIEW.value:
                lessons_to_consolidate.append(node)

        if len(lessons_to_consolidate) < 2:
            return None

        combined_content = "Consolidated Knowledge:\n"
        source_refs = []
        relations = []

        for lesson in lessons_to_consolidate:
            combined_content += f"- {lesson.get('content', '')[:100]}...\n"
            source_refs.append(lesson.get("id"))
            rel = {
                "relation": "derived_from",
                "target": lesson.get("type", "lesson")
            }
            lesson_id = lesson.get("id")
            if lesson_id:
                try:
                    uuid.UUID(str(lesson_id))
                    rel["target_id"] = str(lesson_id)
                except (ValueError, TypeError, AttributeError):
                    pass
            relations.append(rel)

        new_id = str(uuid.uuid4())

        consolidated_node = {
            "id": new_id,
            "type": "knowledge",
            "lifecycle": Lifecycle.REVIEW.value,
            "category": "consolidated-knowledge",
            "tags": ["consolidated", "lessons"],
            "confidence": "medium",
            "verification": "unverified",
            "provenance": {
                "source_type": "inference",
                "source_ref": ", ".join(source_refs) if source_refs else "consolidated-lessons"
            },
            "content": combined_content,
            "relations": relations
        }

        from .reflection import SelfRefine
        passed, refined_node = SelfRefine.refine_memory(consolidated_node)
        if not passed:
            return None

        # Propose through ToolRouter
        self.router.execute(principal, "propose", {"note_data": refined_node})

        # Archive old lessons through ToolRouter
        for lesson in lessons_to_consolidate:
            try:
                self.router.execute(principal, "archive", {
                    "note_id": lesson["id"],
                    "reason": "Consolidated into knowledge node"
                })
            except Exception:
                pass

        return new_id
