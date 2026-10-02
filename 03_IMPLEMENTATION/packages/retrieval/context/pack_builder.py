import base64
import zlib
import re
from typing import Callable, List, Dict, Any, Optional, Mapping

from .budget import ContextBudget, BudgetExceededError, load_agent_budget
from .progressive_disclosure import ProgressiveDisclosure
try:
    from ..memory_trace import record_observed_memory_trace
except ModuleNotFoundError:
    # Memory tracing is optional observability; context packing must remain usable
    # when the optional trace provider is not installed in this checkout.
    def record_observed_memory_trace(**_: Any) -> None:
        return None
from security.knowledge_handoff import VerifiedKnowledgeHandoff
from security.context_compression import AdaptiveContextCompressor, CompressionRouter


class ContextPackBuilder:
    """Build final context only from verified, reduced knowledge."""

    def __init__(self, verifier: Optional[Callable[[Dict[str, Any]], Mapping[str, Any]]] = None):
        # A verifier may enrich an item with a trusted verification record.
        # Without one, the item must already carry an explicit trusted record.
        self.verifier = verifier
        self.handoff = VerifiedKnowledgeHandoff()
        self.compressor = AdaptiveContextCompressor()

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
            "tokenizer": budget.get("tokenizer", configured.tokenizer),
            "cost_per_input_token": budget.get("cost_per_input_token", configured.cost_per_input_token),
            "latency_ms_per_input_token": budget.get("latency_ms_per_input_token", configured.latency_ms_per_input_token),
            "estimated_overhead_tokens": budget.get("estimated_overhead_tokens", configured.estimated_overhead_tokens),
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
            "reduction": {
                "verified_first": True,
                "stage_order": ["verification", "compression_router", "protected_spans", "query_selection", "reduction", "budget", "progressive_disclosure", "validation"],
                "tokens_saved": 0,
                "items_reduced": 0,
                "items_rejected_unverified": 0,
                "net_tokens_saved": 0,
                "cost_saved": 0.0,
                "latency_saved_ms": 0.0,
                "tokenizer_mode": "heuristic_fallback",
            },
        }

    def _verify_and_reduce(
        self,
        results: List[Dict[str, Any]],
        resolved: ContextBudget,
        *,
        query: str = "",
    ) -> tuple[List[Dict[str, Any]], Dict[str, Any]]:
        reduced: List[Dict[str, Any]] = []
        tokens_saved = 0
        rejected = 0
        net_tokens_saved = 0
        cost_saved = 0.0
        latency_saved_ms = 0.0
        tokenizer_mode = "real" if resolved.tokenizer is not None else "heuristic_fallback"
        reduction_chars = max(128, int(resolved.soft_token_budget * resolved.chars_per_token))

        for raw in results:
            item = dict(raw)
            if self.verifier is not None:
                verification = self.verifier(dict(item))
                if not isinstance(verification, Mapping):
                    rejected += 1
                    continue
                item["verification"] = dict(verification)

            verification = item.get("verification")
            if not isinstance(verification, Mapping):
                rejected += 1
                continue

            content = str(item.get("content", ""))
            compressor = AdaptiveContextCompressor(
                router=CompressionRouter(
                    tokenizer=resolved.tokenizer,
                    chars_per_token=resolved.chars_per_token,
                    cost_per_input_token=resolved.cost_per_input_token,
                    latency_ms_per_input_token=resolved.latency_ms_per_input_token,
                    estimated_overhead_tokens=resolved.estimated_overhead_tokens,
                )
            )
            protected_artifact = any(item.get(key) is not None for key in ("code", "signature", "dependencies", "identifiers"))
            compression = compressor.compress(
                content, query=query,
                target_chars=reduction_chars,
                do_not_compress=bool(item.get("do_not_compress", False)) or protected_artifact,
            )
            if not compression.validation.get("passed", False):
                rejected += 1
                continue

            compact = dict(item)
            compact["content"] = compression.content
            compact["compression"] = {
                "action": compression.decision.action,
                "reason": compression.decision.reason,
                "original_tokens": compression.decision.original_tokens,
                "estimated_tokens": compression.decision.estimated_tokens,
                "redundancy": compression.decision.redundancy,
                "protected_spans": len(compression.protected_spans),
                "removed_segments": len(compression.removed_segments),
                "fallback": compression.fallback,
                "validation": dict(compression.validation),
                "tokenizer_mode": compression.decision.tokenizer_mode,
                "net_tokens_saved": compression.decision.net_tokens_saved,
                "cost_saved": compression.decision.cost_saved,
                "latency_saved_ms": compression.decision.latency_saved_ms,
            }
            compact["reduction"] = {
                "original_chars": len(content),
                "final_chars": len(compression.content),
                "bytes_saved": max(0, len(content.encode("utf-8")) - len(compression.content.encode("utf-8"))),
                "token_estimate_before": compression.decision.original_tokens,
                "token_estimate_after": compression.decision.estimated_tokens,
                "tokens_saved": max(0, compression.decision.original_tokens - compression.decision.estimated_tokens),
            }
            reduced.append(compact)
            tokens_saved += max(0, compression.decision.original_tokens - compression.decision.estimated_tokens)
            net_tokens_saved += max(0, compression.decision.net_tokens_saved)
            cost_saved += max(0.0, compression.decision.cost_saved or 0.0)
            latency_saved_ms += max(0.0, compression.decision.latency_saved_ms or 0.0)
            tokenizer_mode = compression.decision.tokenizer_mode
        return reduced, {
            "tokens_saved": tokens_saved,
            "items_reduced": len(reduced),
            "items_rejected_unverified": rejected,
            "net_tokens_saved": net_tokens_saved,
            "cost_saved": cost_saved,
            "latency_saved_ms": latency_saved_ms,
            "tokenizer_mode": tokenizer_mode,
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
        reduction_metrics: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        pack = self._base_pack(request_id, agent_id, resolved, disclosure_level)
        pack["results"] = [self._json_safe(dict(item)) for item in results]
        if reduction_metrics:
            pack["reduction"].update(reduction_metrics)
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

    @staticmethod
    def _protected_result(item: Dict[str, Any]) -> bool:
        if bool(item.get("do_not_compress")) or bool(item.get("protected_content")):
            return True
        content = str(item.get("content", item.get("snippet", "")))
        return bool(item.get("code") or item.get("signature") or item.get("dependencies") or item.get("identifiers")) or bool(
            re.search(r"(?im)^.*\b(?:MUST(?: NOT)?|NEVER|SHALL|REQUIRED|FORBIDDEN|DO_NOT_COMPRESS)\b.*$", content)
            or "```" in content
        )
    def _compact_for_token_budget(
        self,
        results: List[Dict[str, Any]],
        reduction_metrics: Dict[str, Any],
        resolved: ContextBudget,
        request_id: str,
        agent_id: str,
        disclosure_level: str,
        query: str,
    ) -> tuple[List[Dict[str, Any]], Dict[str, Any]]:
        """Fit optional representation details before considering result removal.

        This is a model-facing budget optimization, not a trust decision.
        Verification, provenance, requirements, acceptance data, protected spans,
        and mandatory content are never discarded. If content itself cannot fit
        after adaptive reduction, the caller still fails closed.
        """
        compact_results: List[Dict[str, Any]] = []
        for item in results:
            candidate = dict(item)
            compression = candidate.get("compression")
            if isinstance(compression, dict):
                candidate["compression"] = {
                    "action": compression["action"]
                } if "action" in compression else {}
            candidate.pop("reduction", None)
            compact_results.append(candidate)

        # Reserve the actual envelope + trust metadata, then give remaining
        # capacity to content. This avoids a fixed compression target that can
        # fit the document but not the final context pack.
        for candidate in compact_results:
            if "content" not in candidate:
                continue
            probe = dict(candidate)
            probe["content"] = ""
            probe_pack = self._build_pack(
                request_id, agent_id, resolved, [probe], disclosure_level,
                None, None, None, {"verified_first": True}
            )
            envelope_tokens = resolved.estimate_tokens(probe_pack)
            available_tokens = max(1, resolved.hard_token_budget - envelope_tokens)
            target_chars = max(1, int(available_tokens * resolved.chars_per_token))
            content = str(candidate.get("content", ""))
            if len(content) <= target_chars:
                continue
            router = CompressionRouter(
                min_tokens=1,
                tokenizer=resolved.tokenizer,
                chars_per_token=resolved.chars_per_token,
                cost_per_input_token=resolved.cost_per_input_token,
                latency_ms_per_input_token=resolved.latency_ms_per_input_token,
                estimated_overhead_tokens=resolved.estimated_overhead_tokens,
            )
            result = AdaptiveContextCompressor(router=router).compress(
                content,
                query=query,
                target_chars=target_chars,
                do_not_compress=False,
            )
            if result.validation.get("passed", False):
                candidate["content"] = result.content
                # Keep the original verified compression decision. The
                # second pass is only a budget-fitting representation step and
                # must not rewrite the semantic compression metric.
                if isinstance(compression, dict):
                    candidate["compression"] = compression
        compact_metrics = {
            key: reduction_metrics[key]
            for key in ("verified_first", "tokens_saved", "net_tokens_saved")
            if key in reduction_metrics
        }
        return compact_results, compact_metrics

    @staticmethod
    def _json_safe(value: Any) -> Any:
        """Convert compressed note payloads into transport-safe JSON values."""
        if isinstance(value, bytes):
            try:
                return zlib.decompress(value).decode("utf-8")
            except (OSError, UnicodeDecodeError, zlib.error):
                return base64.b64encode(value).decode("ascii")
        if isinstance(value, dict):
            return {key: ContextPackBuilder._json_safe(item) for key, item in value.items()}
        if isinstance(value, list):
            return [ContextPackBuilder._json_safe(item) for item in value]
        return value

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
        query: str = "",
        disclosure_query: str = "",
        provenance_storage_engine: Any = None,
    ) -> Dict[str, Any]:
        resolved = self._resolve_budget(agent_id, budget or {})
        safe_results = [dict(item) for item in (results or [])]

        # Mandatory order: verification -> semantic reduction -> token/byte budget.
        # Unverified content never reaches apply_degradation() or the final pack.
        effective_query = query.strip() or disclosure_query.strip()
        safe_results, reduction_metrics = self._verify_and_reduce(safe_results, resolved, query=effective_query)
        safe_results = resolved.apply_degradation(safe_results)

        disclosure = ProgressiveDisclosure(resolved)
        if disclosure_level == "full":
            disclosure_level = "full_document"
        if disclosure_level in {"metadata", "metadata_only"}:
            disclosure_level = "metadata_only"
            safe_results = disclosure.metadata_only(safe_results)
        elif disclosure_level == "snippet":
            safe_results = disclosure.snippet(safe_results)
        elif disclosure_level == "full_document":
            safe_results = disclosure.full_document(safe_results)
        elif disclosure_level == "sections":
            if not disclosure_query.strip():
                raise ValueError("sections disclosure requires disclosure_query")
            safe_results = disclosure.sections(safe_results, disclosure_query)
        elif disclosure_level == "provenance_on_demand":
            if provenance_storage_engine is None:
                raise ValueError("provenance_on_demand requires provenance_storage_engine")
            ids = [str(item.get("id")) for item in safe_results if item.get("id") is not None]
            provenance = disclosure.provenance_on_demand(ids, provenance_storage_engine)
            for item, raw in zip(safe_results, provenance):
                item["provenance_on_demand"] = raw
        elif disclosure_level != "provenance_on_demand":
            raise ValueError(f"unknown disclosure level: {disclosure_level}")
        
        # Keep the highest-value results until BOTH transport and token budgets fit.
        while safe_results:
            pack = self._build_pack(
                request_id, agent_id, resolved, safe_results, disclosure_level,
                minimal_provenance, next_page_token, audit_ref, reduction_metrics
            )
            serialized_size = resolved.serialized_size(pack)
            estimated_tokens = resolved.estimate_tokens(pack)
            if estimated_tokens > resolved.hard_token_budget:
                compact_results, compact_metrics = self._compact_for_token_budget(
                    safe_results,
                    reduction_metrics,
                    resolved,
                    request_id,
                    agent_id,
                    disclosure_level,
                    effective_query,
                )
                compact_pack = self._build_pack(
                    request_id, agent_id, resolved, compact_results, disclosure_level,
                    minimal_provenance, next_page_token, audit_ref, compact_metrics
                )
                # Under an extremely small model budget, trim only optional
                # envelope diagnostics. Trust/security evidence in each result
                # remains mandatory and is never removed here.
                compact_pack["budget"] = {
                    "hard": resolved.hard_context_budget,
                    "hard_tokens": resolved.hard_token_budget,
                }
                compact_pack["reduction"] = {"verified_first": True}
                compact_size = resolved.serialized_size(compact_pack)
                compact_tokens = resolved.estimate_tokens(compact_pack)
                if compact_size <= resolved.hard_context_budget and compact_tokens <= resolved.hard_token_budget:
                    try:
                        record_observed_memory_trace(
                            run_id=request_id,
                            results=compact_pack.get("results", []),
                            context_size_bytes=compact_size,
                            estimated_tokens=compact_tokens,
                        )
                    except Exception:
                        pass
                    return compact_pack
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
            removable = next((idx for idx in range(len(safe_results) - 1, -1, -1)
                              if not self._protected_result(safe_results[idx])), None)
            if removable is None:
                raise BudgetExceededError("Protected context cannot fit within hard budget")
            safe_results.pop(removable)

        # Empty result pack is always the last safe representation. If even the
        # envelope exceeds the configured hard budget, fail closed.
        pack = self._build_pack(
            request_id, agent_id, resolved, [], disclosure_level,
            None, next_page_token, audit_ref, reduction_metrics
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
