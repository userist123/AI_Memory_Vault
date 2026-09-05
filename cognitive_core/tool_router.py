import enum
from typing import Dict, Any
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
        self.risk_policy = {
            "search": RiskLevel.LOW,
            "read": RiskLevel.LOW,
            "propose": RiskLevel.LOW,
            "update": RiskLevel.LOW,
            "archive": RiskLevel.LOW,
            "supersede": RiskLevel.LOW,
            "delete_canonical": RiskLevel.HIGH,
            "modify_raw_imports": RiskLevel.HIGH,
        }

    def check_risk(self, action: str) -> RiskLevel:
        return self.risk_policy.get(action, RiskLevel.HIGH)

    def _check_knowledge_reconciliation_boundary(
        self, principal: Principal, action: str, kwargs: Dict[str, Any]
    ) -> None:
        """BRAIN-13: block automatic mutation/archive of verified memories."""
        if action not in ("update", "archive", "supersede"):
            return

        node_id = None
        if action == "archive":
            node_id = kwargs.get("note_id")
            if not node_id and len(kwargs) == 1:
                node_id = list(kwargs.values())[0]
        elif action == "update":
            node_id = kwargs.get("note_id") or kwargs.get("id")
        elif action == "supersede":
            node_id = kwargs.get("old_id")

        if not node_id:
            return

        try:
            # Use the storage record directly. Public `read()` intentionally rejects
            # non-ACTIVE notes, so using it here could hide a verified REVIEW note.
            node = self.controller.storage.get(node_id)
        except Exception as exc:
            raise ApprovalRequiredError(
                f"Unable to validate reconciliation boundary for action '{action}' "
                f"(id={node_id}); explicit user approval is required."
            ) from exc

        if node and node.get("verification") == "verified":
            raise ApprovalRequiredError(
                f"Action '{action}' targets a human-verified memory (id={node_id}) "
                "and requires explicit user approval."
            )

    def execute(self, principal: Principal, action: str, kwargs: Dict[str, Any]) -> Any:
        """
        Executes a mapped action on the MemoryController.
        Raises ApprovalRequiredError if action is HIGH risk or violates reconciliation boundaries.
        """
        risk = self.check_risk(action)
        if risk == RiskLevel.HIGH:
            raise ApprovalRequiredError(
                f"Action '{action}' is HIGH RISK and requires explicit user approval."
            )

        self._check_knowledge_reconciliation_boundary(principal, action, kwargs)

        if action == "search":
            return self.controller.search(principal, **kwargs)
        if action == "read":
            return self.controller.read(principal, **kwargs)
        if action == "propose":
            return self.controller.propose(principal, **kwargs)
        if action == "update":
            if hasattr(self.controller, "update"):
                return getattr(self.controller, "update")(principal, **kwargs)
            raise NotImplementedError("Update not fully implemented in MemoryController")
        if action == "archive":
            if hasattr(self.controller, "archive"):
                return getattr(self.controller, "archive")(principal, **kwargs)
            raise NotImplementedError("Archive not fully implemented in MemoryController")
        if action == "supersede":
            if hasattr(self.controller, "supersede"):
                return getattr(self.controller, "supersede")(principal, **kwargs)
            raise NotImplementedError("Supersede not implemented in MemoryController")
        raise ValueError(f"Unknown action: {action}")
