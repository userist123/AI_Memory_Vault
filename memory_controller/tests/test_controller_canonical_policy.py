import pytest

from memory_controller import controller as controller_module
from memory_controller.authorizer import Operation, Principal
from memory_controller.controller import Lifecycle, MemoryController
from memory_controller.lifecycle_policy import Mutation


class _Storage:
    def __init__(self, old_note):
        self.old_note = old_note

    def get(self, note_id):
        return self.old_note


class _DenyAuthorizer:
    def is_allowed(self, principal, operation):
        return False


@pytest.fixture
def validation_bypass(monkeypatch):
    monkeypatch.setattr(controller_module, "validate_frontmatter", lambda note: None)
    monkeypatch.setattr(controller_module, "validate_provenance", lambda provenance: None)


@pytest.mark.parametrize(
    "source,target,mutation,verification",
    [
        ("RAW", "CLASSIFIED", Mutation.CLASSIFY, "unverified"),
        ("CLASSIFIED", "NORMALIZED", Mutation.NORMALIZE, "unverified"),
        ("NORMALIZED", "REVIEW", Mutation.REVIEW, "unverified"),
        ("REVIEW", "VERIFIED", Mutation.VERIFY, "unverified"),
        ("REVIEW", "ACTIVE", Mutation.PROMOTE, "verified"),
        ("VERIFIED", "ACTIVE", Mutation.PROMOTE, "verified"),
        ("ACTIVE", "RECONSOLIDATING", Mutation.RECONSOLIDATE_CHALLENGE, "verified"),
        ("VERIFIED", "RECONSOLIDATING", Mutation.RECONSOLIDATE_CHALLENGE, "verified"),
        ("RECONSOLIDATING", "REVIEW", Mutation.RECONSOLIDATE_RESOLVE, "unverified"),
        ("REVIEW", "ARCHIVED", Mutation.ARCHIVE, "unverified"),
        ("ACTIVE", "ARCHIVED", Mutation.ARCHIVE, "verified"),
        ("ACTIVE", "SUPERSEDED", Mutation.SUPERSEDE, "verified"),
    ],
)
def test_validate_note_routes_lifecycle_changes_through_canonical_policy(
    validation_bypass, monkeypatch, source, target, mutation, verification
):
    calls = []

    def evaluate(old, new, *, mutation, verification=None):
        calls.append((old, new, mutation, verification))
        return True

    monkeypatch.setattr(controller_module, "evaluate_lifecycle_mutation", evaluate)

    controller = MemoryController.__new__(MemoryController)
    controller.storage = _Storage({"id": "n1", "lifecycle": source})

    note = {
        "id": "n1",
        "lifecycle": target,
        "verification": verification,
        "provenance": {},
    }

    controller._validate_note(note)

    assert calls == [(source, target, mutation, verification)]


def test_validate_note_fails_closed_for_transition_without_canonical_mutation(
    validation_bypass
):
    controller = MemoryController.__new__(MemoryController)
    controller.storage = _Storage({"id": "n1", "lifecycle": Lifecycle.RAW.value})

    note = {
        "id": "n1",
        "lifecycle": Lifecycle.ACTIVE.value,
        "verification": "verified",
        "provenance": {},
    }

    with pytest.raises(ValueError, match="Invalid transition"):
        controller._validate_note(note)


def test_search_authorizes_before_any_retrieval_work():
    controller = MemoryController.__new__(MemoryController)
    controller.authorizer = _DenyAuthorizer()

    with pytest.raises(PermissionError, match="not allowed to perform search"):
        controller.search(Principal.AI_AGENT, "anything")
