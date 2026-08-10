import pytest
from typing import Dict, Any
import uuid

from memory_controller.controller import controller as global_controller
from memory_controller.core import Lifecycle
from memory_controller.authorizer import Principal
from cognitive_core.synapse import Synapse, SynapticGraph
from cognitive_core.activation import ActivationEngine

# --- FIXTURES ---

@pytest.fixture
def clean_memory():
    """Ensure the global controller's storage is clean before each test."""
    # Wipe the in-memory store
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
        # Force insert directly into storage to bypass propose validation for quick setup
        global_controller.storage.set(note_id, note)
        return note_id
        
    yield _create_note
    global_controller.storage.store = {}

# --- TESTS ---

def test_synaptic_graph_extraction():
    note = {
        "id": "node-1",
        "relations": [
            {"target_id": "node-2", "type": "related_to"},
            {"target_id": "node-3", "type": "supports"}
        ]
    }
    
    synapses = SynapticGraph.extract_synapses(note)
    assert len(synapses) == 2
    
    # Sort for deterministic check
    synapses = sorted(synapses, key=lambda x: x.target_id)
    assert synapses[0].source_id == "node-1"
    assert synapses[0].target_id == "node-2"
    assert synapses[0].relation_type == "related_to"
    
    assert synapses[1].target_id == "node-3"
    assert synapses[1].relation_type == "supports"

def test_activation_direct_traversal(clean_memory):
    """Test 1 hop traversal from node A to node B"""
    clean_memory("A", relations=[{"target_id": "B"}])
    clean_memory("B")
    
    engine = ActivationEngine(global_controller)
    activated = engine.activate_from_ids(Principal.ADMIN, ["A"])
    
    assert len(activated) == 2
    nodes = [node.get("id") for node, score in activated]
    assert "A" in nodes
    assert "B" in nodes
    
    # A should have higher activation than B
    a_score = next(score for node, score in activated if node["id"] == "A")
    b_score = next(score for node, score in activated if node["id"] == "B")
    assert a_score > b_score

def test_activation_cycle_detection(clean_memory):
    """Test A -> B -> A cycle"""
    clean_memory("A", relations=[{"target_id": "B"}])
    clean_memory("B", relations=[{"target_id": "A"}])
    
    engine = ActivationEngine(global_controller)
    activated = engine.activate_from_ids(Principal.ADMIN, ["A"])
    
    # Should only activate A and B, not loop infinitely
    assert len(activated) == 2

def test_activation_depth_limit(clean_memory):
    """Test A -> B -> C -> D, depth limit 2"""
    clean_memory("A", relations=[{"target_id": "B"}])
    clean_memory("B", relations=[{"target_id": "C"}])
    clean_memory("C", relations=[{"target_id": "D"}])
    clean_memory("D")
    
    # depth=0 is A, depth=1 is B, depth=2 is C. D should not be activated if max_depth=2
    engine = ActivationEngine(global_controller, max_depth=2)
    activated = engine.activate_from_ids(Principal.ADMIN, ["A"])
    
    nodes = [node.get("id") for node, score in activated]
    assert "A" in nodes
    assert "B" in nodes
    assert "C" in nodes
    assert "D" not in nodes

def test_activation_node_limit(clean_memory):
    """Test context economy node limit"""
    relations = [{"target_id": f"N{i}"} for i in range(1, 10)]
    clean_memory("A", relations=relations)
    for i in range(1, 10):
        clean_memory(f"N{i}")
        
    engine = ActivationEngine(global_controller, max_nodes=5)
    activated = engine.activate_from_ids(Principal.ADMIN, ["A"])
    
    # Total nodes should be strictly 5 (A + 4 neighbors)
    assert len(activated) == 5

def test_activation_lifecycle_isolation(clean_memory):
    """Test that cognitive_read returns REVIEW nodes (tagged as unverified)
    but still blocks RAW/ARCHIVED/etc."""
    clean_memory("A", relations=[{"target_id": "B"}, {"target_id": "C"}])
    clean_memory("B", lifecycle=Lifecycle.REVIEW)
    # C is RAW — should NOT be reachable
    global_controller.storage.set("C", {
        "id": "C", "type": "knowledge", "lifecycle": Lifecycle.RAW.value,
        "confidence": "high", "verification": "verified",
        "provenance": {"source_type": "user"}, "content": "raw", "relations": []
    })
    
    engine = ActivationEngine(global_controller)
    
    # AI_AGENT should get A + B (REVIEW is now eligible via cognitive_read), but NOT C (RAW)
    activated_ai = engine.activate_from_ids(Principal.AI_AGENT, ["A"])
    activated_ids = [n[0].get("id") for n in activated_ai]
    assert "A" in activated_ids
    assert "B" in activated_ids
    assert "C" not in activated_ids
    
    # B should be tagged as cognitively unverified
    b_node = [n[0] for n in activated_ai if n[0].get("id") == "B"][0]
    assert b_node.get("_cognitive_unverified") is True
    
    # ADMIN gets the same behavior
    activated_admin = engine.activate_from_ids(Principal.ADMIN, ["A"])
    admin_ids = [n[0].get("id") for n in activated_admin]
    assert "A" in admin_ids
    assert "B" in admin_ids
    assert "C" not in admin_ids

def test_activation_missing_target(clean_memory):
    """Test resilience to missing targets in relations"""
    clean_memory("A", relations=[{"target_id": "MISSING"}])
    
    engine = ActivationEngine(global_controller)
    activated = engine.activate_from_ids(Principal.ADMIN, ["A"])
    
    # Should just gracefully skip missing
    assert len(activated) == 1
    assert activated[0][0].get("id") == "A"
