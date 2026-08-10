import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from cognitive_core.reflection import ReflectionPipeline

def test_reflection_evaluates_success():
    mock_controller = MagicMock()
    pipeline = ReflectionPipeline(mock_controller)
    
    intent = {"query": "find node"}
    action = {"action": "search"}
    result = {"status": "success", "result": []}
    
    # Success means no new memory proposed
    note_id = pipeline.evaluate_outcome(Principal.AI_AGENT, intent, action, result)
    assert note_id is None
    mock_controller.propose.assert_not_called()

def test_reflection_evaluates_error():
    mock_controller = MagicMock()
    pipeline = ReflectionPipeline(mock_controller)
    
    intent = {"query": "do something"}
    action = {"action": "unknown_action"}
    result = {"status": "error", "error": "Crash!"}
    
    note_id = pipeline.evaluate_outcome(Principal.AI_AGENT, intent, action, result)
    assert note_id is not None
    mock_controller.propose.assert_called_once()
    
    args, _ = mock_controller.propose.call_args
    assert args[0] == Principal.AI_AGENT
    proposed_note = args[1]
    
    assert proposed_note["id"] == note_id
    assert proposed_note["type"] == "error"
    assert proposed_note["lifecycle"] == "REVIEW"
    assert "Crash!" in proposed_note["content"]

def test_reflection_evaluates_blocked():
    mock_controller = MagicMock()
    pipeline = ReflectionPipeline(mock_controller)
    
    intent = {"query": "delete everything"}
    action = {"action": "delete_canonical"}
    result = {"status": "blocked", "reason": "HIGH RISK"}
    
    note_id = pipeline.evaluate_outcome(Principal.AI_AGENT, intent, action, result)
    assert note_id is not None
    mock_controller.propose.assert_called_once()
    
    args, _ = mock_controller.propose.call_args
    proposed_note = args[1]
    
    assert proposed_note["type"] == "lesson"
    assert "Autonomy Policy" in proposed_note["content"]
