"""Canonical data-plane routing for memory-to-model handoff.

The router is deliberately an egress gate: all context-producing controller
entry points must pass their final Context Pack through this seam before it can
be returned to an agent/model-facing caller.
"""

from __future__ import annotations

from copy import deepcopy
from typing import Any, Callable, Dict, Mapping

from retrieval.context.budget import ContextBudget
from security.verified_reduction import TRUSTED_STATUSES, content_withheld_from


class DataRouteViolation(RuntimeError):
    """Raised when a model-facing context bypasses a mandatory route stage."""


class MemoryDataEgressGate:
    """Enforce the final verified-memory -> model egress boundary.

    This component does not choose a retrieval path. It validates the final
    context pack immediately before model-facing return.
    """

    STAGES = (
        "source",
        "verification",
        "provenance",
        "security_boundary",
        "token_economy",
        "model_egress",
    )

    def route_to_model(
        self,
        pack: Dict[str, Any],
        *,
        source: str,
        principal: str,
    ) -> Dict[str, Any]:
        if not isinstance(pack, dict):
            raise DataRouteViolation("source: context pack must be a mapping")

        results = pack.get("results")
        if not isinstance(results, list):
            raise DataRouteViolation("source: context pack results are missing")

        for index, result in enumerate(results):
            if not isinstance(result, dict):
                raise DataRouteViolation(f"verification: result {index} is not a mapping")

            verification = result.get("verification")
            if isinstance(verification, Mapping):
                status = str(
                    verification.get("status", verification.get("state", ""))
                ).upper()
            else:
                status = str(verification or "").upper()
            quarantined_view = (
                str(principal) in {"human", "ai_agent", "admin"}
                and result.get("trust_state") == "UNVERIFIED_QUARANTINED"
            )
            if status not in TRUSTED_STATUSES and not quarantined_view:
                raise DataRouteViolation(
                    f"verification: result {index} is not trusted ({status or 'missing'})"
                )

            provenance = result.get("provenance")
            if not isinstance(provenance, dict):
                raise DataRouteViolation(
                    f"provenance: result {index} has no provenance record"
                )

        budget = pack.get("budget")
        reduction = pack.get("reduction")
        if not isinstance(budget, dict):
            raise DataRouteViolation("security_boundary: final budget envelope is missing")
        # An empty result envelope is a legitimate fail-closed outcome after
        # budget filtering. It still needs a valid budget, but it has no
        # per-result reduction metrics to validate.
        if not isinstance(reduction, dict):
            if results:
                raise DataRouteViolation("token_economy: reduction metrics are missing")
            reduction = {"verified_first": True, "items_reduced": 0, "items_rejected_unverified": 0}
            routed_reduction = True
        else:
            routed_reduction = False

        hard_tokens = budget.get("hard_tokens")
        if hard_tokens is None:
            raise DataRouteViolation("security_boundary: hard token budget is missing")

        routed = deepcopy(pack)
        if routed_reduction:
            routed["reduction"] = reduction

        routed["data_route"] = {
            "source": str(source),
            "principal": str(principal),
            "stages": [
                "source",
                "verification",
                "provenance",
                "security_boundary",
                "token_economy",
                "model_egress",
            ],
            "model_egress": not any(
                isinstance(result, dict) and result.get("trust_state") == "UNVERIFIED_QUARANTINED"
                for result in results
            ),
            "quarantine": any(
                isinstance(result, dict) and result.get("trust_state") == "UNVERIFIED_QUARANTINED"
                for result in results
            ),
        }

        # Model-facing egress isolation, applied again at the final gate with the same
        # rule the context pack builder uses (one shared predicate, one owner set):
        # non-owner callers never receive the body of an unverified REVIEW candidate or
        # of a note flagged quarantined. ACTIVE notes keep their content whatever their
        # verification label.
        for result in routed.get("results", []):
            if not isinstance(result, dict):
                continue
            unverified = result.get("trust_state") == "UNVERIFIED_QUARANTINED"
            if content_withheld_from(principal, result, unverified=unverified):
                result["content"] = ""
                result["snippet"] = ""
                result["model_egress"] = False

        try:
            hard_tokens = int(budget["hard_tokens"])
            # Retrieval traces and transport pagination tokens are metadata, not model prompt input.
            # They remain available to callers without consuming the model context
            # budget already satisfied by the producer.
            model_input = {
                key: value
                for key, value in routed.items()
                if key not in {"candidate_trace", "retrieval_trace", "data_route", "nextPageToken", "next_page_token"}
            }
            final_tokens = ContextBudget({"hard_tokens": hard_tokens}).estimate_tokens(model_input)
        except (KeyError, TypeError, ValueError) as exc:
            raise DataRouteViolation(
                "security_boundary: invalid final token budget envelope"
            ) from exc
        if final_tokens > hard_tokens:
            raise DataRouteViolation(
                f"security_boundary: final routed context exceeds hard token budget "
                f"({final_tokens}>{hard_tokens})"
            )
        routed["data_route"]["final_model_input_tokens"] = final_tokens
        return routed


class MemoryDataRouter:
    """Canonical memory-data router for model-facing context production.

    The router owns route registration and dispatch. Registered producers remain
    responsible for retrieval, ranking and context construction. Every producer
    result is passed through the dedicated egress gate before it can leave the
    data plane.
    """

    ROUTES = frozenset({
        "search",
        "read",
        "cognitive_read",
        "financial_search",
    })

    def __init__(self, egress_gate: MemoryDataEgressGate | None = None) -> None:
        self.egress_gate = egress_gate or MemoryDataEgressGate()
        self._handlers: Dict[str, Callable[..., Dict[str, Any]]] = {}

    def register(
        self,
        source: str,
        handler: Callable[..., Dict[str, Any]],
    ) -> None:
        route = str(source).strip()
        if route not in self.ROUTES:
            raise DataRouteViolation(f"source: unsupported memory data route '{route}'")
        if not callable(handler):
            raise DataRouteViolation(f"source: route '{route}' has no callable handler")
        if route in self._handlers:
            raise DataRouteViolation(f"source: route '{route}' is already registered")
        self._handlers[route] = handler

    def dispatch(
        self,
        *,
        source: str,
        principal: str,
        handler_kwargs: Mapping[str, Any] | None = None,
    ) -> Dict[str, Any]:
        route = str(source).strip()
        if route not in self.ROUTES:
            raise DataRouteViolation(f"source: unsupported memory data route '{route}'")
        handler = self._handlers.get(route)
        if handler is None:
            raise DataRouteViolation(f"source: route '{route}' has no registered handler")

        kwargs = dict(handler_kwargs or {})
        pack = handler(**kwargs)
        if not isinstance(pack, dict):
            raise DataRouteViolation(
                f"source: route '{route}' handler did not return a context pack"
            )
        return self.egress_gate.route_to_model(
            pack,
            source=route,
            principal=str(principal),
        )
