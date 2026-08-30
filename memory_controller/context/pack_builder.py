import json
from typing import List, Dict, Any, Optional

from .budget import ContextBudget, BudgetExceededError, load_agent_budget


class ContextPackBuilder:
    """Build the final context payload and enforce the hard runtime budget.

    This is the last memory/context boundary before an agent receives the pack.
    The budget applies to the complete serialized pack, not only to note bodies.
    """

    @staticmethod
    def _serialized_size(value: Any) -> int:
        return len(json.dumps(value, ensure_ascii=False, default=str, separators=(",", ":")).encode("utf-8"))

    def _resolve_budget(self, agent_id: str, budget: Dict[str, Any]) -> ContextBudget:
        # An explicit runtime budget is authoritative, but preserve the agent's
        # configured result limits when only soft/hard byte limits are supplied.
        configured = load_agent_budget(agent_id)
        if not budget:
            return configured
        return ContextBudget({
            "max_notes": int(budget.get("max_notes", configured.max_notes)),
            "max_full_documents": int(budget.get("max_full_documents", configured.max_full_documents)),
            "soft_limit_bytes": int(budget.get("soft", configured.soft_context_budget)),
            "hard_limit_bytes": int(budget.get("hard", configured.hard_context_budget)),
        })

    @staticmethod
    def _base_pack(request_id: str, agent_id: str, resolved: ContextBudget, disclosure_level: str) -> Dict[str, Any]:
        return {
            "requestId": request_id,
            "agentId": agent_id,
            "budget": {
                "soft": resolved.soft_context_budget,
                "hard": resolved.hard_context_budget,
                "max_notes": resolved.max_notes,
                "max_full_documents": resolved.max_full_documents,
            },
            "disclosureLevel": disclosure_level,
            "results": [],
        }

    def _build_pack(
        self,
        request_id: str,
        agent_id: str,
        resolved: ContextBudget,
        results: List[Dict[str, Any]],
        disclosure_level: str,
        minimal_provenance: Optional[List[Dict[str, Any]]],
        next_page_token: Optional[str],
        audit_ref: Optional[str],
    ) -> Dict[str, Any]:
        pack = self._base_pack(request_id, agent_id, resolved, disclosure_level)
        pack["results"] = results

        if minimal_provenance:
            for res, prov in zip(pack["results"], minimal_provenance):
                res.setdefault("provenance", {})
                res["provenance"].setdefault("source_type", prov.get("source_type"))
                res["provenance"].setdefault("source_ref", prov.get("source_ref"))
        if next_page_token:
            pack["nextPageToken"] = next_page_token
        if audit_ref:
            pack["auditRef"] = audit_ref
        return pack

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

        pack = self._build_pack(
            request_id, agent_id, resolved, safe_results, disclosure_level,
            minimal_provenance, next_page_token, audit_ref
        )

        # The previous budget check measured only the notes. This check measures
        # the actual wire payload. If metadata/provenance/token overhead pushes
        # it over the limit, progressively remove lowest-value results first.
        while self._serialized_size(pack) > resolved.hard_context_budget and len(safe_results) > 1:
            safe_results = safe_results[:-1]
            pack = self._build_pack(
                request_id, agent_id, resolved, safe_results, disclosure_level,
                minimal_provenance, next_page_token, audit_ref
            )

        if self._serialized_size(pack) > resolved.hard_context_budget:
            raise BudgetExceededError(
                "Final context pack exceeds hard budget: "
                f"{self._serialized_size(pack)} > {resolved.hard_context_budget} bytes"
            )

        return pack
