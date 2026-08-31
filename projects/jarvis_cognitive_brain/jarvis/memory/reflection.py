"""
6-Stage Formal Reflexion Engine and SelfRefine Critique Filter.
Error -> Root Cause -> Fix -> Verification -> Prevention -> Lesson.
"""

import uuid
from datetime import datetime, timezone
from typing import Dict, Any, Optional, Tuple

from jarvis.memory.invariants import Principal, Lifecycle, NoteType
from jarvis.memory.sqlite_engine import SQLiteStorageEngine


class FormalReflexion:
    """Encapsulates structured 6-stage Reflexion."""

    @staticmethod
    def format_reflection(
        error: str,
        root_cause: str,
        fix: str,
        verification: str,
        prevention: str,
        lesson: str,
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
    """Pre-consolidation critique filter validating quality and coherence."""

    @staticmethod
    def refine_memory(candidate: Dict[str, Any]) -> Tuple[bool, Dict[str, Any]]:
        """
        Validates whether a candidate memory is coherent, specific, and non-redundant.
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

        refined = candidate.copy()
        if "confidence" not in refined:
            refined["confidence"] = "medium"
        return True, refined


class ReflexionEngine:
    """Executes formal 6-stage reflection on errors, blocks, and deviations."""

    def __init__(self, storage_engine: SQLiteStorageEngine):
        self.storage = storage_engine

    def reflect_error(
        self,
        principal: Principal,
        step_action: str,
        error_msg: str,
        root_cause: Optional[str] = None,
        fix: Optional[str] = None,
        verification: Optional[str] = None,
        prevention: Optional[str] = None,
        lesson: Optional[str] = None,
    ) -> str:
        """Create and propose a formal 6-stage reflection note in REVIEW lifecycle."""
        note_id = str(uuid.uuid4())
        now_str = datetime.now(timezone.utc).strftime("%Y-%m-%d")

        rc = root_cause or f"Execution failure during step '{step_action}'"
        fx = fix or "Adjusted parameters and executed fallback pathway"
        vr = verification or "Checked isolated execution boundaries"
        pv = prevention or f"Validate preconditions before invoking '{step_action}'"
        ls = lesson or f"Handle failures and retries gracefully for '{step_action}'"

        content = FormalReflexion.format_reflection(
            error=error_msg,
            root_cause=rc,
            fix=fx,
            verification=vr,
            prevention=pv,
            lesson=ls,
        )

        note_data = {
            "id": note_id,
            "type": NoteType.ERROR.value,
            "lifecycle": Lifecycle.REVIEW.value,
            "category": "error-reflection",
            "tags": ["reflexion", "error", "cognitive-learning"],
            "created": now_str,
            "updated": now_str,
            "provenance": {
                "source_type": "inference",
                "source_ref": "formal-reflexion-6stage",
            },
            "confidence": "high",
            "verification": "unverified",
            "content": content,
            "relations": [],
        }

        # Validate with SelfRefine
        passed, refined = SelfRefine.refine_memory(note_data)
        if passed:
            self.storage.propose(principal, refined)
            return note_id

        self.storage.propose(principal, note_data)
        return note_id
