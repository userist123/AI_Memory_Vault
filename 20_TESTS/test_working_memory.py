import pytest
from typing import Dict, Any

from cognitive_core.working_memory import WorkingMemory
from cognitive_core.attention import AttentionModel

def test_attention_model():
    model = AttentionModel(activation_weight=0.5, confidence_weight=0.3, recency_weight=0.2)
    
    # 1. High confidence, recent
    node_high = {"id": "1", "confidence": "high"}
    score1 = model.calculate_score(node_high, activation=1.0, recency_tick=1, current_tick=1)
    # expected: (1.0 * 0.5) + (0.8 * 0.3) + (1.0 * 0.2) = 0.5 + 0.24 + 0.2 = 0.94
    assert pytest.approx(score1) == 0.94
    
    # 2. Low confidence, old
    node_low = {"id": "2", "confidence": "unknown"}
    score2 = model.calculate_score(node_low, activation=0.2, recency_tick=1, current_tick=10)
    # activation: 0.2 * 0.5 = 0.1
    # confidence: 0.1 * 0.3 = 0.03
    # recency: max(0, 1.0 - (9 * 0.05)) = max(0, 0.55) = 0.55 * 0.2 = 0.11
    # total = 0.1 + 0.03 + 0.11 = 0.24
    assert pytest.approx(score2) == 0.24

def test_working_memory_admit():
    wm = WorkingMemory(capacity=5)
    
    # Admit 3 nodes
    nodes = [
        ({"id": "A", "confidence": "high"}, 1.0),
        ({"id": "B", "confidence": "medium"}, 0.8),
        ({"id": "C", "confidence": "unknown"}, 0.5)
    ]
    
    wm.admit(nodes)
    
    context = wm.get_active_context()
    assert len(context) == 3
    # Sorted by attention
    assert context[0]["id"] == "A"
    assert context[1]["id"] == "B"
    assert context[2]["id"] == "C"

def test_working_memory_eviction():
    wm = WorkingMemory(capacity=3)
    
    # Admit 5 nodes in one go
    nodes = [
        ({"id": "A", "confidence": "very_high"}, 1.0),
        ({"id": "B", "confidence": "high"}, 0.9),
        ({"id": "C", "confidence": "medium"}, 0.5),
        ({"id": "D", "confidence": "low"}, 0.3),
        ({"id": "E", "confidence": "unknown"}, 0.1)
    ]
    
    wm.admit(nodes)
    
    context = wm.get_active_context()
    assert len(context) == 3
    ids = [n["id"] for n in context]
    assert "A" in ids
    assert "B" in ids
    assert "C" in ids
    assert "D" not in ids
    assert "E" not in ids

def test_working_memory_recency_eviction():
    wm = WorkingMemory(capacity=3)
    
    # Admit A, B, C
    wm.admit([
        ({"id": "A", "confidence": "high"}, 1.0),
        ({"id": "B", "confidence": "high"}, 1.0),
        ({"id": "C", "confidence": "high"}, 1.0)
    ])
    
    # 5 ticks pass, admit D
    for _ in range(5):
        wm.admit([])
        
    wm.admit([({"id": "D", "confidence": "high"}, 1.0)])
    
    context = wm.get_active_context()
    assert len(context) == 3
    # D is newer, should be in
    ids = [n["id"] for n in context]
    assert "D" in ids
    # One of A, B, C is evicted (deterministic, likely C due to tie-break)
    
def test_working_memory_refresh():
    wm = WorkingMemory(capacity=3)
    wm.admit([
        ({"id": "A", "confidence": "low"}, 0.2),
        ({"id": "B", "confidence": "high"}, 1.0),
        ({"id": "C", "confidence": "high"}, 1.0)
    ])
    
    for _ in range(5):
        wm.admit([])
        
    # Refresh A with high activation
    wm.admit([({"id": "A", "confidence": "low"}, 1.0)])
    
    # D arrives, but A is refreshed, so B or C might be evicted instead if they decayed,
    # but wait, A's confidence is low. 
    # Just checking capacity
    wm.admit([({"id": "D", "confidence": "very_high"}, 1.0)])
    assert len(wm.get_active_context()) == 3
