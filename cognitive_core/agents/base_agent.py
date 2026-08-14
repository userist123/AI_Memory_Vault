from abc import ABC, abstractmethod
from typing import Dict, Any, List, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from cognitive_core.tool_router import ToolRouter

class BaseWorkerAgent(ABC):
    """Abstract base class for all specialized worker agents in the cognitive architecture.
    Enforces least privilege, bounded step execution, and structured messaging.
    """

    def __init__(self, name: str, role: str, controller: MemoryController, router: Optional[ToolRouter] = None, max_steps: int = 3):
        self.name = name
        self.role = role
        self.controller = controller
        self.router = router or ToolRouter(self.controller)
        self.max_steps = max_steps
        self.permitted_actions: List[str] = []

    def can_perform(self, action: str) -> bool:
        return action in self.permitted_actions

    def execute_action(self, principal: Principal, action: str, kwargs: Dict[str, Any]) -> Any:
        if not self.can_perform(action):
            raise PermissionError(f"Agent '{self.name}' (role: {self.role}) is not authorized to perform action '{action}'")
        return self.router.execute(principal, action, kwargs)

    @abstractmethod
    def process_task(self, principal: Principal, task: Dict[str, Any]) -> Dict[str, Any]:
        """Executes the agent's specialized task workflow and returns structured results."""
        pass
