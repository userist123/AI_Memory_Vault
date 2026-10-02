from security.trust_gate import ArtifactAssessment, TrustState, assess_artifact, can_execute_instruction

def test_external_content_is_not_authority_without_provenance():
    d = assess_artifact(ArtifactAssessment("https://example.invalid", False, "SAFE"))
    assert d.state is TrustState.UNTRUSTED
    assert not can_execute_instruction(d)

def test_verified_safe_content_can_be_trusted():
    d = assess_artifact(ArtifactAssessment("https://example.invalid", True, "SAFE"))
    assert d.state is TrustState.TRUSTED
    assert can_execute_instruction(d)

def test_override_never_becomes_authority():
    d = assess_artifact(ArtifactAssessment("https://example.invalid", True, "REVIEW", has_active_override=True))
    assert d.state is TrustState.REVIEW
    assert not can_execute_instruction(d)

def test_data_to_network_chain_is_blocked():
    d = assess_artifact(ArtifactAssessment("https://example.invalid", True, "BLOCK", has_active_data_access=True, has_active_network=True))
    assert d.state is TrustState.BLOCKED
    assert not can_execute_instruction(d)

def test_side_effect_requires_human_approval():
    d = assess_artifact(ArtifactAssessment("https://example.invalid", True, "SAFE", requested_side_effect=True))
    assert d.state is TrustState.REVIEW
    assert d.requires_human_approval
