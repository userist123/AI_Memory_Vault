import pytest
import uuid
from cognitive_core.learning import LearningEngine, ContinualLearningGuard
from cognitive_core.tool_router import ToolRouter
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal

def test_continual_learning_guard_anchor_verification():
    guard = ContinualLearningGuard()
    anchor1 = {"id": "core-rule-1", "content": "Source of Truth Hierarchy", "verification": "verified"}
    anchor2 = {"id": "core-rule-2", "content": "Memory Protocol", "verification": "verified"}

    guard.register_anchor_node(anchor1)
    guard.register_anchor_node(anchor2)

    # Clean state
    ok, violations = guard.verify_no_catastrophic_regression([anchor1, anchor2])
    assert ok is True
    assert len(violations) == 0

    # Missing anchor state (simulating forgetting/corruption)
    ok_bad, violations_bad = guard.verify_no_catastrophic_regression([anchor1])
    assert ok_bad is False
    assert len(violations_bad) == 1
    assert "core-rule-2" in violations_bad[0]

def test_learning_engine_promotes_to_very_high_with_execution_evidence():
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    engine = LearningEngine(controller, router)

    note_id = str(uuid.uuid4())
    relations = [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(9)]
    node = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "testing",
        "tags": ["execution"],
        "created": "2026-08-14",
        "updated": "2026-08-14",
        "provenance": {"source_type": "execution", "source_ref": "pytest-suite"},
        "confidence": "high",
        "verification": "partially_verified",
        "relations": relations,
        "content": "Execution verified automated testing procedure"
    }

    storage.set(note_id, node)
    promoted = engine.promote_memories(Principal.AI_AGENT)

    assert note_id in promoted
    updated = storage.get(note_id)
    assert updated["confidence"] == "very_high"
    assert updated["verification"] == "partially_verified"
