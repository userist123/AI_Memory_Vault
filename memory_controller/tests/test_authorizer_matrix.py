"""Exhaustive regression matrix for the default authorization boundary."""

import pytest

from memory_controller.authorizer import DefaultAuthorizer, Operation, Principal


EXPECTED = {
    Operation.READ: {Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN},
    Operation.SEARCH: {Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN},
    Operation.PROPOSE: {Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN},
    Operation.REVIEW: {Principal.HUMAN, Principal.ADMIN},
    Operation.PROMOTE: {Principal.HUMAN, Principal.ADMIN},
    Operation.ARCHIVE: {Principal.HUMAN, Principal.ADMIN},
    Operation.UPDATE: {Principal.HUMAN, Principal.ADMIN, Principal.AI_AGENT},
    Operation.SUPERSEDE: {Principal.HUMAN, Principal.ADMIN, Principal.AI_AGENT},
    Operation.ATTEST: {Principal.HUMAN, Principal.ADMIN},
    Operation.RECONSOLIDATE_CHALLENGE: {Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN},
    Operation.RECONSOLIDATE_RESOLVE: {Principal.HUMAN, Principal.ADMIN},
}


@pytest.mark.parametrize("operation,allowed", EXPECTED.items())
def test_default_authorizer_matches_canonical_matrix(operation, allowed):
    authorizer = DefaultAuthorizer()
    for principal in Principal:
        assert authorizer.is_allowed(principal, operation) is (principal in allowed)


def test_default_authorizer_fails_closed_for_unknown_operation_and_principal_values():
    authorizer = DefaultAuthorizer()
    assert authorizer.is_allowed("unknown-principal", Operation.SEARCH) is False
    assert authorizer.is_allowed(Principal.AI_AGENT, "unknown-operation") is False


def test_default_authorizer_does_not_mutate_policy_matrix():
    authorizer = DefaultAuthorizer()
    snapshot = {operation: set(principals) for operation, principals in authorizer._policy.items()}
    assert authorizer.is_allowed(Principal.HUMAN, Operation.READ)
    assert {operation: set(principals) for operation, principals in authorizer._policy.items()} == snapshot
