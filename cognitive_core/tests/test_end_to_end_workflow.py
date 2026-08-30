import os
import tempfile
import pytest
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from cognitive_core.executive import Executive


@pytest.fixture
def setup_notes():
    storage = StorageEngine()
    controller = MemoryController(storage)

    note_a = {
        "id": "A",
        "type": "knowledge",
        "lifecycle": Lifecycle.ACTIVE.value,
        "confidence": "high",
        "verification": "verified",
        "provenance": {"source_type": "user", "source_ref": "e2e"},
        "content": "Content A",
        "relations": [{"target_id": "B"}]
    }
    storage.set("A", note_a)

    note_b = {
        "id": "B",
        "type": "knowledge",
        "lifecycle": Lifecycle.REVIEW.value,
        "confidence": "high",
        "verification": "unverified",
        "provenance": {"source_type": "user", "source_ref": "e2e"},
        "content": "Content B",
        "relations": []
    }
    storage.set("B", note_b)
    return controller, note_a, note_b


def test_end_to_end_workflow(setup_notes):
    controller, note_a, note_b = setup_notes
    with tempfile.TemporaryDirectory() as tmp_dir:
        exec1 = Executive(controller, checkpoint_dir=tmp_dir)
        result = exec1.process_intent(Principal.AI_AGENT, "find A")
        assert result["status"] == "success"

        wm_ids_pre = set(exec1.working_memory.buffer.keys())
        assert "A" in wm_ids_pre
        assert "B" in wm_ids_pre

        b_entry = exec1.working_memory.buffer.get("B")
        assert b_entry is not None
        assert b_entry["node"].get("_cognitive_unverified") is True

        assert os.path.exists(os.path.join(tmp_dir, "wm.json"))
        assert os.path.exists(os.path.join(tmp_dir, "plan.json"))

        blocked_res = exec1.process_intent(Principal.ADMIN, "delete_canonical")
        assert blocked_res["status"] == "blocked"
        assert "reflection_memory_generated" in blocked_res
        lesson_id = blocked_res["reflection_memory_generated"]
        lesson = controller.storage.get(lesson_id)
        assert lesson is not None
        assert lesson["type"] == "lesson"
        assert lesson["lifecycle"] == Lifecycle.REVIEW.value

        pack = controller.cognitive_read(Principal.AI_AGENT, lesson_id)
        results = pack.get("results", [])
        assert any(r["id"] == lesson_id for r in results)

        exec2 = Executive(controller)
        exec2.load_state(tmp_dir, Principal.AI_AGENT)
        restored_ids = set(exec2.working_memory.buffer.keys())
        assert "A" in restored_ids
        assert "B" in restored_ids
        assert exec2.active_plan is not None
