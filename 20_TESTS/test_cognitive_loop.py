import pytest
import os
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from cognitive_core.executive import Executive

# We assume the same global mocked controller from conftest
from memory_controller.controller import controller as global_controller

@pytest.fixture
def clean_memory():
    """Ensure the global controller's storage is clean before each test."""
    os.environ["MEMORY_CONTROLLER_HMAC_SECRET"] = "test_secret_key"
    global_controller.storage.store = {}
    
    def _create_note(note_id: str, relations: list = None, lifecycle=Lifecycle.ACTIVE) -> str:
        relations = relations or []
        note = {
            "id": note_id,
            "type": "knowledge",
            "lifecycle": lifecycle.value if hasattr(lifecycle, 'value') else lifecycle,
            "confidence": "high",
            "verification": "verified",
            "provenance": {"source_type": "user"},
            "content": f"Content for {note_id}",
            "relations": relations
        }
        global_controller.storage.set(note_id, note)
        return note_id
        
    yield _create_note
    global_controller.storage.store = {}

def test_full_cognitive_loop(clean_memory):
    # Setup some basic memories
    clean_memory("A", relations=[{"target_id": "B"}])
    clean_memory("B")
    
    # Initialize the Executive (Prefrontal Cortex)
    executive = Executive(global_controller)
    
    # Trigger a task
    # "migrate memory" triggers a search, which returns nodes, puts them in WM, creates a plan
    result = executive.process_intent(Principal.ADMIN, "find node A")
    
    assert result["status"] == "success", f"Failed with: {result.get('error')}"
    
    # Verify context was populated
    context = result["context"]
    assert len(context) > 0
    
    # Let's trigger a failure to see reflection at work
    # We will simulate a blocked intent
    result_blocked = executive.process_intent(Principal.ADMIN, "delete_canonical")
    assert result_blocked["status"] == "blocked"
    
    # Check if a reflection memory was generated (lesson about autonomy)
    assert "reflection_memory_generated" in result_blocked
    lesson_id = result_blocked["reflection_memory_generated"]
    
    # Retrieve the lesson via storage to verify
    lesson = global_controller.storage.get(lesson_id)
    assert lesson is not None
    assert lesson["type"] == "lesson"
    assert "Autonomy Policy" in lesson["content"]
