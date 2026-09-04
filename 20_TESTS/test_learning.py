import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from cognitive_core.learning import LearningEngine

def test_learning_engine_promotes_confidence():
    mock_controller = MagicMock()
    mock_router = MagicMock()
    mock_controller.search.return_value = {
        "results": [
            {
                "id": "node-1",
                "type": "knowledge",
                "lifecycle": Lifecycle.ACTIVE.value,
                "confidence": "low",
                "verification": "unverified",
                "relations": [{"target_id": "a"}, {"target_id": "b"}, {"target_id": "c"}]
            }
        ]
    }
    
    engine = LearningEngine(mock_controller, mock_router)
    promoted = engine.promote_memories(Principal.AI_AGENT)
    
    assert len(promoted) == 1
    assert promoted[0] == "node-1"
    
    # Verify update was called through ToolRouter
    mock_router.execute.assert_called_once()
    args = mock_router.execute.call_args[0]
    assert args[1] == "update"
    assert args[2]["note_id"] == "node-1"
    assert args[2]["confidence"] == "medium"

def test_learning_engine_skips_verified():
    mock_controller = MagicMock()
    mock_router = MagicMock()
    mock_controller.search.return_value = {
        "results": [
            {
                "id": "node-1",
                "type": "knowledge",
                "lifecycle": Lifecycle.ACTIVE.value,
                "confidence": "low",
                "verification": "verified",
                "relations": [{"target_id": "a"}, {"target_id": "b"}, {"target_id": "c"}]
            }
        ]
    }
    
    engine = LearningEngine(mock_controller, mock_router)
    promoted = engine.promote_memories(Principal.AI_AGENT)
    
    assert len(promoted) == 0
    mock_router.execute.assert_not_called()
