from abc import ABC, abstractmethod
from typing import Dict, Any, List, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from cognitive_core.tool_router import ToolRouter


class BaseWorkerAgent(ABC):
    """Abstract base class for specialized worker agents.

    The worker layer is intentionally provider/dispatcher neutral. A caller may
    inject an optional dispatcher for explicit LLM execution, but constructing a
    worker must never import or instantiate an unavailable global dispatcher.
    """

    def __init__(
        self,
        name: str,
        role: str,
        controller: MemoryController,
        router: Optional[ToolRouter] = None,
        max_steps: int = 3,
        dispatcher: Optional[Any] = None,
    ):
        self.name = name
        self.role = role
        self.controller = controller
        self.router = router or ToolRouter(self.controller)
        self.max_steps = max_steps
        self.permitted_actions: List[str] = []
        self.dispatcher = dispatcher

    def can_perform(self, action: str) -> bool:
        return action in self.permitted_actions

    def execute_action(self, principal: Principal, action: str, kwargs: Dict[str, Any]) -> Any:
        if not self.can_perform(action):
            raise PermissionError(
                f"Agent '{self.name}' (role: {self.role}) is not authorized "
                f"to perform action '{action}'"
            )
        return self.router.execute(principal, action, kwargs)

    def dispatch_llm(self, user_prompt: str, system_prompt: Optional[str] = None) -> str:
        """Dispatch explicitly through an injected model dispatcher.

        LLM execution is optional and must be supplied by the caller. The
        worker layer does not manufacture a global GPU/Kaggle/Colab dispatcher.
        """
        if self.dispatcher is None:
            raise RuntimeError(
                f"No LLM dispatcher is configured for agent '{self.name}'. "
                "Inject an explicit dispatcher before calling dispatch_llm()."
            )

        sys_p = system_prompt or (
            f"You are the specialized {self.name} (Role: {self.role}) "
            "operating within the AI Memory Vault."
        )
        return self.dispatcher.dispatch(
            agent_role=self.role,
            system_prompt=sys_p,
            user_input=user_prompt,
        )

    @abstractmethod
    def process_task(self, principal: Principal, task: Dict[str, Any]) -> Dict[str, Any]:
        """Execute the agent's specialized task workflow."""
        raise NotImplementedError
