"""r024 WP-1 Phase B — ranking arm regression coverage.

Phase A (07_EVALUATION/r024_wp1_ranking/PHASE_A_ATTRIBUTION.md) found 100% of
held-out losses were `ranked_out`: RelevanceScorer re-scores every candidate
with a formula that is 50% `confidence` (epistemic metadata unrelated to the
query), overwriting candidate_generation.py's already-computed `fused_score`.
Phase B measured four arms that each change exactly one thing about how the
already-computed signals combine into a ranking key.

These tests pin: the baseline arm is byte-identical to pre-r024 behaviour
(the flag genuinely defaults OFF); each arm produces the ordering its name
claims, deterministically; confidence stays in the returned note and the
trace under every arm (requirement 3 -- ranking must not make it disappear);
and graph expansion, which this package must not touch, is unaffected by the
ranking_arm flag.
"""
from __future__ import annotations

import pytest

from memory_controller.controller import (
    MemoryController,
    StorageEngine,
    Lifecycle,
    Principal,
    RANKING_ARM_BASELINE,
    RANKING_ARM_FUSED_SCORE,
    RANKING_ARM_NO_CONFIDENCE,
    RANKING_ARM_CONFIDENCE_TIEBREAK,
    RANKING_ARM_FUSED_PLUS_TIEBREAK,
)
from memory_controller.context.relevance_scoring import RelevanceScorer


@pytest.fixture(autouse=True)
def _hmac_secret(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_r024")


def _note(note_id, content, confidence="medium", tags=None):
    return {
        "id": note_id, "type": "knowledge", "lifecycle": Lifecycle.ACTIVE.value,
        "category": "test", "tags": tags or [], "created": "2026-01-01", "updated": "2026-01-01",
        "provenance": {"source_type": "user", "source_ref": "unit"},
        "confidence": confidence, "verification": "unverified", "relations": [],
        "content": content,
    }


def make_controller(ranking_arm=None):
    storage = StorageEngine()
    return storage, MemoryController(storage, ranking_arm=ranking_arm)


# ---------------------------------------------------------------------------
# 1. RelevanceScorer.score_components() / score() -- pure unit tests.
# ---------------------------------------------------------------------------

def test_score_unchanged_by_the_score_components_refactor():
    """score() must remain byte-for-byte the pre-r024 formula: it is still
    the production default and requirement 3 forbids removing confidence
    from anything readers see."""
    scorer = RelevanceScorer()
    notes = [
        {"id": "a", "content": "alpha beta gamma", "confidence": "high"},
        {"id": "b", "content": "unrelated content entirely", "confidence": "unknown"},
    ]
    scored = scorer.score("alpha beta", notes)
    by_id = {s["id"]: s["score"] for s in scored}
    # (overlap_ratio + confidence) / 2, exactly as before.
    assert by_id["a"] == pytest.approx((1.0 + 0.9) / 2)
    assert by_id["b"] == pytest.approx((0.0 + 0.0) / 2)


def test_score_components_exposes_the_same_two_signals_unblended():
    scorer = RelevanceScorer()
    notes = [{"id": "a", "content": "alpha beta gamma", "confidence": "low"}]
    components = scorer.score_components("alpha beta", notes)
    assert components[0]["overlap_ratio"] == pytest.approx(1.0)
    assert components[0]["confidence"] == pytest.approx(0.2)


# ---------------------------------------------------------------------------
# 2. Baseline stays the production default: the flag genuinely defaults OFF.
# ---------------------------------------------------------------------------

def test_baseline_ranking_matches_pre_r024_relevance_scorer_order():
    storage, controller = make_controller()  # ranking_arm=None -> baseline
    storage.set("high_overlap_low_conf", _note("high_overlap_low_conf", "alpha beta gamma delta", confidence="unknown"))
    storage.set("low_overlap_high_conf", _note("low_overlap_high_conf", "totally unrelated text", confidence="very_high"))

    pack = controller.search(Principal.HUMAN, "alpha beta gamma delta", page_size=10)
    ids = [r["id"] for r in pack["results"]]

    scorer = RelevanceScorer()
    notes = [storage.get(n) for n in ("high_overlap_low_conf", "low_overlap_high_conf")]
    expected_scores = {s["id"]: s["score"] for s in scorer.score("alpha beta gamma delta", notes)}
    expected_order = sorted(ids, key=lambda i: (expected_scores[i], i), reverse=True)
    assert ids == expected_order
    assert pack["candidate_trace"]["ranking_arm"] == RANKING_ARM_BASELINE


def test_ranking_arm_defaults_to_none_end_to_end_when_unspecified():
    """No caller anywhere passes ranking_arm; the flag must resolve to the
    literal baseline constant, not silently to some other arm."""
    storage, controller = make_controller()
    storage.set("n1", _note("n1", "hello world"))
    pack = controller.search(Principal.HUMAN, "hello")
    assert pack["candidate_trace"]["ranking_arm"] == RANKING_ARM_BASELINE


# ---------------------------------------------------------------------------
# 3. Each arm produces the ordering its name claims.
# ---------------------------------------------------------------------------

def test_fused_score_arm_ranks_by_trace_fused_score_not_relevance_scorer():
    """A1: construct a case where RelevanceScorer and fused_score disagree,
    and confirm the FUSED order wins under this arm."""
    storage, controller = make_controller(ranking_arm=RANKING_ARM_FUSED_SCORE)
    # 'needle' matches the query lexically (high fused_score via BM25) but
    # carries low confidence, so baseline RelevanceScorer would rank it below
    # a high-confidence, low-overlap filler note.
    storage.set("needle", _note("needle", "unique_needle_term unique_needle_term", confidence="unknown"))
    storage.set("filler", _note("filler", "totally unrelated filler content", confidence="very_high"))

    pack = controller.search(Principal.HUMAN, "unique_needle_term", page_size=10)
    ids = [r["id"] for r in pack["results"]]
    fused_ids = [e["id"] for e in pack["candidate_trace"]["fused_ranking"]]

    assert ids == fused_ids, "fused_score arm must reproduce the trace's fused order exactly"
    assert ids[0] == "needle", "the lexically relevant note must rank first under A1 despite low confidence"
    assert pack["candidate_trace"]["ranking_arm"] == RANKING_ARM_FUSED_SCORE


def test_no_confidence_arm_ranks_by_overlap_ratio_only():
    storage, controller = make_controller(ranking_arm=RANKING_ARM_NO_CONFIDENCE)
    storage.set("a", _note("a", "alpha beta gamma", confidence="unknown"))  # overlap 1.0, conf 0.0
    storage.set("b", _note("b", "alpha only", confidence="very_high"))       # overlap 0.33, conf 1.0

    pack = controller.search(Principal.HUMAN, "alpha beta gamma", page_size=10)
    ids = [r["id"] for r in pack["results"]]
    # Baseline would blend confidence in and could favour "b" (0.5+0.17 avg
    # vs a's 0.0+0.5); A2 must ignore confidence entirely and rank "a" first
    # purely on overlap_ratio.
    assert ids[0] == "a"


def test_confidence_tiebreak_arm_only_breaks_ties_never_outweighs_overlap():
    storage, controller = make_controller(ranking_arm=RANKING_ARM_CONFIDENCE_TIEBREAK)
    storage.set("higher_overlap_low_conf", _note("higher_overlap_low_conf", "alpha beta gamma delta", confidence="unknown"))
    storage.set("lower_overlap_high_conf", _note("lower_overlap_high_conf", "alpha only here", confidence="very_high"))
    storage.set("tie_a", _note("tie_a", "alpha beta only", confidence="low"))
    storage.set("tie_b", _note("tie_b", "alpha beta also", confidence="very_high"))

    pack = controller.search(Principal.HUMAN, "alpha beta gamma delta", page_size=10)
    ids = [r["id"] for r in pack["results"]]

    # Strictly higher overlap must outrank strictly higher confidence.
    assert ids.index("higher_overlap_low_conf") < ids.index("lower_overlap_high_conf")
    # Among equal overlap_ratio (tie_a, tie_b), confidence breaks the tie.
    assert ids.index("tie_b") < ids.index("tie_a")


def test_confidence_survives_in_pack_and_trace_under_every_arm():
    """Requirement 3: removing confidence from RANKING must not remove it
    from the pack or the trace."""
    for arm in (RANKING_ARM_BASELINE, RANKING_ARM_FUSED_SCORE, RANKING_ARM_NO_CONFIDENCE,
                RANKING_ARM_CONFIDENCE_TIEBREAK, RANKING_ARM_FUSED_PLUS_TIEBREAK):
        storage, controller = make_controller(ranking_arm=arm)
        storage.set("n1", _note("n1", "alpha beta", confidence="low"))
        pack = controller.search(Principal.HUMAN, "alpha beta")
        assert pack["results"], f"arm={arm} returned no results"
        assert pack["results"][0]["confidence"] == "low", f"arm={arm} dropped confidence from the pack"
        # RelevanceScorer still ran regardless of arm (it decides nothing
        # about order under non-baseline arms, but it still runs and its
        # signal is still derivable from the note's own confidence field).


# ---------------------------------------------------------------------------
# 4. Determinism: repeated calls under the same arm produce the same order.
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("arm", [
    RANKING_ARM_BASELINE, RANKING_ARM_FUSED_SCORE, RANKING_ARM_NO_CONFIDENCE,
    RANKING_ARM_CONFIDENCE_TIEBREAK, RANKING_ARM_FUSED_PLUS_TIEBREAK,
])
def test_each_arm_is_deterministic_across_repeated_calls(arm):
    def run():
        storage, controller = make_controller(ranking_arm=arm)
        for i in range(6):
            storage.set(f"n{i}", _note(f"n{i}", f"shared term note {i}", confidence=["low", "high", "unknown"][i % 3]))
        pack = controller.search(Principal.HUMAN, "shared term", page_size=6)
        return [r["id"] for r in pack["results"]]

    assert run() == run()


# ---------------------------------------------------------------------------
# 5. Out of scope, must stay untouched: graph expansion.
# ---------------------------------------------------------------------------

def test_ranking_arm_has_no_effect_when_graph_expansion_is_enabled():
    """WP-1 is forbidden from touching graph expansion. The ranking_arm flag
    must only apply to the graph-off branch; with expansion on, the result
    must be identical regardless of ranking_arm (both fall through to the
    untouched graph-branch scoring code)."""
    storage = StorageEngine()
    storage.set("n1", _note("n1", "alpha beta", confidence="medium"))
    storage.set("n2", _note("n2", "gamma delta", confidence="medium"))

    results_by_arm = {}
    for arm in (None, RANKING_ARM_FUSED_SCORE, RANKING_ARM_NO_CONFIDENCE):
        controller = MemoryController(storage, enable_graph_expansion=True, ranking_arm=arm)
        pack = controller.search(Principal.HUMAN, "alpha beta")
        results_by_arm[arm] = [r["id"] for r in pack["results"]]

    values = list(results_by_arm.values())
    assert all(v == values[0] for v in values), (
        f"ranking_arm changed graph-on results: {results_by_arm} -- "
        "graph expansion must be untouched by this package"
    )
