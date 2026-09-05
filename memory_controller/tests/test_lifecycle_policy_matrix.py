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
    assert set(allowed_targets(mutation, source)) == targets
    for target in targets:
        assert is_transition_allowed(mutation, source, target)


@pytest.mark.parametrize("mutation", list(Mutation))
@pytest.mark.parametrize("source", sorted(ALL_LIFECYCLES))
def test_every_policy_entry_is_subset_of_known_lifecycles(mutation, source):
    assert set(allowed_targets(mutation, source)) <= ALL_LIFECYCLES


@pytest.mark.parametrize("mutation", list(Mutation))
@pytest.mark.parametrize("source", sorted(ALL_LIFECYCLES))
def test_all_non_identity_transitions_not_explicitly_allowed_fail_closed(
    mutation, source
):
    allowed = set(allowed_targets(mutation, source))
    for target in sorted(ALL_LIFECYCLES - {source} - allowed):
        assert not is_transition_allowed(mutation, source, target)


@pytest.mark.parametrize("mutation,source,target", [
    ("unknown", "ACTIVE", "REVIEW"),
    (Mutation.PROMOTE, "UNKNOWN", "ACTIVE"),
    (Mutation.ARCHIVE, "", "ARCHIVED"),
    (Mutation.SUPERSEDE, None, "SUPERSEDED"),
])
def test_unknown_mutation_or_source_fails_closed(mutation, source, target):
    assert allowed_targets(mutation, source) == []
    assert not is_transition_allowed(mutation, source, target)


def test_unknown_target_fails_closed_without_corrupting_known_policy():
    assert set(allowed_targets(Mutation.PROMOTE, "REVIEW")) == {"ACTIVE"}
    assert not is_transition_allowed(Mutation.PROMOTE, "REVIEW", "UNKNOWN")


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
    assert not is_transition_allowed(Mutation.PROMOTE, source, target)
    assert not is_transition_allowed(Mutation.RECONSOLIDATE_RESOLVE, source, target)


def test_promotion_policy_does_not_conflate_verification_with_lifecycle():
    """The policy permits REVIEW->ACTIVE structurally; verification remains a
    separate runtime precondition enforced by promotion/attestation logic.
    """
    assert is_transition_allowed(Mutation.PROMOTE, "REVIEW", "ACTIVE")
    assert not is_transition_allowed(Mutation.PROMOTE, "ACTIVE", "VERIFIED")
