from typing import Dict, Any, List, Optional
from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from cognitive_core.tool_router import ToolRouter
from .base_agent import BaseWorkerAgent

class VerifierAgent(BaseWorkerAgent):
    """Specialized Memory Verifier Agent.
    Validates provenance claims, verification flags, and source-of-truth hierarchy conformance.
    """

    def __init__(self, controller: MemoryController, router: Optional[ToolRouter] = None):
        super().__init__(name="VerifierAgent", role="verifier", controller=controller, router=router, max_steps=2)
        self.permitted_actions = ["read"]

    def process_task(self, principal: Principal, task: Dict[str, Any]) -> Dict[str, Any]:
        nodes_to_verify = task.get("nodes", [])
        verified_nodes = []
        unverified_nodes = []
        violations = []

        for node in nodes_to_verify:
            node_id = node.get("id", "unknown")
            verification = node.get("verification", "unverified")
            prov = node.get("provenance", {})
            source_type = prov.get("source_type", "unknown")

            # Check if AI claims human or official provenance illegitimately
            if source_type in ["user", "official"] and verification != "verified":
                violations.append(f"Node {node_id} claims '{source_type}' without attested verification")

            if verification == "verified":
                verified_nodes.append(node)
            else:
                unverified_nodes.append(node)

        return {
            "status": "success",
            "total_inspected": len(nodes_to_verify),
            "verified_count": len(verified_nodes),
            "unverified_count": len(unverified_nodes),
            "violations": violations,
            "is_clean": len(violations) == 0
        }
