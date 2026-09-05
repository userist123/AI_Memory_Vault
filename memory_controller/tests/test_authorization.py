import pytest
from memory_controller.controller import MemoryController, Principal, Lifecycle

# Mock storage engine
class MockStorageEngine:
    def __init__(self):
        self.store = {}
    def set(self, note_id, data):
        self.store[note_id] = data
    def get(self, note_id):
        return self.store.get(note_id)
    def delete(self, note_id):
        self.store.pop(note_id, None)
    def query(self, intent=None, lifecycle=None, types=None, max_notes=None):
        notes = list(self.store.values())
        if lifecycle:
            notes = [n for n in notes if n.get('lifecycle') == lifecycle]
        if max_notes:
            notes = notes[:max_notes]
        return notes

@pytest.fixture
def controller():
    storage = MockStorageEngine()
    return MemoryController(storage)

# READ permissions
def test_ai_read_allowed(controller):
    note = {"id": "11111111-1111-1111-1111-111111111111", "lifecycle": Lifecycle.ACTIVE, "content": "data"}
    controller.storage.set("11111111-1111-1111-1111-111111111111", note)
    pack = controller.read(Principal.AI_AGENT, "11111111-1111-1111-1111-111111111111")
    assert pack["results"][0]["id"] == "11111111-1111-1111-1111-111111111111"

def test_human_read_allowed(controller):
    note = {"id": "22222222-2222-2222-2222-222222222222", "lifecycle": Lifecycle.ACTIVE, "content": "data"}
    controller.storage.set("22222222-2222-2222-2222-222222222222", note)
    pack = controller.read(Principal.HUMAN, "22222222-2222-2222-2222-222222222222")
    assert pack["results"][0]["id"] == "22222222-2222-2222-2222-222222222222"

def test_admin_read_allowed(controller):
    note = {"id": "33333333-3333-3333-3333-333333333333", "lifecycle": Lifecycle.ACTIVE, "content": "data"}
    controller.storage.set("33333333-3333-3333-3333-333333333333", note)
    pack = controller.read(Principal.ADMIN, "33333333-3333-3333-3333-333333333333")
    assert pack["results"][0]["id"] == "33333333-3333-3333-3333-333333333333"

# PROPOSE permissions
def test_ai_propose_allowed(controller):
    note = {"id": "44444444-4444-4444-4444-444444444444", "content": "new"}
    note_id = controller.propose(Principal.AI_AGENT, note)
    assert note_id == "44444444-4444-4444-4444-444444444444"

def test_human_propose_allowed(controller):
    note = {"id": "55555555-5555-5555-5555-555555555555", "content": "new"}
    note_id = controller.propose(Principal.HUMAN, note)
    assert note_id == "55555555-5555-5555-5555-555555555555"

def test_admin_propose_allowed(controller):
    note = {"id": "66666666-6666-6666-6666-666666666666", "content": "new"}
    note_id = controller.propose(Principal.ADMIN, note)
    assert note_id == "66666666-6666-6666-6666-666666666666"

# REVIEW permissions
def test_ai_cannot_review(controller):
    note = {"id": "77777777-7777-7777-7777-777777777777", "lifecycle": Lifecycle.REVIEW}
    controller.storage.set("77777777-7777-7777-7777-777777777777", note)
    with pytest.raises(PermissionError):
        controller.review(Principal.AI_AGENT, "77777777-7777-7777-7777-777777777777", decision="approve")

def test_human_review_allowed(controller):
    note = {"id": "88888888-8888-8888-8888-888888888888", "lifecycle": Lifecycle.REVIEW}
    controller.storage.set("88888888-8888-8888-8888-888888888888", note)
    controller.review(Principal.HUMAN, "88888888-8888-8888-8888-888888888888", decision="approve")
    assert controller.storage.get("r2")["review"]["decision"] == "approve"

def test_admin_review_allowed(controller):
    note = {"id": "99999999-9999-9999-9999-999999999999", "lifecycle": Lifecycle.REVIEW}
    controller.storage.set("99999999-9999-9999-9999-999999999999", note)
    controller.review(Principal.ADMIN, "99999999-9999-9999-9999-999999999999", decision="reject")
    assert controller.storage.get("r3")["review"]["decision"] == "reject"

# PROMOTE permissions
def test_ai_cannot_promote(controller):
    note = {"id": "p1", "lifecycle": Lifecycle.REVIEW}
    controller.storage.set("p1", note)
    with pytest.raises(PermissionError):
        controller.promote(Principal.AI_AGENT, "p1")

def test_human_promote_allowed(controller):
    # Lifecycle canon (see 00_GOVERNANCE/coordination/claude-code/ ADR
    # response): promote() requires verification == 'verified'. This test
    # targets PROMOTE authorization specifically (can HUMAN call promote()
    # at all), not the attest() workflow, so the note is seeded as already
    # verified.
    note = {"id": "p2", "lifecycle": Lifecycle.REVIEW, "verification": "verified"}
    controller.storage.set("p2", note)
    controller.promote(Principal.HUMAN, "p2")
    assert controller.storage.get("p2")["lifecycle"] == Lifecycle.ACTIVE

def test_admin_promote_allowed(controller):
    note = {"id": "p3", "lifecycle": Lifecycle.REVIEW, "verification": "verified"}
    controller.storage.set("p3", note)
    controller.promote(Principal.ADMIN, "p3")
    assert controller.storage.get("p3")["lifecycle"] == Lifecycle.ACTIVE

def test_human_promote_rejected_without_verification(controller):
    """Lifecycle canon: REVIEW -> ACTIVE without prior attestation must be
    rejected, even for an authorized HUMAN principal. promote() must never
    silently attest on the caller's behalf."""
    note = {"id": "p2b", "lifecycle": Lifecycle.REVIEW, "verification": "unverified"}
    controller.storage.set("p2b", note)
    with pytest.raises(ValueError, match="Only VERIFIED notes can be promoted"):
        controller.promote(Principal.HUMAN, "p2b")
    assert controller.storage.get("p2b")["lifecycle"] == Lifecycle.REVIEW
