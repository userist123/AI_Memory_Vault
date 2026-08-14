from typing import Dict, Any, List, Optional
from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from cognitive_core.tool_router import ToolRouter
from cognitive_core.reflection import ReflectionPipeline, FormalReflexion, SelfRefine
from .base_agent import BaseWorkerAgent

class CriticAgent(BaseWorkerAgent):
    """Specialized Critic & Formal Reflexion Agent.
    Evaluates failure outcomes via 6-stage Reflexion and critiques candidate memories via SelfRefine.
    """

    def __init__(self, controller: MemoryController, router: Optional[ToolRouter] = None):
        super().__init__(name="CriticAgent", role="critic", controller=controller, router=router, max_steps=3)
        self.permitted_actions = ["read", "propose"]
        self.reflection = ReflectionPipeline(self.controller)

    def process_task(self, principal: Principal, task: Dict[str, Any]) -> Dict[str, Any]:
        task_type = task.get("type", "reflect")

        if task_type == "reflect":
            intent = task.get("intent", {})
            action = task.get("action", {})
            result = task.get("result", {})
            note_id = self.reflection.evaluate_outcome(principal, intent, action, result)
            return {
                "status": "success",
                "reflection_note_id": note_id,
                "reflexion_applied": note_id is not None
            }
        elif task_type == "self_refine":
            candidate = task.get("candidate", {})
            passed, refined = SelfRefine.refine_memory(candidate)
            return {
                "status": "success",
                "passed_filter": passed,
                "refined_candidate": refined
            }
        else:
            return {"status": "error", "message": f"Unknown task type '{task_type}'"}
