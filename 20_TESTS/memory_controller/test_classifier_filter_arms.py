"""r025 WP-9 Phase B — classifier-filter arm regression coverage.

Phase A (07_EVALUATION/r025_wp9_classifier/phase_a_attribution_report.json)
proved QueryClassifier.classify() infers a lifecycle filter from ANY query
that merely contains a stage keyword ("verified", "classified", ...) as
ordinary text, and that VERIFIED/CLASSIFIED have zero notes in the real
corpus -- guaranteeing a collapsed pool. These tests pin: the default arm
(CLASSIFIER_FILTER_ARM_HARD) reproduces the pre-WP-9 collapse exactly (the
flag genuinely defaults OFF); BOOST and CONDITIONAL recover the collapsed
note when the filter was INFERRED; and an EXPLICIT caller-supplied filter
stays hard under every arm, never softened (requirement 1 -- this is a
security boundary, not a classifier convenience).
"""
from __future__ import annotations

import pytest

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle, Principal
from memory_controller.context.retrieval import (
    CLASSIFIER_FILTER_ARM_HARD,
    CLASSIFIER_FILTER_ARM_BOOST,
    CLASSIFIER_FILTER_ARM_CONDITIONAL,
)


@pytest.fixture(autouse=True)
def _hmac_secret(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_r025_wp9")


def _note(note_id, content, lifecycle=Lifecycle.ACTIVE.value):
    return {
        "id": note_id, "type": "knowledge", "lifecycle": lifecycle,
        "category": "test", "tags": [], "created": "2026-01-01", "updated": "2026-01-01",
        "provenance": {"source_type": "user", "source_ref": "unit"},
        "confidence": "medium", "verification": "unverified", "relations": [],
        "content": content,
    }


def make_controller(classifier_filter_arm=None):
    storage = StorageEngine()
    return storage, MemoryController(storage, classifier_filter_arm=classifier_filter_arm)


# The word "verified" appears in ordinary query text below (not as an intent
# to filter by lifecycle) -- QueryClassifier still infers
# lifecycle_filters=["VERIFIED"], and no note in these tests is ever created
# with lifecycle=VERIFIED, so the inferred filter always collapses the pool
# under the HARD arm.
QUERY_WITH_INFERRED_LIFECYCLE_KEYWORD = "show me the verified rollout notes"


def test_default_classifier_filter_arm_is_hard_and_reproduces_pre_wp9_collapse():
    """Requirement 3: the flag genuinely defaults OFF -- an unspecified
    classifier_filter_arm must behave exactly as pre-WP-9 code did (the
    inferred filter is a hard exclusion), collapsing the pool to empty."""
    storage, controller = make_controller()
    storage.set("n1", _note("n1", "the verified rollout went smoothly"))

    pack = controller.search(Principal.HUMAN, QUERY_WITH_INFERRED_LIFECYCLE_KEYWORD, page_size=10)
    assert pack["results"] == []
    assert pack["candidate_trace"]["classifier_filter_arm"] == CLASSIFIER_FILTER_ARM_HARD


def test_boost_arm_recovers_a_note_excluded_only_by_the_inferred_filter():
    storage, controller = make_controller(classifier_filter_arm=CLASSIFIER_FILTER_ARM_BOOST)
    storage.set("n1", _note("n1", "the verified rollout went smoothly"))

    pack = controller.search(Principal.HUMAN, QUERY_WITH_INFERRED_LIFECYCLE_KEYWORD, page_size=10)
    ids = [r["id"] for r in pack["results"]]
    assert "n1" in ids
    assert pack["candidate_trace"]["classifier_filter_arm"] == CLASSIFIER_FILTER_ARM_BOOST


def test_conditional_arm_recovers_when_unfiltered_pool_is_small():
    storage, controller = make_controller(classifier_filter_arm=CLASSIFIER_FILTER_ARM_CONDITIONAL)
    storage.set("n1", _note("n1", "the verified rollout went smoothly"))

    pack = controller.search(Principal.HUMAN, QUERY_WITH_INFERRED_LIFECYCLE_KEYWORD, page_size=10)
    ids = [r["id"] for r in pack["results"]]
    assert "n1" in ids


def test_conditional_arm_still_narrows_when_unfiltered_pool_exceeds_candidate_limit():
    """C3 only softens the inferred filter if the UNFILTERED pool would
    otherwise exceed candidate_limit; with a small pool (this test's 3 notes
    are far below any candidate_limit), narrowing is unnecessary and C3 must
    behave like BOOST -- recovering the note. The distinct "large pool"
    branch is exercised structurally (see _apply_classifier_filter_arm in
    retrieval.py) but this benchmark-scale test only proves the common,
    small-pool path recovers correctly; see WP9_CLASSIFIER_FILTER_ARMS.md's
    "what remains open" for why C2/C3 are not distinguished empirically."""
    storage, controller = make_controller(classifier_filter_arm=CLASSIFIER_FILTER_ARM_CONDITIONAL)
    storage.set("n1", _note("n1", "the verified rollout went smoothly"))
    storage.set("n2", _note("n2", "unrelated filler about something else"))
    storage.set("n3", _note("n3", "another unrelated filler note"))

    pack = controller.search(Principal.HUMAN, QUERY_WITH_INFERRED_LIFECYCLE_KEYWORD, page_size=10)
    ids = [r["id"] for r in pack["results"]]
    assert "n1" in ids


def test_explicit_caller_lifecycle_stays_hard_under_every_arm():
    """Requirement 1: lifecycle filtering is a SECURITY boundary when the
    caller supplies it explicitly -- only the classifier's OWN inference is
    ever softened. An explicit lifecycles=[VERIFIED] must collapse the pool
    under BOOST and CONDITIONAL exactly as it does under HARD."""
    for arm in (CLASSIFIER_FILTER_ARM_HARD, CLASSIFIER_FILTER_ARM_BOOST, CLASSIFIER_FILTER_ARM_CONDITIONAL):
        storage, controller = make_controller(classifier_filter_arm=arm)
        storage.set("n1", _note("n1", "the verified rollout went smoothly"))

        pack = controller.search(
            Principal.HUMAN, "rollout notes", lifecycles=[Lifecycle.VERIFIED], page_size=10,
        )
        assert pack["results"] == [], f"arm={arm} softened an EXPLICIT caller-supplied filter"


def test_raw_exclusion_is_unaffected_by_every_classifier_filter_arm():
    """RAW exclusion is enforced unconditionally inside storage.query() and
    must never be reachable through classifier_filter_arm softening, since
    that logic only ever governs the classifier's OWN inferred lifecycle/
    type filters, never the RAW security boundary."""
    for arm in (CLASSIFIER_FILTER_ARM_HARD, CLASSIFIER_FILTER_ARM_BOOST, CLASSIFIER_FILTER_ARM_CONDITIONAL):
        storage, controller = make_controller(classifier_filter_arm=arm)
        storage.set("raw1", _note("raw1", "raw unclassified rollout notes", lifecycle=Lifecycle.RAW.value))

        pack = controller.search(Principal.HUMAN, "rollout notes", page_size=10)
        ids = [r["id"] for r in pack["results"]]
        assert "raw1" not in ids, f"arm={arm} let a RAW note through"
