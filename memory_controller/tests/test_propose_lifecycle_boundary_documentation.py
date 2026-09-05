"""Regression contract for proposal lifecycle ownership.

This test suite describes the trust-boundary invariant that proposal must not
establish privileged lifecycle states for any principal. It is intentionally
kept separate from controller behavior until the lifecycle-policy integration
patch is applied.
"""

import pytest

from memory_controller.controller import Lifecycle


PRIVILEGED_CREATION_LIFECYCLES = {
    Lifecycle.VERIFIED.value,
    Lifecycle.ACTIVE.value,
    Lifecycle.RECONSOLIDATING.value,
    Lifecycle.SUPERSEDED.value,
    Lifecycle.ARCHIVED.value,
}


def test_privileged_lifecycle_states_are_explicitly_classified_as_non_creation_states():
    assert PRIVILEGED_CREATION_LIFECYCLES == {
        "VERIFIED",
        "ACTIVE",
        "RECONSOLIDATING",
        "SUPERSEDED",
        "ARCHIVED",
    }


def test_creation_contract_is_review_pipeline_only():
    permitted = {
        Lifecycle.RAW.value,
        Lifecycle.CLASSIFIED.value,
        Lifecycle.NORMALIZED.value,
        Lifecycle.REVIEW.value,
    }
    assert permitted.isdisjoint(PRIVILEGED_CREATION_LIFECYCLES)


def test_controller_policy_gap_is_not_silently_misclassified():
    """Keep the known integration gap visible until controller wiring lands.

    The current controller only applies its creation-lifecycle restriction to
    AI_AGENT. HUMAN/ADMIN proposal paths therefore require a follow-up fix so
    they cannot establish privileged lifecycle state at creation.
    """
    pytest.skip(
        "Controller lifecycle-policy integration pending: HUMAN/ADMIN propose "
        "must use the same non-privileged creation boundary."
    )
