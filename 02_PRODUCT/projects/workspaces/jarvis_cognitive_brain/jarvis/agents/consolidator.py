"""
Milestone 3: Consolidator Agent (Lesson Synthesis, Plastic Memory Reconsolidation, Scoped Storage).
"""

import uuid
from datetime import datetime, timezone
from typing import Dict, Any, List, Optional
from jarvis.memory.invariants import (
    Principal,
    Lifecycle,
    NoteType,
)
from jarvis.memory.reflection import SelfRefine
from jarvis.llm.base import BaseLLMProvider, CancellationToken
from jarvis.agents.base import BaseAgent
from jarvis.agents.models import (
    AgentRole,
    ConsolidationCluster,
    ConsolidationSummary,
)


class ConsolidatorAgent(BaseAgent):
    """
    Consolidator Agent synthesizes recurring ephemeral REVIEW lessons into
    unified canonical knowledge notes and manages plastic memory reconsolidation.
    Operates under SEARCH, READ, PROPOSE, ARCHIVE least-privilege permissions.
    """

    role: AgentRole = AgentRole.CONSOLIDATOR

    def __init__(
        self,
        storage: Optional[Any] = None,
        llm: Optional[BaseLLMProvider] = None,
    ):
        super().__init__(storage=storage, llm=llm)

    def scan_and_consolidate(
        self,
        limit: int = 50,
        category: Optional[str] = None,
    ) -> ConsolidationSummary:
        """
        Scan REVIEW lifecycle notes for recurring lessons/errors and distill
        into a unified knowledge note, archiving source lesson notes.
        """
        if not self.storage:
            return ConsolidationSummary(status="no_storage", cluster_count=0)

        # Query candidates in REVIEW lifecycle
        candidates = self.storage.query(lifecycle=Lifecycle.REVIEW.value, category=category, limit=limit)
        lessons = [
            c for c in candidates
            if c.get("type") in [NoteType.LESSON.value, NoteType.ERROR.value, "lesson", "error"]
        ]

        if len(lessons) < 2:
            return ConsolidationSummary(
                consolidated_note_id=None,
                archived_source_ids=[],
                cluster_count=0,
                status="insufficient_candidates",
            )

        now_str = datetime.now(timezone.utc).strftime("%Y-%m-%d")
        new_id = str(uuid.uuid4())
        source_items = lessons[:5]
        source_ids = [item["id"] for item in source_items if "id" in item]

        combined_content_lines = ["## Consolidated Domain Knowledge\n"]
        relations = []

        for item in source_items:
            item_id = item.get("id", "")
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
            "category": category or "consolidated-knowledge",
            "tags": ["consolidated", "lessons", "distilled"],
            "created": now_str,
            "updated": now_str,
            "provenance": {
                "source_type": "inference",
                "source_ref": ", ".join(source_ids[:3]),
            },
            "confidence": "medium",
            "verification": "unverified",
            "content": combined_content,
            "relations": relations,
        }

        # SelfRefine quality validation
        passed, refined = SelfRefine.refine_memory(consolidated_note)
        if not passed:
            refined = consolidated_note

        # Propose unified knowledge note into REVIEW
        self.storage.propose(refined)

        # Archive processed source notes
        archived_ids: List[str] = []
        for item in source_items:
            item_id = item.get("id")
            if item_id:
                try:
                    self.storage.archive(
                        item_id,
                        reason=f"Consolidated into knowledge note {new_id}",
                    )
                    archived_ids.append(item_id)
                except Exception:
                    pass

        return ConsolidationSummary(
            consolidated_note_id=new_id,
            archived_source_ids=archived_ids,
            cluster_count=1,
            status="success",
        )

    def challenge_note(
        self,
        note_id: str,
        conflicting_evidence: Dict[str, Any],
    ) -> Optional[Dict[str, Any]]:
        """
        Challenge an ACTIVE/VERIFIED note with conflicting evidence, transitioning
        it to RECONSOLIDATING and saving previous version rollback state.
        """
        if not self.storage:
            return None

        note = self.storage.get(note_id)
        if not note:
            return None

        curr_lifecycle = note.get("lifecycle")
        if curr_lifecycle not in [Lifecycle.ACTIVE.value, Lifecycle.VERIFIED.value, "ACTIVE", "VERIFIED"]:
            return None

        previous_version = {
            "content": note.get("content"),
            "timestamp": note.get("updated", datetime.now(timezone.utc).isoformat()),
            "lifecycle": curr_lifecycle,
        }

        # Use underlying storage atomic update for RECONSOLIDATING state transition
        updated_note = note.copy()
        updated_note["previous_version"] = previous_version
        updated_note["lifecycle"] = Lifecycle.RECONSOLIDATING.value
        updated_note["conflicting_evidence"] = conflicting_evidence
        updated_note["updated"] = datetime.now(timezone.utc).strftime("%Y-%m-%d")

        if hasattr(self.storage, "underlying_storage"):
            self.storage.underlying_storage.set_note_atomic(updated_note)
        elif hasattr(self.storage, "set_note_atomic"):
            self.storage.set_note_atomic(updated_note)

        return updated_note

    def resolve_challenge(
        self,
        note_id: str,
        resolved_node: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        """
        Resolve a reconsolidation challenge, restoring note to ACTIVE if resolved
        or REVIEW if unresolved.
        """
        if not self.storage:
            raise ValueError("Storage is not configured.")

        note = self.storage.get(note_id)
        if not note:
            raise ValueError(f"Note with ID '{note_id}' not found.")

        if note.get("lifecycle") != Lifecycle.RECONSOLIDATING.value and note.get("lifecycle") != "RECONSOLIDATING":
            return note

        updated_note = note.copy()
        if resolved_node:
            updated_note["content"] = resolved_node.get("content", note.get("content"))
            updated_note["relations"] = resolved_node.get("relations", note.get("relations", []))
            updated_note["lifecycle"] = Lifecycle.ACTIVE.value
            updated_note["conflicting_evidence"] = None
        else:
            updated_note["lifecycle"] = Lifecycle.REVIEW.value

        updated_note["updated"] = datetime.now(timezone.utc).strftime("%Y-%m-%d")

        if hasattr(self.storage, "underlying_storage"):
            self.storage.underlying_storage.set_note_atomic(updated_note)
        elif hasattr(self.storage, "set_note_atomic"):
            self.storage.set_note_atomic(updated_note)

        return updated_note

    async def execute(
        self,
        payload: Dict[str, Any],
        cancellation_token: Optional[CancellationToken] = None,
    ) -> Dict[str, Any]:
        """Execute consolidator tasks (synthesis, challenge, resolve_challenge)."""
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        action = payload.get("action", "scan_and_consolidate")

        if action in ["challenge", "challenge_note"]:
            res = self.challenge_note(
                note_id=payload.get("note_id", ""),
                conflicting_evidence=payload.get("conflicting_evidence", {}),
            )
            return {"status": "challenged", "note": res}

        elif action in ["resolve_challenge", "resolve"]:
            res = self.resolve_challenge(
                note_id=payload.get("note_id", ""),
                resolved_node=payload.get("resolved_node"),
            )
            return {"status": "resolved", "note": res}

        else:
            limit = payload.get("limit", 50)
            category = payload.get("category")
            summary = self.scan_and_consolidate(limit=limit, category=category)
            return {
                "consolidated_id": summary.consolidated_note_id,
                "consolidated_note_id": summary.consolidated_note_id,
                "archived_ids": summary.archived_source_ids,
                "archived_source_ids": summary.archived_source_ids,
                "cluster_count": summary.cluster_count,
                "status": summary.status,
            }
