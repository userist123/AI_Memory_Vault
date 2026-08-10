import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from cognitive_core.consolidation import Consolidator

def test_consolidation_success():
    mock_controller = MagicMock()
    mock_router = MagicMock()
    mock_controller.search.return_value = {
        "results": [
            {"id": "lesson-1", "type": "lesson", "lifecycle": Lifecycle.REVIEW.value, "content": "lesson one"},
            {"id": "lesson-2", "type": "lesson", "lifecycle": Lifecycle.REVIEW.value, "content": "lesson two"}
        ]
    }
    
    consolidator = Consolidator(mock_controller, mock_router)
    new_id = consolidator.consolidate_lessons(Principal.AI_AGENT)
    
    assert new_id is not None
    
    # Verify propose was called through ToolRouter
    calls = mock_router.execute.call_args_list
    propose_calls = [c for c in calls if c[0][1] == "propose"]
    assert len(propose_calls) == 1
    proposed_node = propose_calls[0][0][2]["note_data"]
    assert proposed_node["type"] == "knowledge"
    assert "lesson-1" in proposed_node["provenance"]["source_refs"]
    assert "lesson-2" in proposed_node["provenance"]["source_refs"]
    
    # Verify archive was called through ToolRouter
    archive_calls = [c for c in calls if c[0][1] == "archive"]
    assert len(archive_calls) == 2

def test_consolidation_insufficient_lessons():
    mock_controller = MagicMock()
    mock_router = MagicMock()
    mock_controller.search.return_value = {
        "results": [
            {"id": "lesson-1", "type": "lesson", "lifecycle": Lifecycle.REVIEW.value, "content": "lesson one"}
        ]
    }
    
    consolidator = Consolidator(mock_controller, mock_router)
    new_id = consolidator.consolidate_lessons(Principal.AI_AGENT)
    
    assert new_id is None
    mock_router.execute.assert_not_called()
