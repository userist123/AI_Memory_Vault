from security.catalog_provenance_policy import (
    CatalogDisposition,
    CatalogProvenance,
    CatalogProvenancePolicy,
)


def provenance(country="US", signer="trusted-key"):
    return CatalogProvenance(
        catalog_id="cat-1",
        source_url="https://example.invalid/catalog.json",
        publisher="Example",
        country=country,
        jurisdiction=country,
        signer_key_id=signer,
        package_sha256="a" * 64,
    )


def test_explicitly_blocked_country_cannot_install():
    policy = CatalogProvenancePolicy(
        blocked_countries={"Russia", "China", "India", "North Korea"},
    )

    for country in ("Russia", "China", "India", "North Korea"):
        assert policy.evaluate(provenance(country)).disposition is CatalogDisposition.BLOCKED


def test_non_blocked_country_still_requires_other_controls():
    policy = CatalogProvenancePolicy(
        blocked_countries={"Russia", "China", "India", "North Korea"},
        approved_signers={"trusted-key"},
    )
    assert policy.evaluate(provenance("Germany")).disposition is CatalogDisposition.VERIFIED


def test_unknown_signer_is_review_not_automatic_trust():
    policy = CatalogProvenancePolicy(approved_signers={"trusted-key"})
    decision = policy.evaluate(provenance("US", signer="new-key"))
    assert decision.disposition is CatalogDisposition.REVIEW


def test_missing_signer_is_blocked():
    policy = CatalogProvenancePolicy()
    assert policy.evaluate(provenance("US", signer="")).disposition is CatalogDisposition.BLOCKED
