import os
import tempfile
import pytest
from memory_controller.controller import controller as global_controller
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from cognitive_core.executive import Executive

@pytest.fixture
def setup_notes():
    # Clean storage
    global_controller.storage.store = {}
    # Create ACTIVE note A with relation to B
    note_a = {
        "id": "A",
        "type": "knowledge",
        "lifecycle": Lifecycle.ACTIVE.value,
        "confidence": "high",
        "verification": "verified",
        "provenance": {"source_type": "user"},
        "content": "Content A",
        "relations": [{"target_id": "B"}]
    }
    global_controller.storage.set("A", note_a)
    # Create REVIEW note B
    note_b = {
        "id": "B",
        "type": "knowledge",
        "lifecycle": Lifecycle.REVIEW.value,
        "confidence": "high",
        "verification": "unverified",
        "provenance": {"source_type": "user"},
        "content": "Content B",
        "relations": []
    }
    global_controller.storage.set("B", note_b)
    return note_a, note_b

def test_end_to_end_workflow(setup_notes):
    note_a, note_b = setup_notes
    with tempfile.TemporaryDirectory() as tmp_dir:
        # Initialize Executive with checkpoint directory
        exec1 = Executive(global_controller, checkpoint_dir=tmp_dir)
        # Process a normal intent that should succeed
        result = exec1.process_intent(Principal.AI_AGENT, "find A")
        assert result["status"] == "success"
        # Working memory should contain both A and B (B is REVIEW and reachable via cognitive_read)
        wm_ids_pre = set(exec1.working_memory.buffer.keys())
        assert "A" in wm_ids_pre
        assert "B" in wm_ids_pre
        # Verify B is flagged as unverified in WM
        b_entry = exec1.working_memory.buffer.get("B")
        assert b_entry is not None
        assert b_entry["node"].get("_cognitive_unverified") is True
        # Check checkpoint files exist
        assert os.path.exists(os.path.join(tmp_dir, "wm.json"))
        assert os.path.exists(os.path.join(tmp_dir, "plan.json"))
        # Simulate a blocked action to generate a reflection lesson (REVIEW)
        blocked_res = exec1.process_intent(Principal.ADMIN, "delete_canonical")
        assert blocked_res["status"] == "blocked"
        assert "reflection_memory_generated" in blocked_res
        lesson_id = blocked_res["reflection_memory_generated"]
        lesson = global_controller.storage.get(lesson_id)
        assert lesson is not None
        assert lesson["type"] == "lesson"
        assert lesson["lifecycle"] == Lifecycle.REVIEW.value
        # The lesson should be retrievable via cognitive_read (eligible for Cognitive Core)
        pack = global_controller.cognitive_read(Principal.AI_AGENT, lesson_id)
        results = pack.get("results", [])
        assert any(r["id"] == lesson_id for r in results)
        # Load a new Executive from checkpoint and ensure state is restored
        exec2 = Executive(global_controller)
        exec2.load_state(tmp_dir, Principal.AI_AGENT)
        # WM should contain the same nodes as before reflection (checkpoint reflects pre-reflection state)
        restored_ids = set(exec2.working_memory.buffer.keys())
        assert "A" in restored_ids
        assert "B" in restored_ids
        # Active plan should be at step 1 (since first step was completed)
        assert exec2.active_plan is not None
        assert exec2.active_plan.current_step_index == 0
        # Continue executing the remaining step
        step_res = exec2.step_loop(Principal.AI_AGENT)
        assert step_res["status"] == "blocked"
        # After completing plan, executive should be idle
        idle_res = exec2.step_loop(Principal.AI_AGENT)
        assert idle_res["status"] == "blocked"
