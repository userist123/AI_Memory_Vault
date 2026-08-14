import pytest
import uuid
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal
from cognitive_core.tool_router import ToolRouter, ApprovalRequiredError
from cognitive_core.learning import LearningEngine

def make_test_note(id_val, lifecycle="RAW", verification="unverified", provenance=None, content="test content"):
    if provenance is None:
        provenance = {"source_type": "inference", "source_ref": "tool-router-test"}
    return {
        "id": id_val,
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "security-test",
        "tags": ["test"],
        "created": "2026-08-10",
        "updated": "2026-08-10",
        "provenance": provenance,
        "confidence": "high",
        "verification": verification,
        "relations": [],
        "content": content
    }

# P0-009: ToolRouter propagates security rejection when AI_AGENT proposes verified memory
def test_p0_009_tool_router_blocks_ai_verified_propose():
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    
    note_id = str(uuid.uuid4())
    payload = make_test_note(note_id, verification="verified")
    
    with pytest.raises(ValueError, match="verified"):
        router.execute(Principal.AI_AGENT, "propose", {"note_data": payload})
        
    assert storage.get(note_id) is None

# P0-009b: ToolRouter propagates security rejection when AI_AGENT claims user provenance
def test_p0_009_tool_router_blocks_ai_user_provenance_propose():
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    
    note_id = str(uuid.uuid4())
    payload = make_test_note(note_id, provenance={"source_type": "user", "source_ref": "injected"})
    
    with pytest.raises(ValueError, match="not permitted to claim provenance source_type 'user'"):
        router.execute(Principal.AI_AGENT, "propose", {"note_data": payload})
        
    assert storage.get(note_id) is None

# P0-012: LearningEngine promotes to partially_verified legitimately without breakage
def test_p0_012_learning_engine_partially_verified_promotion():
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    
    note_id = str(uuid.uuid4())
    # Create active note with medium confidence and 6 relations (>= threshold*2)
    relations = [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(6)]
    note = make_test_note(note_id, lifecycle="ACTIVE", verification="unverified")
    note["confidence"] = "medium"
    note["relations"] = relations
    note["provenance"] = {"source_type": "user", "source_ref": "user-base"}
    
    # Store note directly in storage as test fixture
    storage.set(note_id, note)
    
    engine = LearningEngine(controller, router)
    promoted = engine.promote_memories(Principal.AI_AGENT)
    
    assert note_id in promoted
    updated_note = storage.get(note_id)
    assert updated_note["confidence"] == "high"
    assert updated_note["verification"] == "partially_verified"
