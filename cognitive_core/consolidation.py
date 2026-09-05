import uuid
from typing import List, Dict, Any, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from memory_controller.reconsolidation import ReconsolidationService
from .tool_router import ToolRouter


class Consolidator:
    """BRAIN-10: Memory Consolidation & Reconsolidation Routine.

    Consolidation remains an orchestrator. All reconsolidation mutations are
    delegated to the canonical memory_controller security boundary.
    """

    def __init__(self, memory_controller: MemoryController, tool_router: ToolRouter):
        self.controller = memory_controller
        self.router = tool_router
        self.reconsolidation = ReconsolidationService(memory_controller)

    def challenge(
        self,
        note_id: str,
        conflicting_evidence: Dict[str, Any],
        principal: Optional[Principal] = None,
    ) -> Optional[Dict[str, Any]]:
        """Challenge an ACTIVE/VERIFIED note through the canonical boundary."""
        if principal is None:
            raise PermissionError("Reconsolidation challenge requires an explicit principal")
        return self.reconsolidation.challenge(note_id, conflicting_evidence, principal)

    def resolve_challenge(
        self,
        note_id: str,
        resolved_node: Optional[Dict[str, Any]] = None,
        principal: Optional[Principal] = None,
    ) -> Dict[str, Any]:
        """Resolve a challenge back to REVIEW; re-verification is still required."""
        if principal is None:
            raise PermissionError("Reconsolidation resolution requires an explicit principal")
        return self.reconsolidation.resolve(note_id, resolved_node, principal)

    def consolidate_lessons(self, principal: Principal) -> Optional[str]:
        """
        Finds multiple 'lesson' nodes in REVIEW lifecycle and attempts to consolidate them.
        Returns the ID of the new consolidated knowledge node, if any.
        """
        results = self.controller.query(principal, lifecycles=[Lifecycle.REVIEW], types=["lesson"])
        lessons_to_consolidate = [
            node for node in results
            if node.get("type") == "lesson" and node.get("lifecycle") == Lifecycle.REVIEW.value
        ]

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

        self.router.execute(principal, "propose", {"note_data": refined_node})

        for lesson in lessons_to_consolidate:
            try:
                self.router.execute(principal, "archive", {
                    "note_id": lesson["id"],
                    "reason": "Consolidated into knowledge node"
                })
            except Exception:
                pass

        return new_id
