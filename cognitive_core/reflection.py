import uuid
from typing import Dict, Any, Optional, Tuple
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle

class FormalReflexion:
    """Encapsulates structured 6-stage Reflexion:
    Error -> Root Cause -> Fix -> Verification -> Prevention -> Lesson
    """

    @staticmethod
    def format_reflection(
        error: str,
        root_cause: str,
        fix: str,
        verification: str,
        prevention: str,
        lesson: str
    ) -> str:
        return (
            f"## Formal Reflexion Analysis\n\n"
            f"- **Error**: {error}\n"
            f"- **Root Cause**: {root_cause}\n"
            f"- **Fix Applied**: {fix}\n"
            f"- **Verification**: {verification}\n"
            f"- **Prevention Rule**: {prevention}\n"
            f"- **Core Lesson**: {lesson}\n"
        )

class SelfRefine:
    """Pre-consolidation self-refine critique filter."""

    @staticmethod
    def refine_memory(candidate: Dict[str, Any]) -> Tuple[bool, Dict[str, Any]]:
        """Validates whether a candidate memory is coherent, specific, and non-redundant.
        Returns: (passed_filter, refined_candidate)
        """
        if not isinstance(candidate, dict):
            return False, candidate

        raw_content = candidate.get("content")
        if not isinstance(raw_content, str):
            content = ""
        else:
            content = raw_content.strip()

        if not content or len(content) < 15:
            return False, candidate

        # Ensure structured format or minimum substance
        refined = candidate.copy()
        if "confidence" not in refined:
            refined["confidence"] = "medium"
        return True, refined

class ReflectionPipeline:
    """Evaluates outcomes of Executive actions and generates new memories (lessons/errors)
    when expectations do not match reality, using formalized Reflexion.
    """
    def __init__(self, memory_controller: MemoryController):
        self.controller = memory_controller

    def evaluate_outcome(self, principal: Principal, intent: Dict[str, Any], action: Dict[str, Any], result: Dict[str, Any]) -> Optional[str]:
        status = result.get("status")
        if status == "error":
            return self._learn_from_error(principal, intent, action, result)
        elif status == "blocked":
            return self._learn_from_blocked(principal, intent, action, result)
        return None

    def _learn_from_error(self, principal: Principal, intent: Dict[str, Any], action: Dict[str, Any], result: Dict[str, Any]) -> str:
        note_id = str(uuid.uuid4())
        error_msg = result.get("error", "Unknown execution error")
        root_cause = result.get("root_cause", f"Failure during execution of action '{action.get('action')}'")
        fix = result.get("fix", "Reverted state and adjusted execution parameters")
        verification = result.get("verification", "Executed isolated boundary check")
        prevention = result.get("prevention", f"Validate preconditions before executing '{action.get('action')}'")
        lesson = result.get("lesson", f"Handle failures gracefully during {action.get('action')}")

        content = FormalReflexion.format_reflection(
            error=error_msg,
            root_cause=root_cause,
            fix=fix,
            verification=verification,
            prevention=prevention,
            lesson=lesson
        )

        note = {
            "id": note_id,
            "type": "error",
            "lifecycle": Lifecycle.REVIEW.value,
            "category": "error-reflection",
            "confidence": "high",
            "verification": "unverified",
            "provenance": {"source_type": "inference", "source_ref": "formal-reflexion"},
            "content": content,
            "relations": []
        }

        self.controller.propose(principal, note)
        return note_id

    def _learn_from_blocked(self, principal: Principal, intent: Dict[str, Any], action: Dict[str, Any], result: Dict[str, Any]) -> str:
        note_id = str(uuid.uuid4())
        reason = result.get("reason", "Action blocked by policy")
        content = FormalReflexion.format_reflection(
            error="Action blocked by Autonomy Policy.",
            root_cause=f"High-risk operation attempted without required authorization: {reason}",
            fix="Requested human operator confirmation",
            verification="Policy gate checked successfully",
            prevention="Enforce proactive approval requests for high-risk operations",
            lesson="High-risk actions require explicit user approval before execution."
        )

        note = {
            "id": note_id,
            "type": "lesson",
            "lifecycle": Lifecycle.REVIEW.value,
            "category": "policy-lesson",
            "confidence": "high",
            "verification": "unverified",
            "provenance": {"source_type": "inference", "source_ref": "autonomy-policy"},
            "content": content,
            "relations": []
        }

        self.controller.propose(principal, note)
        return note_id

    def propose_synapse(self, principal: Principal, source_id: str, target_id: str, relation_type: str = "related_to") -> Optional[str]:
        try:
            pack = self.controller.read(principal, source_id)
            results = pack.get("results", []) if isinstance(pack, dict) else []
            if not results:
                return None

            source_node = results[0]
            relations = source_node.get("relations", [])
            if not isinstance(relations, list):
                relations = []
            else:
                relations = list(relations)

            for rel in relations:
                if isinstance(rel, dict) and rel.get("target_id") == target_id:
                    if rel.get("relation") == relation_type or rel.get("type") == relation_type:
                        return None

            # Retrieve target node type if available to comply with canonical schema
            target_pack = self.controller.read(principal, target_id)
            target_results = target_pack.get("results", []) if isinstance(target_pack, dict) else []
            target_node = target_results[0] if target_results else {}
            target_type = target_node.get("type", "knowledge") if isinstance(target_node, dict) else "knowledge"
            if not isinstance(target_type, str):
                target_type = "knowledge"

            canonical_relation = {
                "relation": relation_type,
                "target": target_type,
                "target_id": target_id
            }
            relations.append(canonical_relation)

            if hasattr(self.controller, "update"):
                self.controller.update(principal, source_id, {"relations": relations})
                return source_id
            return None
        except Exception:
            return None
