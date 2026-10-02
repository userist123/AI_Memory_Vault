"""Supply-chain provenance policy for software and AI components."""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


class ComponentDisposition(str, Enum):
    BLOCKED = "BLOCKED"
    REVIEW = "REVIEW"
    VERIFIED = "VERIFIED"


class ComponentType(str, Enum):
    SOFTWARE = "software"
    LIBRARY = "library"
    PACKAGE = "package"
    MCP_SERVER = "mcp_server"
    AI_MODEL = "ai_model"
    AI_SKILL = "ai_skill"
    PLUGIN = "plugin"
    CATALOG = "catalog"
    SECURITY_UPDATE = "security_update"
    CONTAINER = "container"
    SCRIPT = "script"
    TOOL = "tool"


@dataclass(frozen=True)
class ComponentProvenance:
    component_id: str
    component_type: ComponentType
    source_url: str
    publisher: str
    country: str | None
    jurisdiction: str | None
    signer_key_id: str | None
    artifact_sha256: str | None


@dataclass(frozen=True)
class ComponentDecision:
    disposition: ComponentDisposition
    reason: str


class SoftwareAISupplyChainPolicy:
    """Fail-closed provenance gate for software and AI supply-chain components.

    Country/jurisdiction is a deployment policy signal, not a technical claim
    that every artifact from a jurisdiction is malicious.
    """

    def __init__(
        self,
        *,
        blocked_countries: set[str] | frozenset[str] = frozenset({"Russia", "China", "India", "North Korea"}),
        blocked_jurisdictions: set[str] | frozenset[str] = frozenset(),
        approved_signers: set[str] | frozenset[str] = frozenset(),
    ) -> None:
        self.blocked_countries = frozenset(x.casefold() for x in blocked_countries)
        self.blocked_jurisdictions = frozenset(
            x.casefold() for x in blocked_jurisdictions
        )
        self.approved_signers = frozenset(approved_signers)

    def evaluate(self, component: ComponentProvenance) -> ComponentDecision:
        country = (component.country or "").casefold()
        jurisdiction = (component.jurisdiction or "").casefold()

        if country in self.blocked_countries:
            return ComponentDecision(
                ComponentDisposition.BLOCKED,
                "component_country_blocked_by_local_security_policy",
            )

        if jurisdiction in self.blocked_jurisdictions:
            return ComponentDecision(
                ComponentDisposition.BLOCKED,
                "component_jurisdiction_blocked_by_local_security_policy",
            )

        if not component.signer_key_id:
            return ComponentDecision(
                ComponentDisposition.BLOCKED,
                "component_missing_signer",
            )

        if self.approved_signers and component.signer_key_id not in self.approved_signers:
            return ComponentDecision(
                ComponentDisposition.REVIEW,
                "component_signer_not_allowlisted",
            )

        if not component.artifact_sha256:
            return ComponentDecision(
                ComponentDisposition.BLOCKED,
                "component_missing_artifact_hash",
            )

        return ComponentDecision(
            ComponentDisposition.VERIFIED,
            "component_supply_chain_policy_satisfied",
        )
