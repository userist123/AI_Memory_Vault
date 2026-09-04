import os
import tempfile
import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from cognitive_core.working_memory import WorkingMemory

def test_working_memory_save_load():
    wm = WorkingMemory(capacity=5)
    # Mock some admitted nodes
    wm.admit([
        ({"id": "node-1"}, 1.0),
        ({"id": "node-2"}, 0.8)
    ])
    
    # Verify they are in WM
    assert len(wm.buffer) == 2
    assert wm.tick == 1
    
    with tempfile.TemporaryDirectory() as temp_dir:
        state_file = os.path.join(temp_dir, "wm_state.json")
        
        # Save state
        wm.save_state(state_file)
        assert os.path.exists(state_file)
        
        # Create a new WM instance
        new_wm = WorkingMemory(capacity=5)
        
        # Mock MemoryController to return the nodes when loading
        mock_controller = MagicMock()
        def mock_read(principal, node_id, **kwargs):
            return {"results": [{"id": node_id, "mock_data": True}]}
        mock_controller.read.side_effect = mock_read
        
        # Load state
        new_wm.load_state(state_file, mock_controller, Principal.AI_AGENT)
        
        # Verify state was restored
        assert new_wm.tick == 1
        assert len(new_wm.buffer) == 2
        
        # Verify node-1
        assert "node-1" in new_wm.buffer
        assert new_wm.buffer["node-1"]["activation"] == 1.0
        assert new_wm.buffer["node-1"]["node"]["mock_data"] is True
        
        # Verify node-2
        assert "node-2" in new_wm.buffer
        assert new_wm.buffer["node-2"]["activation"] == 0.8

def test_working_memory_load_missing_node():
    wm = WorkingMemory(capacity=5)
    wm.admit([({"id": "node-1"}, 1.0)])
    
    with tempfile.TemporaryDirectory() as temp_dir:
        state_file = os.path.join(temp_dir, "wm_state.json")
        wm.save_state(state_file)
        
        new_wm = WorkingMemory(capacity=5)
        
        # Mock MemoryController to simulate node-1 being deleted or unauthorized
        mock_controller = MagicMock()
        mock_controller.read.side_effect = ValueError("Not found or access denied")
        
        new_wm.load_state(state_file, mock_controller, Principal.AI_AGENT)
        
        # Buffer should be empty because node-1 couldn't be loaded
        assert len(new_wm.buffer) == 0
        assert new_wm.tick == 1
