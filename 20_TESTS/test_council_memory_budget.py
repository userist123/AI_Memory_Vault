import pytest
from cognitive_core.executive import Executive
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal


def _make_note(note_id, verification="verified"):
    return {
        "id": note_id,
        "type": "lesson",
        "lifecycle": "ACTIVE",
        "content": f"Content for {note_id}",
        "category": "test",
        "tags": [],
        "created": "2026-08-30",
        "updated": "2026-08-30",
        "provenance": {"source_type": "inference", "source_ref": "test"},
        "confidence": "high",
        "verification": verification,
        "relations": [],
    }


def test_executive_exposes_confirmed_memory_budget_constant():
    # Regression test for the reviewer-raised mismatch: WorkingMemory(capacity=10)
    # vs Council's max_memory_results (confirmed real value from
    # 99_SYSTEM/Council_Context_Budget.md: `max_memory_results: 5`).
    assert Executive.MAX_COUNCIL_MEMORY_RESULTS == 5


def test_council_dispatch_never_exceeds_confirmed_memory_budget():
    storage = StorageEngine()
    for i in range(10):
        storage.set(f"n{i}", _make_note(f"n{i}"))
    controller = MemoryController(storage)
    executive = Executive(controller)

    result = executive.process_intent(Principal.AI_AGENT, "delete the old note")

    assert result["dispatch_report"]["total_context_used"] <= Executive.MAX_COUNCIL_MEMORY_RESULTS
