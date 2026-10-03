from security.supply_chain_policy import (
    ComponentDisposition,
    ComponentProvenance,
    ComponentType,
    SoftwareAISupplyChainPolicy,
)


def component(country="Germany", signer="trusted-key", kind=ComponentType.AI_SKILL, digest="a" * 64):
    return ComponentProvenance(
        "component-1", kind, "https://example.invalid/component",
        "Example", country, country, signer, digest
    )


def test_blacklisted_origin_blocks_every_component_class():
    policy = SoftwareAISupplyChainPolicy(
        blocked_countries={"Russia", "China", "India", "North Korea"}
    )
    for kind in ComponentType:
        assert policy.evaluate(component("Russia", kind=kind)).disposition is ComponentDisposition.BLOCKED


def test_allowed_origin_is_not_trusted_without_signer_and_hash():
    policy = SoftwareAISupplyChainPolicy()
    assert policy.evaluate(component(signer=None)).disposition is ComponentDisposition.BLOCKED
    assert policy.evaluate(component(digest=None)).disposition is ComponentDisposition.BLOCKED


def test_non_allowlisted_signer_requires_review():
    policy = SoftwareAISupplyChainPolicy(approved_signers={"trusted-key"})
    assert policy.evaluate(component(signer="new-key")).disposition is ComponentDisposition.REVIEW
