import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from cognitive_core.reasoning import ReasoningEngine

def test_reasoning_synthesize():
    mock_controller = MagicMock()
    mock_controller.search.return_value = {"results": [{"id": "node2"}]}
    
    engine = ReasoningEngine(mock_controller)
    
    context = [{"id": "node1"}]
    
    # Simple query, no extra retrieval
    result = engine.synthesize(Principal.AI_AGENT, context, "summary")
    assert result["context_used"] == 1
    assert result["extra_retrieved"] == 0
    mock_controller.search.assert_not_called()
    
    # Detailed query triggers read-only search
    result_detailed = engine.synthesize(Principal.AI_AGENT, context, "detailed analysis")
    assert result_detailed["context_used"] == 1
    assert result_detailed["extra_retrieved"] == 1
    mock_controller.search.assert_called_once()
