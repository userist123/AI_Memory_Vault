from cognitive_core.consolidation import Consolidator
from memory_controller.authorizer import Principal
from memory_controller.controller import Lifecycle, MemoryController, StorageEngine


class DummyRouter:
    def execute(self, *args, **kwargs):
        raise AssertionError("router should not be reached by this test")


def _note(lifecycle, verification="unverified"):
    return {
        "id": "note-1",
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "test",
        "tags": [],
        "created": "2026-09-05",
        "updated": "2026-09-05",
        "provenance": {"source_type": "user", "source_ref": "reconsolidation-test"},
        "confidence": "medium",
        "verification": verification,
        "relations": [],
        "content": "test",
    }


def test_challenge_uses_canonical_lifecycle_policy():
    storage = StorageEngine()
    controller = MemoryController(storage)
    note = _note(Lifecycle.ACTIVE.value, "verified")
    storage.set(note["id"], note)
    consolidator = Consolidator(controller, DummyRouter())

    updated = consolidator.challenge(note["id"], {"source": "test"}, Principal.HUMAN)

    assert updated["lifecycle"] == Lifecycle.RECONSOLIDATING.value


def test_resolve_uses_canonical_lifecycle_policy_and_resets_verification():
    storage = StorageEngine()
    controller = MemoryController(storage)
    note = _note(Lifecycle.RECONSOLIDATING.value, "verified")
    storage.set(note["id"], note)
    consolidator = Consolidator(controller, DummyRouter())

    updated = consolidator.resolve_challenge(note["id"], None, Principal.HUMAN)

    assert updated["lifecycle"] == Lifecycle.REVIEW.value
    assert updated["verification"] == "unverified"
