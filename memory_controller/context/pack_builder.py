from typing import List, Dict, Any, Optional

from .budget import ContextBudget, BudgetExceededError, load_agent_budget
from ..memory_trace import record_observed_memory_trace


class ContextPackBuilder:
    """Build the final context payload and enforce byte + token budgets."""

    @staticmethod
    def _resolve_budget(agent_id: str, budget: Dict[str, Any]) -> ContextBudget:
        configured = load_agent_budget(agent_id)
        if not budget:
            return configured
        return ContextBudget({
            "max_notes": int(budget.get("max_notes", configured.max_notes)),
            "max_full_documents": int(budget.get("max_full_documents", configured.max_full_documents)),
            "soft_limit_bytes": int(budget.get("soft", configured.soft_context_budget)),
            "hard_limit_bytes": int(budget.get("hard", configured.hard_context_budget)),
            "soft_limit_tokens": int(budget.get("soft_tokens", configured.soft_token_budget)),
            "hard_limit_tokens": int(budget.get("hard_tokens", configured.hard_token_budget)),
            "chars_per_token": float(budget.get("chars_per_token", configured.chars_per_token)),
        })

    @staticmethod
    def _base_pack(request_id: str, agent_id: str, resolved: ContextBudget, disclosure_level: str) -> Dict[str, Any]:
        return {
            "requestId": request_id,
            "agentId": agent_id,
            "budget": {
                "soft": resolved.soft_context_budget,
                "hard": resolved.hard_context_budget,
                "soft_tokens": resolved.soft_token_budget,
                "hard_tokens": resolved.hard_token_budget,
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

        # Keep the highest-value results until BOTH transport and token budgets fit.
        while safe_results:
            pack = self._build_pack(
                request_id, agent_id, resolved, safe_results, disclosure_level,
                minimal_provenance, next_page_token, audit_ref
            )
            serialized_size = resolved.serialized_size(pack)
            estimated_tokens = resolved.estimate_tokens(pack)
            if serialized_size <= resolved.hard_context_budget and estimated_tokens <= resolved.hard_token_budget:
                try:
                    record_observed_memory_trace(
                        run_id=request_id,
                        results=pack.get("results", []),
                        context_size_bytes=serialized_size,
                        estimated_tokens=estimated_tokens,
                    )
                except Exception:
                    pass
                return pack
            safe_results = safe_results[:-1]

        # Empty result pack is always the last safe representation. If even the
        # envelope exceeds the configured hard budget, fail closed.
        pack = self._build_pack(
            request_id, agent_id, resolved, [], disclosure_level,
            None, next_page_token, audit_ref
        )
        serialized_size = resolved.serialized_size(pack)
        estimated_tokens = resolved.estimate_tokens(pack)
        if serialized_size > resolved.hard_context_budget:
            raise BudgetExceededError(
                f"Final context pack exceeds hard byte budget: {serialized_size} > {resolved.hard_context_budget} bytes"
            )
        if estimated_tokens > resolved.hard_token_budget:
            raise BudgetExceededError(
                f"Final context pack exceeds hard token budget: {estimated_tokens} > {resolved.hard_token_budget} tokens"
            )
        try:
            record_observed_memory_trace(
                run_id=request_id,
                results=[],
                context_size_bytes=serialized_size,
                estimated_tokens=estimated_tokens,
            )
        except Exception:
            pass
        return pack

