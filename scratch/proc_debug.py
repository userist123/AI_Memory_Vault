import os, json, tempfile, sys
# Ensure project root is in sys.path
PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
sys.path.append(PROJECT_ROOT)

from memory_controller.controller import controller as global_controller
from memory_controller.authorizer import Principal
from cognitive_core.executive import Executive

# Setup notes as in test
global_controller.storage.store = {}
# ACTIVE note A
note_a = {
    "id": "A",
    "type": "knowledge",
    "lifecycle": "ACTIVE",
    "confidence": "high",
    "verification": "verified",
    "provenance": {"source_type": "user"},
    "content": "Content A",
    "relations": [{"target_id": "B"}]
}
global_controller.storage.set("A", note_a)
# REVIEW note B
note_b = {
    "id": "B",
    "type": "knowledge",
    "lifecycle": "REVIEW",
    "confidence": "high",
    "verification": "unverified",
    "provenance": {"source_type": "user"},
    "content": "Content B",
    "relations": []
}
global_controller.storage.set("B", note_b)

with tempfile.TemporaryDirectory() as tmp_dir:
    exec1 = Executive(global_controller, checkpoint_dir=tmp_dir)
    result = exec1.process_intent(Principal.AI_AGENT, "find A")
    print("Intent result", result)
    wm_path = os.path.join(tmp_dir, "wm.json")
    print("WM path", wm_path)
    with open(wm_path, "r", encoding="utf-8") as f:
        data = json.load(f)
    print("Saved WM JSON", json.dumps(data, indent=2))
    print("Buffer keys", list(exec1.working_memory.buffer.keys()))
