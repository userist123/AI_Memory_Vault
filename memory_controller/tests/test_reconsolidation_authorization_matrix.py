"""Focused authorization matrix for reconsolidation operations."""

import pytest

from memory_controller.authorizer import DefaultAuthorizer, Operation, Principal


@pytest.mark.parametrize("principal", [Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN])
def test_reconsolidation_challenge_is_explicitly_authorized(principal):
    authorizer = DefaultAuthorizer()

    assert authorizer.is_allowed(principal, Operation.RECONSOLIDATE_CHALLENGE)


@pytest.mark.parametrize("principal", [Principal.HUMAN, Principal.ADMIN])
def test_reconsolidation_resolve_is_human_admin_only(principal):
    authorizer = DefaultAuthorizer()

    assert authorizer.is_allowed(principal, Operation.RECONSOLIDATE_RESOLVE)


def test_ai_agent_cannot_resolve_reconsolidation():
    authorizer = DefaultAuthorizer()

    assert not authorizer.is_allowed(Principal.AI_AGENT, Operation.RECONSOLIDATE_RESOLVE)


@pytest.mark.parametrize("operation", [Operation.RECONSOLIDATE_CHALLENGE, Operation.RECONSOLIDATE_RESOLVE])
def test_unknown_principal_like_value_is_fail_closed(operation):
    authorizer = DefaultAuthorizer()

    class UnknownPrincipal:
        value = "unknown"

    assert not authorizer.is_allowed(UnknownPrincipal(), operation)
