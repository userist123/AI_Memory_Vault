# supersession.py
"""Supersession enforcer to validate and execute explicit supersession of notes.
"""
from typing import Dict, Any
from memory_controller.authorizer import Principal


class SupersessionEnforcer:
    def __init__(self, storage):
        self.storage = storage

    def validate_supersession(self, principal: Principal, old_id: str, new_id: str) -> None:
        if old_id == new_id:
            raise ValueError("Self-supersession is not allowed")

        old_note = self.storage.get(old_id)
        if not old_note:
            raise ValueError(f"Predecessor note {old_id} does not exist")

        new_note = self.storage.get(new_id)
        if not new_note:
            raise ValueError(f"Successor note {new_id} does not exist")

        # Canonical lifecycle policy permits SUPERSEDE only for ACTIVE notes.
        # Check SUPERSEDED explicitly first so the error remains deterministic
        # if lifecycle validation is ever broadened in a future policy revision.
        if old_note.get("lifecycle") == "SUPERSEDED":
            raise ValueError(f"Predecessor note {old_id} is already SUPERSEDED")
        if old_note.get("lifecycle") != "ACTIVE":
            raise ValueError(
                f"Predecessor note {old_id} must be ACTIVE for supersession "
                f"(current lifecycle={old_note.get('lifecycle')!r})"
            )

        # Invariant: human-verified memory cannot be automatically superseded.
        is_human_verified = (
            old_note.get("verification") == "verified"
            or old_note.get("provenance", {}).get("source_type") == "user"
        )
        if is_human_verified and principal == Principal.AI_AGENT:
            raise PermissionError("Human-verified memory cannot be automatically superseded by an AI Agent")

        # Check for cycles.
        if self._has_cycle(old_id, new_id):
            raise ValueError("Supersession would create a cycle")

    def _has_cycle(self, old_id: str, new_id: str) -> bool:
        def has_path(start: str, target: str, visited: set) -> bool:
            if start == target:
                return True
            if start in visited:
                return False
            visited.add(start)
            note = self.storage.get(start)
            if not note:
                return False

            # Check direct supersedes field.
            pred = note.get("supersedes")
            if pred and has_path(pred, target, visited):
                return True

            # Check relations of type "replaces".
            for rel in note.get("relations", []):
                r_type = rel.get("relation") or rel.get("type")
                if r_type == "replaces":
                    t_id = rel.get("target_id")
                    if t_id and has_path(t_id, target, visited):
                        return True
            return False

        return has_path(old_id, new_id, set())


def resolve_active_lineage(storage, note_id: str, max_depth: int = 50) -> str:
    """Traverse the superseded_by chain until the active successor note is reached."""
    # If storage engine natively implements resolve_active_lineage, use it
    if hasattr(storage, "resolve_active_lineage") and callable(getattr(storage, "resolve_active_lineage")):
        try:
            return storage.resolve_active_lineage(note_id)
        except Exception:
            pass

    current_id = str(note_id)
    visited = set()
    for _ in range(max_depth):
        if current_id in visited:
            break
        visited.add(current_id)
        note = storage.get(current_id)
        if not note:
            break
        successor_id = note.get("superseded_by")
        if not successor_id:
            break
        current_id = str(successor_id)
    return current_id
