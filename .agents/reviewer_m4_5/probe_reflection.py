import sys
import os
import uuid
sys.path.insert(0, os.path.abspath("."))

from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from cognitive_core.reflection import FormalReflexion, SelfRefine, ReflectionPipeline

storage = StorageEngine()
controller = MemoryController(storage)
pipeline = ReflectionPipeline(controller)

# Probe 1: FormalReflexion format check
formatted = FormalReflexion.format_reflection(
    error="Err1",
    root_cause="Cause1",
    fix="Fix1",
    verification="Verif1",
    prevention="Prev1",
    lesson="Lesson1"
)
assert "## Formal Reflexion Analysis" in formatted
assert "- **Error**: Err1" in formatted
assert "- **Root Cause**: Cause1" in formatted
assert "- **Fix Applied**: Fix1" in formatted
assert "- **Verification**: Verif1" in formatted
assert "- **Prevention Rule**: Prev1" in formatted
assert "- **Core Lesson**: Lesson1" in formatted
print("Probe 1 (FormalReflexion 6-stage format): PASSED")

# Probe 2: SelfRefine fuzzing
assert SelfRefine.refine_memory(None)[0] is False
assert SelfRefine.refine_memory(123)[0] is False
assert SelfRefine.refine_memory([])[0] is False
assert SelfRefine.refine_memory({})[0] is False
assert SelfRefine.refine_memory({"content": ""})[0] is False
assert SelfRefine.refine_memory({"content": "   "})[0] is False
assert SelfRefine.refine_memory({"content": "short"})[0] is False
assert SelfRefine.refine_memory({"content": None})[0] is False
assert SelfRefine.refine_memory({"content": ["invalid"]})[0] is False

ok, ref = SelfRefine.refine_memory({"content": "This is a detailed and well-structured note content."})
assert ok is True
assert ref["confidence"] == "medium"
print("Probe 2 (SelfRefine safety & validation): PASSED")

# Probe 3: Synapse Canonical Schema Validation with fully compliant notes
u1 = str(uuid.uuid4())
u2 = str(uuid.uuid4())
storage.set(u1, {
    "id": u1,
    "type": "knowledge",
    "lifecycle": "ACTIVE",
    "category": "database",
    "tags": ["sql"],
    "created": "2026-08-15",
    "updated": "2026-08-15",
    "provenance": {"source_type": "user", "source_ref": "test"},
    "confidence": "high",
    "verification": "verified",
    "content": "Source node content",
    "relations": []
})
storage.set(u2, {
    "id": u2,
    "type": "procedure",
    "lifecycle": "ACTIVE",
    "category": "database",
    "tags": ["sql"],
    "created": "2026-08-15",
    "updated": "2026-08-15",
    "provenance": {"source_type": "user", "source_ref": "test"},
    "confidence": "high",
    "verification": "verified",
    "content": "Target procedure content",
    "relations": []
})

res = pipeline.propose_synapse(Principal.AI_AGENT, u1, u2, "implements")
assert res == u1

src_note = storage.get(u1)
assert len(src_note["relations"]) == 1
rel = src_note["relations"][0]
assert rel == {
    "relation": "implements",
    "target": "procedure",
    "target_id": u2
}
assert "type" not in rel
assert "confidence" not in rel
print("Probe 3 (propose_synapse canonical schema): PASSED")

# Probe 4: Evaluate outcome & review lifecycle invariant
err_res = {"status": "error", "error": "Disk full", "root_cause": "IO capacity reached"}
nid = pipeline.evaluate_outcome(Principal.AI_AGENT, {"query": "save"}, {"action": "write"}, err_res)
assert nid is not None
refl_note = storage.get(nid)
assert refl_note["lifecycle"] == "REVIEW"
assert refl_note["verification"] == "unverified"
assert refl_note["provenance"]["source_type"] == "inference"
print("Probe 4 (Reflection lifecycle & provenance invariant): PASSED")

print("ALL REFLECTION ADVERSARIAL PROBES PASSED")
