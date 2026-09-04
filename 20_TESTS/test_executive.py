import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from cognitive_core.executive import Executive

def test_executive_process_intent():
    mock_controller = MagicMock()
    mock_controller.search.return_value = {"results": [
        {"id": "n1", "content": "test", "confidence": "high", "relations": []}
    ]}
    mock_controller.cognitive_read = MagicMock(return_value={"results": []})
    
    exec1 = Executive(mock_controller)
    result = exec1.process_intent(Principal.AI_AGENT, "find something")
    assert result["status"] == "success"
