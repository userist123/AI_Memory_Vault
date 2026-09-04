import pytest
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal
from cognitive_core.consolidation import Consolidator
from cognitive_core.tool_router import ToolRouter

@pytest.fixture
def setup_consolidator():
    storage = StorageEngine()
    mc = MemoryController(storage=storage)
    tr = ToolRouter(memory_controller=mc)
    consolidator = Consolidator(memory_controller=mc, tool_router=tr)
    return mc, consolidator, storage

def test_memory_reconsolidation_challenge_and_resolution(setup_consolidator):
    mc, consolidator, storage = setup_consolidator
    
    # Setup canonical active note
    note_id = "canonical_note_123"
    storage.set(note_id, {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": Lifecycle.ACTIVE.value,
        "content": "Original canonical fact",
        "updated": "2026-08-20T00:00:00Z"
    })
    
    # Challenge canonical note with conflicting evidence
    conflicting_evidence = {"claim": "New contradictory finding", "source": "execution_test"}
    challenged_note = consolidator.challenge(note_id, conflicting_evidence, principal=Principal.AI_AGENT)
    
    assert challenged_note is not None
    assert challenged_note["lifecycle"] == Lifecycle.RECONSOLIDATING.value
    assert "previous_version" in challenged_note
    assert challenged_note["previous_version"]["content"] == "Original canonical fact"
    assert challenged_note["conflicting_evidence"] == conflicting_evidence
    
    # Resolve challenge positively
    resolved_data = {"content": "Updated canonical fact with new findings", "relations": []}
    final_note = consolidator.resolve_challenge(note_id, resolved_node=resolved_data, principal=Principal.AI_AGENT)
    
    assert final_note["lifecycle"] == Lifecycle.ACTIVE.value
    assert final_note["content"] == "Updated canonical fact with new findings"
    assert final_note["conflicting_evidence"] is None
