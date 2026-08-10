import os
import tempfile
import pytest
from unittest.mock import MagicMock

from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from cognitive_core.executive import Executive
from cognitive_core.planning import ActivePlan

def test_executive_continuity():
    mock_controller = MagicMock()
    mock_controller.search.return_value = {"results": [{"id": "node-1"}]}
    mock_controller.read = MagicMock(return_value={"results": [{"id": "node-1"}]})
    mock_controller.cognitive_read = MagicMock(return_value={"results": [{"id": "node-1"}]})

    
    with tempfile.TemporaryDirectory() as temp_dir:
        exec1 = Executive(mock_controller, checkpoint_dir=temp_dir)
        
        plan = ActivePlan("test goal", [
            {"step": 1, "action": "search", "query": "step 1"},
            {"step": 2, "action": "search", "query": "step 2"}
        ])
        
        exec1.active_plan = plan
        exec1.working_memory.admit([({
            "id": "node-1", "content": "test", "confidence": "high"
        }, 1.0)])
        
        # Execute first step
        res1 = exec1.step_loop(Principal.AI_AGENT)
        assert res1["status"] == "success"
        assert exec1.active_plan.current_step_index == 1
        
        # WIRE-5: Auto-checkpoint should have written files
        assert os.path.exists(os.path.join(temp_dir, "wm.json"))
        assert os.path.exists(os.path.join(temp_dir, "plan.json"))
        
        # New process starts
        exec2 = Executive(mock_controller)
        exec2.load_state(temp_dir, Principal.AI_AGENT)
        
        assert exec2.active_plan is not None
        assert exec2.active_plan.goal == "test goal"
        assert exec2.active_plan.current_step_index == 1
        assert "node-1" in exec2.working_memory.buffer
        
        # Execute next step
        res2 = exec2.step_loop(Principal.AI_AGENT)
        assert res2["status"] == "success"
        
        assert exec2.active_plan.current_step_index == 2
        assert exec2.active_plan.is_complete()
        
        res3 = exec2.step_loop(Principal.AI_AGENT)
        assert res3["status"] == "idle"
