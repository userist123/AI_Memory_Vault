import enum
from typing import Dict, Any, List, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal

class RiskLevel(enum.Enum):
    LOW = "low"
    HIGH = "high"

class ApprovalRequiredError(Exception):
    pass

class ToolRouter:
    """
    Translates cognitive decisions into actual MemoryController API calls.
    Enforces the Autonomy Policy.
    """
    def __init__(self, memory_controller: MemoryController):
        self.controller = memory_controller
        
        # Action mappings to risk levels.
        # Most normal operations are low risk.
        self.risk_policy = {
            "search": RiskLevel.LOW,
            "read": RiskLevel.LOW,
            "propose": RiskLevel.LOW, # Creating new memory is low risk
            "update": RiskLevel.LOW,
            "archive": RiskLevel.LOW,
            "supersede": RiskLevel.LOW,
            "delete_canonical": RiskLevel.HIGH, # Destructive operations require approval
            "modify_raw_imports": RiskLevel.HIGH
        }
        
    def check_risk(self, action: str) -> RiskLevel:
        return self.risk_policy.get(action, RiskLevel.HIGH)
        
    def _check_knowledge_reconciliation_boundary(self, principal: Principal, action: str, kwargs: Dict[str, Any]) -> None:
        """
        BRAIN-13: Prevents automatic modification or archiving of human-verified memories.
        """
        if action in ("update", "archive", "supersede"):
            node_id = None
            if action == "archive":
                node_id = kwargs.get("note_id") # Note: signature might be note_id or id depending on controller
                if not node_id and len(kwargs) == 1:
                    node_id = list(kwargs.values())[0]
            elif action == "update":
                node_id = kwargs.get("note_id")
                if not node_id and "id" in kwargs:
                    node_id = kwargs["id"]
            elif action == "supersede":
                node_id = kwargs.get("old_id")
                    
            if node_id:
                try:
                    pack = self.controller.read(principal, node_id)
                    results = pack.get("results", [])
                    if results:
                        node = results[0]
                        if node.get("verification") == "verified":
                            raise ApprovalRequiredError(f"Action '{action}' targets a human-verified memory (id={node_id}) and requires explicit user approval.")
                except ApprovalRequiredError:
                    raise
                except Exception:
                    pass
        
    def execute(self, principal: Principal, action: str, kwargs: Dict[str, Any]) -> Any:
        """
        Executes a mapped action on the MemoryController.
        Raises ApprovalRequiredError if action is HIGH risk or violates reconciliation boundaries.
        """
        risk = self.check_risk(action)
        if risk == RiskLevel.HIGH:
            raise ApprovalRequiredError(f"Action '{action}' is HIGH RISK and requires explicit user approval.")
            
        self._check_knowledge_reconciliation_boundary(principal, action, kwargs)
            
        if action == "search":
            return self.controller.search(principal, **kwargs)
        elif action == "read":
            return self.controller.read(principal, **kwargs)
        elif action == "propose":
            return self.controller.propose(principal, **kwargs)
        elif action == "update":
            if hasattr(self.controller, "update"):
                return getattr(self.controller, "update")(principal, **kwargs)
            else:
                raise NotImplementedError("Update not fully implemented in MemoryController")
        elif action == "archive":
            if hasattr(self.controller, "archive"):
                return getattr(self.controller, "archive")(principal, **kwargs)
            else:
                raise NotImplementedError("Archive not fully implemented in MemoryController")
        elif action == "supersede":
            if hasattr(self.controller, "supersede"):
                return getattr(self.controller, "supersede")(principal, **kwargs)
            else:
                raise NotImplementedError("Supersede not implemented in MemoryController")
        else:
            raise ValueError(f"Unknown action: {action}")
