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

def test_self_refine_none_and_non_string_content_safety():
    """Verify that SelfRefine safely handles None, integers, dicts, lists, and non-dicts."""
    from cognitive_core.reflection import SelfRefine

    # None content
    passed, refined = SelfRefine.refine_memory({"content": None})
    assert passed is False
    assert refined == {"content": None}

    # Non-string contents (int, list, dict, bool)
    assert SelfRefine.refine_memory({"content": 12345})[0] is False
    assert SelfRefine.refine_memory({"content": ["item1", "item2"]})[0] is False
    assert SelfRefine.refine_memory({"content": {"key": "val"}})[0] is False
    assert SelfRefine.refine_memory({"content": True})[0] is False

    # Non-dict candidate
    assert SelfRefine.refine_memory(None)[0] is False  # type: ignore
    assert SelfRefine.refine_memory("just a string")[0] is False  # type: ignore

    # Valid string with default confidence
    valid_cand = {"content": "This is a valid structured memory note content."}
    passed_valid, refined_valid = SelfRefine.refine_memory(valid_cand)
    assert passed_valid is True
    assert refined_valid["confidence"] == "medium"
    assert refined_valid["content"] == valid_cand["content"]
