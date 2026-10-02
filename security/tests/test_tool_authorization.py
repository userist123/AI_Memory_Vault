from security.tool_authorization import ToolRequest, authorize_tool
from security.trust_gate import TrustDecision, TrustState


def test_untrusted_external_content_cannot_authorize_tool():
    decision = TrustDecision(TrustState.UNTRUSTED, ("unverified",))
    result = authorize_tool(
        decision,
        ToolRequest("shell.exec", side_effect=True, requested_by="external_content"),
        human_approved=True,
    )
    assert not result.allowed
    assert result.reason == "external_content_not_authority"


def test_review_requires_explicit_human_approval():
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)
    request = ToolRequest("network.request", side_effect=True, requested_by="external_content")
    denied = authorize_tool(decision, request, human_approved=False)
    assert not denied.allowed
    assert denied.reason == "human_approval_required"

    approved = authorize_tool(decision, request, human_approved=True)
    assert approved.allowed


def test_blocked_content_can_never_be_approved():
    decision = TrustDecision(TrustState.BLOCKED, ("exfiltration",))
    result = authorize_tool(
        decision,
        ToolRequest("shell.exec", side_effect=True, requested_by="external_content"),
        human_approved=True,
    )
    assert not result.allowed
    assert result.reason == "blocked_content"


def test_trusted_read_only_tool_can_execute():
    decision = TrustDecision(TrustState.TRUSTED)
    result = authorize_tool(
        decision,
        ToolRequest("artifact.read", side_effect=False, requested_by="policy"),
    )
    assert result.allowed


def test_external_content_cannot_be_authorization_source():
    decision = TrustDecision(TrustState.TRUSTED)
    result = authorize_tool(
        decision,
        ToolRequest("artifact.read", side_effect=False, requested_by="external_content"),
    )
    assert not result.allowed
    assert result.reason == "external_content_not_authority"


def test_data_export_requires_human_approval_even_when_trusted():
    decision = TrustDecision(TrustState.TRUSTED)
    request = ToolRequest("file.export", data_export=True, requested_by="policy")
    denied = authorize_tool(decision, request, human_approved=False)
    assert not denied.allowed
    assert denied.reason == "human_approval_required"

    approved = authorize_tool(decision, request, human_approved=True)
    assert approved.allowed
