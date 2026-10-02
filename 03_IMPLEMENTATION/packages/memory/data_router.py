"""Canonical data-plane routing for memory-to-model handoff.

The router is deliberately an egress gate: all context-producing controller
entry points must pass their final Context Pack through this seam before it can
be returned to an agent/model-facing caller.
"""

from __future__ import annotations

from copy import deepcopy
from typing import Any, Dict


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
            if not isinstance(verification, dict):
                raise DataRouteViolation(
                    f"verification: result {index} has no structured verification record"
                )

            status = str(
                verification.get("status", verification.get("state", ""))
            ).upper()
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
        return routed
