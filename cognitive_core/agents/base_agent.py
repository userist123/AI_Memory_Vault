from abc import ABC, abstractmethod
from typing import Dict, Any, List, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from cognitive_core.tool_router import ToolRouter

class BaseWorkerAgent(ABC):
    """Abstract base class for all specialized worker agents in the cognitive architecture.
    Enforces least privilege, bounded step execution, and structured messaging.
    """

    def __init__(self, name: str, role: str, controller: MemoryController, router: Optional[ToolRouter] = None, max_steps: int = 3, dispatcher: Optional[Any] = None):
        self.name = name
        self.role = role
        self.controller = controller
        self.router = router or ToolRouter(self.controller)
        self.max_steps = max_steps
        self.permitted_actions: List[str] = []
        
        # Conectam fiecare agent la ferma de GPU-uri (Kaggle/Colab)
        if dispatcher is None:
            from cognitive_core.orchestrator import MultiAgentDispatcher
            self.dispatcher = MultiAgentDispatcher()
        else:
            self.dispatcher = dispatcher

    def can_perform(self, action: str) -> bool:
        return action in self.permitted_actions

    def execute_action(self, principal: Principal, action: str, kwargs: Dict[str, Any]) -> Any:
        if not self.can_perform(action):
            raise PermissionError(f"Agent '{self.name}' (role: {self.role}) is not authorized to perform action '{action}'")
        return self.router.execute(principal, action, kwargs)

    def dispatch_llm(self, user_prompt: str, system_prompt: Optional[str] = None) -> str:
        """Executa rationamentul agentului direct pe ferma de calcul GPU (Kaggle 32B / Colab 7B)."""
        sys_p = system_prompt or f"You are the specialized {self.name} (Role: {self.role}) operating within the AI Memory Vault."
        return self.dispatcher.dispatch(
            agent_role=self.role,
            system_prompt=sys_p,
            user_input=user_prompt
        )

    @abstractmethod
    def process_task(self, principal: Principal, task: Dict[str, Any]) -> Dict[str, Any]:
        """Executes the agent's specialized task workflow and returns structured results."""
        pass
