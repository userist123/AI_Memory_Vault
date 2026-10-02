"""Canonical data-plane routing for memory-to-model handoff.

The router is deliberately an egress gate: all context-producing controller
entry points must pass their final Context Pack through this seam before it can
be returned to an agent/model-facing caller.
"""

from __future__ import annotations

from copy import deepcopy
from typing import Any, Dict, Mapping

from .context.budget import ContextBudget


class DataRouteViolation(RuntimeError):
    """Raised when a model-facing context bypasses a mandatory route stage."""


class MemoryDataRouter:
    """Enforce the canonical verified-memory -> model data route."""

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
            if status in {"", "UNVERIFIED", "REJECTED", "BLOCKED", "DENIED"}:
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
        if not isinstance(reduction, dict):
            raise DataRouteViolation("token_economy: reduction metrics are missing")

        hard_tokens = budget.get("hard_tokens")
        if hard_tokens is None:
            raise DataRouteViolation("security_boundary: hard token budget is missing")

        routed = deepcopy(pack)
        route = {
            "source": str(source),
            "principal": str(principal),
            "stages": list(self.STAGES),
            "model_egress": True,
        }
        routed["data_route"] = route

        # Measure the complete model-facing envelope, including route metadata.
        try:
            hard_tokens = int(budget["hard_tokens"])
            final_tokens = ContextBudget({"hard_tokens": hard_tokens}).estimate_tokens(routed)
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
