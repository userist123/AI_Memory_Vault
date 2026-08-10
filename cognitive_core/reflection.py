import uuid
from typing import Dict, Any, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle

class ReflectionPipeline:
    """
    Evaluates outcomes of Executive actions and generates new memories (lessons/errors)
    when expectations do not match reality.
    """
    def __init__(self, memory_controller: MemoryController):
        self.controller = memory_controller

    def evaluate_outcome(self, principal: Principal, intent: Dict[str, Any], action: Dict[str, Any], result: Dict[str, Any]) -> Optional[str]:
        """
        Evaluates the action's result against the intent.
        Returns the ID of a newly proposed memory if learning occurred, else None.
        """
        status = result.get("status")
        
        if status == "error":
            return self._learn_from_error(principal, intent, action, result)
        elif status == "blocked":
            return self._learn_from_blocked(principal, intent, action, result)
        
        # If success, no new memory generated for now.
        return None

    def _learn_from_error(self, principal: Principal, intent: Dict[str, Any], action: Dict[str, Any], result: Dict[str, Any]) -> str:
        """
        Generates an 'error' memory.
        """
        note_id = str(uuid.uuid4())
        error_msg = result.get("error", "Unknown error")
        
        content = (
            f"Error during action: {action.get('action')}\n"
            f"Intent: {intent.get('query')}\n"
            f"Error details: {error_msg}\n"
            "System generated reflection."
        )
        
        note = {
            "id": note_id,
            "type": "error",
            "lifecycle": Lifecycle.REVIEW.value,
            "confidence": "high",
            "verification": "unverified",
            "provenance": {"source_type": "inference"},
            "content": content,
            "relations": []
        }
        
        self.controller.propose(principal, note)
        return note_id

    def _learn_from_blocked(self, principal: Principal, intent: Dict[str, Any], action: Dict[str, Any], result: Dict[str, Any]) -> str:
        """
        Generates a 'lesson' memory about autonomy boundaries.
        """
        note_id = str(uuid.uuid4())
        reason = result.get("reason", "Unknown block reason")
        
        content = (
            f"Action blocked by Autonomy Policy.\n"
            f"Action: {action.get('action')}\n"
            f"Reason: {reason}\n"
            "Lesson: High-risk actions require explicit user approval before execution."
        )
        
        note = {
            "id": note_id,
            "type": "lesson",
            "lifecycle": Lifecycle.REVIEW.value,
            "confidence": "high",
            "verification": "unverified",
            "provenance": {"source_type": "inference"},
            "content": content,
            "relations": []
        }
        
        self.controller.propose(principal, note)
        return note_id

    def propose_synapse(self, principal: Principal, source_id: str, target_id: str, relation_type: str = "related_to") -> Optional[str]:
        """
        BRAIN-11: Dynamic Synapses.
        Injects a 'related_to' edge between two nodes by updating the source node.
        """
        try:
            pack = self.controller.read(principal, source_id)
            results = pack.get("results", [])
            if not results:
                return None
                
            source_node = results[0]
            relations = source_node.get("relations", [])
            if not relations:
                relations = []
                
            # Check if synapse already exists
            for rel in relations:
                if rel.get("target_id") == target_id and rel.get("type") == relation_type:
                    return None
                    
            relations.append({
                "target_id": target_id,
                "type": relation_type,
                "confidence": "unverified"
            })
            source_node["relations"] = relations
            
            # Use propose for now since update requires specific ToolRouter mappings
            # If update is supported natively, it would be self.controller.update(principal, source_node)
            # Assuming MemoryController has update:
            if hasattr(self.controller, "update"):
                self.controller.update(principal, source_id, source_node)
                return source_id
            return None
            
        except Exception:
            return None
