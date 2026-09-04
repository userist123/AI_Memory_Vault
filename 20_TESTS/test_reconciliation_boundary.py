import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from cognitive_core.tool_router import ToolRouter, ApprovalRequiredError

def test_reconciliation_boundary_blocks_verified():
    mock_controller = MagicMock()
    # Mock reading a verified node
    mock_controller.read.return_value = {
        "results": [{"id": "node-1", "verification": "verified"}]
    }
    
    router = ToolRouter(mock_controller)
    
    with pytest.raises(ApprovalRequiredError, match="human-verified memory"):
        router.execute(Principal.AI_AGENT, "update", {"note_id": "node-1", "content": "test"})
        
    with pytest.raises(ApprovalRequiredError, match="human-verified memory"):
        router.execute(Principal.AI_AGENT, "archive", {"note_id": "node-1"})
        
def test_reconciliation_boundary_allows_unverified():
    mock_controller = MagicMock()
    mock_controller.update = MagicMock(return_value=True)
    # Mock reading an unverified node
    mock_controller.read.return_value = {
        "results": [{"id": "node-2", "verification": "unverified"}]
    }
    
    router = ToolRouter(mock_controller)
    
    # Should not raise exception
    result = router.execute(Principal.AI_AGENT, "update", {"note_id": "node-2", "content": "test"})
    assert result is True
