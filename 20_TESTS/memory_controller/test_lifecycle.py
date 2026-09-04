import pytest
from uuid import uuid4
from datetime import datetime, timezone
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle, Principal, Operation

# Helper to build minimal frontmatter for a note
def make_note(lifecycle: Lifecycle, note_id: str = None):
    note_id = note_id or str(uuid4())
    today = datetime.now(timezone.utc).date().isoformat()
    return {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": lifecycle.value,
        "category": "test",
        "tags": [],
        "created": today,
        "updated": today,
        "provenance": {
            "source_type": "user",
            "source_ref": "unit_test",
        },
        "confidence": "high",
        "verification": "unverified",
        "relations": []
    }

# Fixture for a fresh controller
@pytest.fixture
def controller():
    storage = StorageEngine()
    return MemoryController(storage)

# Valid lifecycle transitions according to canonical protocol
VALID_TRANSITIONS = [
    (Lifecycle.RAW, Lifecycle.CLASSIFIED),
    (Lifecycle.CLASSIFIED, Lifecycle.NORMALIZED),
    (Lifecycle.NORMALIZED, Lifecycle.REVIEW),
    (Lifecycle.REVIEW, Lifecycle.VERIFIED),
    (Lifecycle.VERIFIED, Lifecycle.ACTIVE),
    (Lifecycle.ACTIVE, Lifecycle.SUPERSEDED),
    (Lifecycle.ACTIVE, Lifecycle.ARCHIVED),
(Lifecycle.ACTIVE, Lifecycle.ARCHIVED),
]

@pytest.mark.parametrize("src, dst", VALID_TRANSITIONS)
def test_valid_transition(controller, src, dst):
    # Directly store a note with source lifecycle
    note = make_note(src)
    controller.storage.set(note["id"], note)
    # Transition by updating lifecycle field
    note["lifecycle"] = dst.value
    # Validation should pass for a correct transition
    controller._validate_note(note)  # should not raise
    # Persist the transition
    controller.storage.set(note["id"], note)
    # Verify stored lifecycle matches
    stored = controller.storage.get(note["id"])
    assert stored["lifecycle"] == dst.value

# Invalid transitions (any that are not in the above list)
INVALID_TRANSITIONS = [
    (Lifecycle.RAW, Lifecycle.VERIFIED),
    (Lifecycle.CLASSIFIED, Lifecycle.ACTIVE),
    (Lifecycle.NORMALIZED, Lifecycle.SUPERSEDED),
    (Lifecycle.REVIEW, Lifecycle.ARCHIVED),
    (Lifecycle.VERIFIED, Lifecycle.RAW),
    (Lifecycle.SUPERSEDED, Lifecycle.ARCHIVED),
]

@pytest.mark.parametrize("src, dst", INVALID_TRANSITIONS)
def test_invalid_transition(controller, src, dst):
    note = make_note(src)
    controller.storage.set(note["id"], note)
    note["lifecycle"] = dst.value
    with pytest.raises(Exception):
        controller._validate_note(note)

def test_raw_not_in_read_search(controller):
    raw_note = make_note(Lifecycle.RAW)
    controller.storage.set(raw_note["id"], raw_note)
    # Attempt read as AI (should raise)
    with pytest.raises(ValueError):
        controller.read(Principal.AI_AGENT, raw_note["id"])
    # Search should not return RAW notes
    result = controller.search(Principal.AI_AGENT, "test query")
    ids = [r.get('id') for r in result.get('results', [])]
    assert raw_note["id"] not in ids

def test_verified_not_active_unless_promoted(controller):
    verified = make_note(Lifecycle.VERIFIED)
    controller.storage.set(verified["id"], verified)
    # READ should still reject because only ACTIVE is readable
    with pytest.raises(ValueError):
        controller.read(Principal.AI_AGENT, verified["id"])

def test_ai_cannot_bypass_lifecycle(controller):
    # AI can read ACTIVE notes
    active = make_note(Lifecycle.ACTIVE)
    controller.storage.set(active["id"], active)
    result = controller.read(Principal.AI_AGENT, active["id"])
    assert result is not None
    # Change to RAW and attempt read again – should fail
    active["lifecycle"] = Lifecycle.RAW.value
    controller.storage.set(active["id"], active)
    with pytest.raises(ValueError):
        controller.read(Principal.AI_AGENT, active["id"])
