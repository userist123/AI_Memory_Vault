import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from cognitive_core.semantic import DeterministicSemanticProvider
from cognitive_core.working_memory import WorkingMemory
from cognitive_core.recall import RecallEngine

def test_deterministic_semantic_provider():
    provider = DeterministicSemanticProvider()
    
    score1 = provider.compute_similarity("hello world", "world hello")
    assert score1 == 1.0
    
    score2 = provider.compute_similarity("hello world", "goodbye moon")
    assert score2 == 0.0
    
    score3 = provider.compute_similarity("hello beautiful world", "hello world")
    assert score3 == 2/3

def test_recall_engine_scoring():
    mock_controller = MagicMock()
    provider = DeterministicSemanticProvider()
    engine = RecallEngine(mock_controller, provider)
    
    wm = WorkingMemory(capacity=5)
    wm.admit([({"id": "wm1", "content": "docker container"}, 1.0)])
    
    # WIRE-9: Use (node, activation) tuples instead of _temp_activation
    activated_nodes = [
        ({"id": "node1", "content": "docker kubernetes", "confidence": "high"}, 1.0),
        ({"id": "node2", "content": "kubernetes helm", "confidence": "low"}, 0.5),
    ]
    
    query = "kubernetes"
    
    results = engine.recall(Principal.AI_AGENT, query, activated_nodes, wm)
    
    assert len(results) == 2
    assert results[0][0]["id"] == "node1"
    assert results[1][0]["id"] == "node2"
    assert results[0][1] > results[1][1]
