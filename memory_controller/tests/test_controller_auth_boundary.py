import pytest

from memory_controller.authorizer import Operation, Principal
from memory_controller.controller import MemoryController


class _DenyAuthorizer:
    def __init__(self):
        self.calls = []

    def is_allowed(self, principal, operation):
        self.calls.append((principal, operation))
        return False


def test_check_auth_rejects_non_enum_principal_without_attribute_error():
    authorizer = _DenyAuthorizer()
    controller = MemoryController.__new__(MemoryController)
    controller.authorizer = authorizer

    with pytest.raises(PermissionError, match="Invalid principal"):
        controller._check_auth("unknown", Operation.SEARCH)

    assert authorizer.calls == []


def test_check_auth_still_delegates_valid_principal_to_authorizer():
    authorizer = _DenyAuthorizer()
    controller = MemoryController.__new__(MemoryController)
    controller.authorizer = authorizer

    with pytest.raises(PermissionError, match="ai_agent not allowed to perform search"):
        controller._check_auth(Principal.AI_AGENT, Operation.SEARCH)

    assert authorizer.calls == [(Principal.AI_AGENT, Operation.SEARCH)]
