"""Provenance policy for AI security update catalogs."""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


class CatalogDisposition(str, Enum):
    BLOCKED = "BLOCKED"
    REVIEW = "REVIEW"
    VERIFIED = "VERIFIED"


@dataclass(frozen=True)
class CatalogProvenance:
    catalog_id: str
    source_url: str
    publisher: str
    country: str | None
    jurisdiction: str | None
    signer_key_id: str
    package_sha256: str


@dataclass(frozen=True)
class ProvenanceDecision:
    disposition: CatalogDisposition
    reason: str


class CatalogProvenancePolicy:
    """Country/jurisdiction is an explicit deployment policy, not proof of malware."""

    def __init__(
        self,
        *,
        blocked_countries: set[str] | frozenset[str] = frozenset(),
        blocked_jurisdictions: set[str] | frozenset[str] = frozenset(),
        approved_signers: set[str] | frozenset[str] = frozenset(),
    ) -> None:
        self.blocked_countries = frozenset(x.casefold() for x in blocked_countries)
        self.blocked_jurisdictions = frozenset(x.casefold() for x in blocked_jurisdictions)
        self.approved_signers = frozenset(approved_signers)

    def evaluate(self, provenance: CatalogProvenance) -> ProvenanceDecision:
        country = (provenance.country or "").casefold()
        jurisdiction = (provenance.jurisdiction or "").casefold()

        if country in self.blocked_countries:
            return ProvenanceDecision(
                CatalogDisposition.BLOCKED,
                "catalog_country_blocked_by_local_security_policy",
            )

        if jurisdiction in self.blocked_jurisdictions:
            return ProvenanceDecision(
                CatalogDisposition.BLOCKED,
                "catalog_jurisdiction_blocked_by_local_security_policy",
            )

        if not provenance.signer_key_id:
            return ProvenanceDecision(
                CatalogDisposition.BLOCKED,
                "catalog_missing_signer",
            )

        if self.approved_signers and provenance.signer_key_id not in self.approved_signers:
            return ProvenanceDecision(
                CatalogDisposition.REVIEW,
                "catalog_signer_not_allowlisted",
            )

        if not provenance.package_sha256:
            return ProvenanceDecision(
                CatalogDisposition.BLOCKED,
                "catalog_missing_package_hash",
            )

        return ProvenanceDecision(
            CatalogDisposition.VERIFIED,
            "catalog_provenance_policy_satisfied",
        )
