import sys
import os
import pytest
sys.path.insert(0, os.path.abspath("."))

from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal
from cognitive_core.recall import RecallEngine
from cognitive_core.semantic import DeterministicSemanticProvider
from cognitive_core.working_memory import WorkingMemory

storage = StorageEngine()
controller = MemoryController(storage)
semantic = DeterministicSemanticProvider()
engine = RecallEngine(controller, semantic)
wm = WorkingMemory()

# Test 1: Simple 1-hop supersession with 10% freshness boost
storage.set("s1", {
    "id": "s1",
    "lifecycle": "SUPERSEDED",
    "superseded_by": "a1",
    "content": "database replication configuration",
    "confidence": "high",
    "verification": "verified"
})
storage.set("a1", {
    "id": "a1",
    "lifecycle": "ACTIVE",
    "supersedes": "s1",
    "content": "database replication configuration",
    "confidence": "high",
    "verification": "verified"
})

activated = [({"id": "s1", "lifecycle": "SUPERSEDED", "superseded_by": "a1", "content": "database replication configuration", "confidence": "high"}, 0.8)]
res1 = engine.recall(Principal.AI_AGENT, "database replication", activated, wm)
res_map1 = {node["id"]: score for node, score in res1}

assert "s1" in res_map1
assert "a1" in res_map1
# Superseded s1 is penalized by 0.3
# Active a1 receives unpenalized pre-score * 1.1 = (s1_score / 0.3) * 1.1
expected_a1 = (res_map1["s1"] / 0.3) * 1.1
assert abs(res_map1["a1"] - expected_a1) < 1e-5
assert res_map1["a1"] > res_map1["s1"]
print("Test 1 (1-hop supersession freshness boost): PASSED")

# Test 2: Score capping at 1.0 ceiling
storage.set("s2", {
    "id": "s2",
    "lifecycle": "SUPERSEDED",
    "superseded_by": "a2",
    "content": "perfect matching content",
    "confidence": "very_high",
    "verification": "verified"
})
storage.set("a2", {
    "id": "a2",
    "lifecycle": "ACTIVE",
    "supersedes": "s2",
    "content": "perfect matching content",
    "confidence": "very_high",
    "verification": "verified"
})
activated2 = [({"id": "s2", "lifecycle": "SUPERSEDED", "superseded_by": "a2", "content": "perfect matching content", "confidence": "very_high"}, 1.0)]
res2 = engine.recall(Principal.AI_AGENT, "perfect matching content", activated2, wm)
res_map2 = {node["id"]: score for node, score in res2}
assert res_map2["a2"] <= 1.0
print("Test 2 (Score ceiling 1.0): PASSED")

# Test 3: Historical query penalty reduction
res_hist = engine.recall(Principal.AI_AGENT, "historical database replication", activated, wm)
res_hist_map = {node["id"]: score for node, score in res_hist}
# Under historical query, lifecycle factor is 0.8 instead of 0.3
assert res_hist_map["s1"] > res_map1["s1"]
print("Test 3 (Historical query penalty attenuation): PASSED")

print("ALL RECALL ENGINE ADVERSARIAL PROBES PASSED")
