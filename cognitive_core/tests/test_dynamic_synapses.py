import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from cognitive_core.reflection import ReflectionPipeline

def test_propose_synapse_success():
    mock_controller = MagicMock()
    # Mock reading the source node
    mock_controller.read.return_value = {
        "results": [{
            "id": "node-A",
            "type": "knowledge",
            "relations": []
        }]
    }
    mock_controller.update = MagicMock()
    
    pipeline = ReflectionPipeline(mock_controller)
    
    result = pipeline.propose_synapse(Principal.AI_AGENT, "node-A", "node-B")
    assert result == "node-A"
    
    # Verify update was called
    mock_controller.update.assert_called_once()
    args, kwargs = mock_controller.update.call_args
    assert args[0] == Principal.AI_AGENT
    assert args[1] == "node-A"
    updated_node = args[2]
    assert len(updated_node["relations"]) == 1
    assert updated_node["relations"][0]["target_id"] == "node-B"
    assert updated_node["relations"][0]["type"] == "related_to"

def test_propose_synapse_duplicate():
    mock_controller = MagicMock()
    # Mock reading the source node with existing relation
    mock_controller.read.return_value = {
        "results": [{
            "id": "node-A",
            "type": "knowledge",
            "relations": [{
                "target_id": "node-B",
                "type": "related_to",
                "confidence": "high"
            }]
        }]
    }
    mock_controller.update = MagicMock()
    
    pipeline = ReflectionPipeline(mock_controller)
    
    result = pipeline.propose_synapse(Principal.AI_AGENT, "node-A", "node-B")
    # Should return None and NOT call update
    assert result is None
    mock_controller.update.assert_not_called()
