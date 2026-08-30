import json
from typing import List, Dict, Any, Optional

from .budget import ContextBudget, load_agent_budget


class ContextPackBuilder:
    """Assemble the final context payload and enforce the runtime budget gate."""

    def __init__(self):
        pass

    @staticmethod
    def _serialized_size(value: Any) -> int:
        return len(json.dumps(value, ensure_ascii=False, default=str, separators=(",", ":")).encode("utf-8"))

    def _resolve_budget(self, agent_id: str, budget: Dict[str, Any]) -> ContextBudget:
        if budget and "soft" in budget and "hard" in budget:
            return ContextBudget({"soft_limit_bytes": int(budget["soft"]), "hard_limit_bytes": int(budget["hard"])})
        return load_agent_budget(agent_id)

    def build(
        self,
        request_id: str,
        agent_id: str,
        budget: Dict[str, Any],
        results: List[Dict[str, Any]],
        disclosure_level: str,
        minimal_provenance: List[Dict[str, Any]] = None,
        next_page_token: Optional[str] = None,
        audit_ref: Optional[str] = None,
    ) -> Dict[str, Any]:
        resolved = self._resolve_budget(agent_id, budget or {})
        safe_results = [dict(item) for item in (results or [])]
        safe_results = resolved.apply_degradation(safe_results)

        effective_budget = {
            "soft": resolved.soft_context_budget,
            "hard": resolved.hard_context_budget,
            "max_notes": resolved.max_notes,
            "max_full_documents": resolved.max_full_documents,
        }
        pack: Dict[str, Any] = {
            "requestId": request_id,
            "agentId": agent_id,
            "budget": effective_budget,
            "disclosureLevel": disclosure_level,
            "results": safe_results,
        }
        if minimal_provenance:
            for res, prov in zip(pack["results"], minimal_provenance):
                res.setdefault("provenance", {})
                res["provenance"].setdefault("source_type", prov.get("source_type"))
                res["provenance"].setdefault("source_ref", prov.get("source_ref"))
        if next_page_token:
            pack["nextPageToken"] = next_page_token
        if audit_ref:
            pack["auditRef"] = audit_ref

        usage = self._serialized_size(pack)
        if usage > resolved.hard_context_budget:
            raise RuntimeError(
                f"Final context pack exceeds hard budget: {usage} > {resolved.hard_context_budget} bytes"
            )
        return pack
