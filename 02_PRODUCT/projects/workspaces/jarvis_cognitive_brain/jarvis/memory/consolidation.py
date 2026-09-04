"""
Memory Consolidation and Plastic Memory Reconsolidation Engine.
"""

import uuid
from datetime import datetime, timezone
from typing import List, Dict, Any, Optional

from jarvis.memory.invariants import Principal, Lifecycle, NoteType
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.reflection import SelfRefine


class ConsolidationEngine:
    """Consolidates recurring lessons and manages plastic memory reconsolidation."""

    def __init__(self, storage_engine: SQLiteStorageEngine):
        self.storage = storage_engine

    def challenge(
        self,
        note_id: str,
        conflicting_evidence: Dict[str, Any],
        principal: Optional[Principal] = None,
    ) -> Optional[Dict[str, Any]]:
        """
        Challenge a canonical note when conflicting evidence is encountered.
        Transitions the note from ACTIVE/VERIFIED to RECONSOLIDATING, preserving previous version.
        """
        note = self.storage.get(note_id)
        if not note:
            return None

        curr_lifecycle = note.get("lifecycle")
        if curr_lifecycle not in [Lifecycle.ACTIVE.value, Lifecycle.VERIFIED.value]:
            return None

        previous_version = {
            "content": note.get("content"),
            "timestamp": note.get("updated", datetime.now(timezone.utc).isoformat()),
            "lifecycle": curr_lifecycle,
        }

        note["previous_version"] = previous_version
        note["lifecycle"] = Lifecycle.RECONSOLIDATING.value
        note["conflicting_evidence"] = conflicting_evidence
        note["updated"] = datetime.now(timezone.utc).strftime("%Y-%m-%d")

        self.storage.set_note_atomic(note)
        return note

    def resolve_challenge(
        self,
        note_id: str,
        resolved_node: Optional[Dict[str, Any]] = None,
        principal: Optional[Principal] = None,
    ) -> Dict[str, Any]:
        """
        Resolves a reconsolidation challenge.
        Restores node to ACTIVE if resolved, or leaves in REVIEW if unresolved.
        """
        note = self.storage.get(note_id)
        if not note:
            raise ValueError(f"Note with ID '{note_id}' not found.")

        if note.get("lifecycle") != Lifecycle.RECONSOLIDATING.value:
            return note

        if resolved_node:
            note["content"] = resolved_node.get("content", note.get("content"))
            note["relations"] = resolved_node.get("relations", note.get("relations", []))
            note["lifecycle"] = Lifecycle.ACTIVE.value
            note["conflicting_evidence"] = None
        else:
            note["lifecycle"] = Lifecycle.REVIEW.value

        note["updated"] = datetime.now(timezone.utc).strftime("%Y-%m-%d")
        self.storage.set_note_atomic(note)
        return note

    def consolidate_lessons(self, principal: Principal = Principal.AI_AGENT) -> Optional[str]:
        """
        Scan REVIEW lessons/errors and consolidate 2+ related items into a unified knowledge note.
        """
        candidates = self.storage.query(lifecycle=Lifecycle.REVIEW.value, limit=50)
        lessons = [c for c in candidates if c.get("type") in [NoteType.LESSON.value, NoteType.ERROR.value]]

        if len(lessons) < 2:
            return None

        now_str = datetime.now(timezone.utc).strftime("%Y-%m-%d")
        new_id = str(uuid.uuid4())
        source_refs = []
        relations = []
        combined_content_lines = ["## Consolidated Domain Knowledge\n"]

        for item in lessons[:5]:
            item_id = item.get("id", "")
            source_refs.append(item_id)
            snippet = item.get("content", "").replace("\n", " ")[:120]
            combined_content_lines.append(f"- Derived from [[{item_id[:8]}]]: {snippet}...")
            relations.append({
                "relation": "derived_from",
                "target": item.get("type", "lesson"),
                "target_id": item_id,
            })

        combined_content = "\n".join(combined_content_lines)

        consolidated_note = {
            "id": new_id,
            "type": NoteType.KNOWLEDGE.value,
            "lifecycle": Lifecycle.REVIEW.value,
            "category": "consolidated-knowledge",
            "tags": ["consolidated", "lessons", "distilled"],
            "created": now_str,
            "updated": now_str,
            "provenance": {
                "source_type": "inference",
                "source_ref": ", ".join(source_refs[:3]),
            },
            "confidence": "medium",
            "verification": "unverified",
            "content": combined_content,
            "relations": relations,
        }

        passed, refined = SelfRefine.refine_memory(consolidated_note)
        if not passed:
            return None

        self.storage.propose(principal, refined)

        # Archive original lessons
        for item in lessons[:5]:
            try:
                self.storage.archive(
                    principal, item["id"], reason=f"Consolidated into knowledge note {new_id}"
                )
            except Exception:
                pass

        return new_id
