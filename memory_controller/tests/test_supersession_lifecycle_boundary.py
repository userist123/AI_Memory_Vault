import pytest

from memory_controller.authorizer import Principal
from memory_controller.validation.supersession import SupersessionEnforcer


class DictStorage:
    def __init__(self, notes):
        self.notes = notes

    def get(self, note_id):
        return self.notes.get(note_id)


def _note(lifecycle="ACTIVE", verification="unverified", source_type="inference"):
    return {
        "id": "note",
        "lifecycle": lifecycle,
        "verification": verification,
        "provenance": {"source_type": source_type},
        "relations": [],
    }


def test_supersession_requires_active_predecessor():
    storage = DictStorage({"old": _note("REVIEW"), "new": _note("REVIEW")})
    enforcer = SupersessionEnforcer(storage)

    with pytest.raises(ValueError, match="must be ACTIVE for supersession"):
        enforcer.validate_supersession(Principal.HUMAN, "old", "new")


def test_supersession_accepts_active_predecessor_when_other_guards_pass():
    storage = DictStorage({"old": _note("ACTIVE"), "new": _note("REVIEW")})
    enforcer = SupersessionEnforcer(storage)

    enforcer.validate_supersession(Principal.HUMAN, "old", "new")


def test_ai_cannot_supersede_human_verified_active_memory():
    storage = DictStorage({"old": _note("ACTIVE", "verified", "inference"), "new": _note("REVIEW")})
    enforcer = SupersessionEnforcer(storage)

    with pytest.raises(PermissionError, match="Human-verified memory"):
        enforcer.validate_supersession(Principal.AI_AGENT, "old", "new")
