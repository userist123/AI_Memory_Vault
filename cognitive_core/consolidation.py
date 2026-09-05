import uuid
import datetime
from typing import List, Dict, Any, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from memory_controller.audit.logger import audit_event
from .tool_router import ToolRouter

class Consolidator:
    """
    BRAIN-10: Memory Consolidation & Reconsolidation Routine.
    Periodically scans ephemeral REVIEW lessons and synthesizes them into concrete knowledge.
    Implements Memory Reconsolidation: allows canonical memories to become volatile (RECONSOLIDATING)
    when challenged by conflicting evidence, preserving lineage history while permitting plastic updates.
    """
    def __init__(self, memory_controller: MemoryController, tool_router: ToolRouter):
        self.controller = memory_controller
        self.router = tool_router

    def challenge(self, note_id: str, conflicting_evidence: Dict[str, Any], principal: Optional[Principal] = None) -> Optional[Dict[str, Any]]:
        """
        Invoked when VerifierAgent or CriticAgent detects conflicting evidence against a canonical memory.
        Transitions the note from CANONICAL/ACTIVE to RECONSOLIDATING, preserving version history.
        """
        caller_principal = principal or Principal.AI_AGENT
        note = self.controller.storage.get(note_id)
        if not note:
            return None
            
        current_lifecycle = note.get("lifecycle")
        if current_lifecycle not in [Lifecycle.ACTIVE.value, Lifecycle.VERIFIED.value, "CANONICAL"]:
            return None
            
        previous_version = {
            "content": note.get("content"),
            "timestamp": note.get("updated", datetime.datetime.now(datetime.timezone.utc).isoformat()),
            "lifecycle": current_lifecycle
        }
        
        note["previous_version"] = previous_version
        note["lifecycle"] = Lifecycle.RECONSOLIDATING.value
        note["conflicting_evidence"] = conflicting_evidence
        note["updated"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        
        self.controller.storage.set(note_id, note)
        
        audit_event(
            operation="reconsolidation_challenge",
            principal=caller_principal,
            target_id=note_id,
            success=True,
            details={
                "previous_lifecycle": current_lifecycle,
                "new_lifecycle": Lifecycle.RECONSOLIDATING.value,
                "conflicting_evidence": conflicting_evidence
            }
        )
        return note

    def resolve_challenge(self, note_id: str, resolved_node: Optional[Dict[str, Any]] = None, principal: Optional[Principal] = None) -> Dict[str, Any]:
        """
        Resolves a reconsolidation challenge after FormalReflexion or SelfRefine critique.
        Returns the node to ACTIVE/CANONICAL if resolved, or REVIEW/VOLATILE if unresolved.
        """
        caller_principal = principal or Principal.AI_AGENT
        note = self.controller.storage.get(note_id)
        if not note:
            raise ValueError(f"Note with ID {note_id} not found")
            
        if note.get("lifecycle") != Lifecycle.RECONSOLIDATING.value:
            return note
            
        if resolved_node:
            note["content"] = resolved_node.get("content", note.get("content"))
            note["relations"] = resolved_node.get("relations", note.get("relations", []))
            note["lifecycle"] = Lifecycle.ACTIVE.value
            note["conflicting_evidence"] = None
        else:
            note["lifecycle"] = Lifecycle.REVIEW.value
            
        note["updated"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        self.controller.storage.set(note_id, note)
        
        audit_event(
            operation="reconsolidation_resolved",
            principal=caller_principal,
            target_id=note_id,
            success=True,
            details={
                "resolved": bool(resolved_node),
                "final_lifecycle": note.get("lifecycle")
            }
        )
        return note

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
