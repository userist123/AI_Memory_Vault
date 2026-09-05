"""Exhaustive transition matrix checks for lifecycle_policy.

These tests intentionally exercise the policy as a pure, fail-closed decision
boundary. They do not depend on MemoryController storage or authorization state.
"""

import pytest

from memory_controller.lifecycle_policy import (
    Mutation,
    allowed_targets,
    is_transition_allowed,
)


ALL_LIFECYCLES = {
    "RAW",
    "CLASSIFIED",
    "NORMALIZED",
    "REVIEW",
    "VERIFIED",
    "ACTIVE",
    "RECONSOLIDATING",
    "SUPERSEDED",
    "ARCHIVED",
}


@pytest.mark.parametrize(
    "mutation,source,targets",
    [
        (Mutation.REVIEW, "RAW", {"REVIEW"}),
        (Mutation.REVIEW, "CLASSIFIED", {"REVIEW"}),
        (Mutation.REVIEW, "NORMALIZED", {"REVIEW"}),
        (Mutation.REVIEW, "REVIEW", {"REVIEW"}),
        (Mutation.PROMOTE, "REVIEW", {"ACTIVE"}),
        (Mutation.RECONSOLIDATE_CHALLENGE, "ACTIVE", {"RECONSOLIDATING"}),
        (Mutation.RECONSOLIDATE_CHALLENGE, "VERIFIED", {"RECONSOLIDATING"}),
        (Mutation.RECONSOLIDATE_RESOLVE, "RECONSOLIDATING", {"REVIEW"}),
        (Mutation.ARCHIVE, "REVIEW", {"ARCHIVED"}),
        (Mutation.ARCHIVE, "ACTIVE", {"ARCHIVED"}),
        (Mutation.SUPERSEDE, "ACTIVE", {"SUPERSEDED"}),
    ],
)
def test_allowed_targets_matches_canonical_matrix(mutation, source, targets):
    assert set(allowed_targets(source, mutation=mutation)) == targets
    for target in targets:
        if mutation is Mutation.PROMOTE:
            assert is_transition_allowed(source, target, mutation=mutation, verification="verified")
        else:
            assert is_transition_allowed(source, target, mutation=mutation)


@pytest.mark.parametrize("mutation", list(Mutation))
@pytest.mark.parametrize("source", sorted(ALL_LIFECYCLES))
def test_every_policy_entry_is_subset_of_known_lifecycles(mutation, source):
    assert set(allowed_targets(source, mutation=mutation)) <= ALL_LIFECYCLES


@pytest.mark.parametrize("mutation", list(Mutation))
@pytest.mark.parametrize("source", sorted(ALL_LIFECYCLES))
def test_all_non_identity_transitions_not_explicitly_allowed_fail_closed(
    mutation, source
):
    allowed = set(allowed_targets(source, mutation=mutation))
    for target in sorted(ALL_LIFECYCLES - {source} - allowed):
        assert not is_transition_allowed(source, target, mutation=mutation, verification="verified")


@pytest.mark.parametrize("mutation,source,target", [
    ("unknown", "ACTIVE", "REVIEW"),
    (Mutation.PROMOTE, "UNKNOWN", "ACTIVE"),
    (Mutation.ARCHIVE, "", "ARCHIVED"),
    (Mutation.SUPERSEDE, None, "SUPERSEDED"),
])
def test_unknown_mutation_or_source_fails_closed(mutation, source, target):
    assert allowed_targets(source, mutation=mutation) == frozenset()
    assert not is_transition_allowed(source, target, mutation=mutation, verification="verified")


def test_unknown_target_fails_closed_without_corrupting_known_policy():
    assert set(allowed_targets("REVIEW", mutation=Mutation.PROMOTE)) == {"ACTIVE"}
    assert not is_transition_allowed("REVIEW", "UNKNOWN", mutation=Mutation.PROMOTE, verification="verified")


@pytest.mark.parametrize(
    "source,target",
    [
        ("ACTIVE", "ACTIVE"),
        ("RECONSOLIDATING", "RECONSOLIDATING"),
        ("ARCHIVED", "ACTIVE"),
        ("SUPERSEDED", "ACTIVE"),
    ],
)
def test_no_implicit_reactivation_or_noop_transition(source, target):
    assert not is_transition_allowed(source, target, mutation=Mutation.PROMOTE, verification="verified")
    assert not is_transition_allowed(source, target, mutation=Mutation.RECONSOLIDATE_RESOLVE)


def test_promotion_policy_does_not_conflate_verification_with_lifecycle():
    """The policy permits REVIEW->ACTIVE only when the verification precondition is met."""
    assert is_transition_allowed("REVIEW", "ACTIVE", mutation=Mutation.PROMOTE, verification="verified")
    assert not is_transition_allowed("REVIEW", "ACTIVE", mutation=Mutation.PROMOTE, verification="unverified")
    assert not is_transition_allowed("ACTIVE", "VERIFIED", mutation=Mutation.PROMOTE, verification="verified")
