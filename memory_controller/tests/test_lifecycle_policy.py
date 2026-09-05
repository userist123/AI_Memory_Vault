import pytest

from memory_controller.controller import Lifecycle
from memory_controller.lifecycle_policy import Mutation, allowed_targets, is_transition_allowed


@pytest.mark.parametrize(
    "mutation,old,new",
    [
        (Mutation.REVIEW, Lifecycle.RAW, Lifecycle.REVIEW),
        (Mutation.REVIEW, Lifecycle.CLASSIFIED, Lifecycle.REVIEW),
        (Mutation.REVIEW, Lifecycle.NORMALIZED, Lifecycle.REVIEW),
        (Mutation.REVIEW, Lifecycle.REVIEW, Lifecycle.REVIEW),
        (Mutation.PROMOTE, Lifecycle.REVIEW, Lifecycle.ACTIVE),
        (Mutation.RECONSOLIDATE_CHALLENGE, Lifecycle.ACTIVE, Lifecycle.RECONSOLIDATING),
        (Mutation.RECONSOLIDATE_CHALLENGE, Lifecycle.VERIFIED, Lifecycle.RECONSOLIDATING),
        (Mutation.RECONSOLIDATE_RESOLVE, Lifecycle.RECONSOLIDATING, Lifecycle.REVIEW),
        (Mutation.ARCHIVE, Lifecycle.REVIEW, Lifecycle.ARCHIVED),
        (Mutation.ARCHIVE, Lifecycle.ACTIVE, Lifecycle.ARCHIVED),
        (Mutation.SUPERSEDE, Lifecycle.ACTIVE, Lifecycle.SUPERSEDED),
    ],
)
def test_supported_mutation_transition_is_allowed(mutation, old, new):
    verification = "verified" if mutation is Mutation.PROMOTE else None
    assert is_transition_allowed(old, new, mutation=mutation, verification=verification)


@pytest.mark.parametrize(
    "mutation,old,new",
    [
        (Mutation.PROMOTE, Lifecycle.VERIFIED, Lifecycle.ACTIVE),
        (Mutation.REVIEW, Lifecycle.REVIEW, Lifecycle.ACTIVE),
        (Mutation.RECONSOLIDATE_RESOLVE, Lifecycle.RECONSOLIDATING, Lifecycle.ACTIVE),
        (Mutation.ARCHIVE, Lifecycle.VERIFIED, Lifecycle.ARCHIVED),
        (Mutation.SUPERSEDE, Lifecycle.REVIEW, Lifecycle.SUPERSEDED),
        (Mutation.REVIEW, Lifecycle.ACTIVE, Lifecycle.REVIEW),
    ],
)
def test_unsupported_transition_is_denied(mutation, old, new):
    assert not is_transition_allowed(old, new, mutation=mutation, verification="verified")


def test_promotion_requires_verified_state():
    assert not is_transition_allowed(
        Lifecycle.REVIEW,
        Lifecycle.ACTIVE,
        mutation=Mutation.PROMOTE,
        verification="partially_verified",
    )
    assert is_transition_allowed(
        Lifecycle.REVIEW,
        Lifecycle.ACTIVE,
        mutation=Mutation.PROMOTE,
        verification="verified",
    )


def test_unknown_values_fail_closed():
    assert not is_transition_allowed("NOT_A_LIFECYCLE", Lifecycle.ACTIVE, mutation=Mutation.PROMOTE, verification="verified")
    assert not is_transition_allowed(Lifecycle.REVIEW, Lifecycle.ACTIVE, mutation="NOT_A_MUTATION", verification="verified")


def test_allowed_targets_match_policy():
    assert allowed_targets(Lifecycle.ACTIVE, mutation=Mutation.ARCHIVE) == frozenset({Lifecycle.ARCHIVED})
    assert allowed_targets(Lifecycle.REVIEW, mutation=Mutation.PROMOTE) == frozenset({Lifecycle.ACTIVE})
    assert allowed_targets(Lifecycle.RECONSOLIDATING, mutation=Mutation.RECONSOLIDATE_RESOLVE) == frozenset({Lifecycle.REVIEW})
