"""The controls that guard the ranking-arm experiment are themselves tested.

Three experiments in this project reported null results from arms that could
not move the metric. The experiment's answer is a sabotage control, and a
sabotage control that always passes would be worse than none: it would license
exactly the mistake it exists to catch.

So these tests feed the detector the two cases that matter — an arm whose order
changes under sabotage, and one whose order does not — and require it to tell
them apart. The statistics are checked against values computable by hand.
"""
from __future__ import annotations

import importlib.util
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
_SCRIPT = REPO / "30_SCRIPTS" / "evaluation" / "run_ranking_arm_experiment.py"

_spec = importlib.util.spec_from_file_location("ranking_arm_experiment", _SCRIPT)
exp = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(exp)


def row(case_id: str, order: list[str], recall=None) -> dict:
    return {
        "id": case_id,
        "class": "factual",
        "order": order,
        "arm_in_trace": "fused_score",
        "floor_applied": True,
        "candidate_recall": 1,
        "context_recall": recall if recall is not None else int(bool(order)),
    }


# --- the sabotage detector -------------------------------------------------

def test_an_identical_order_means_the_arm_is_not_live():
    """The case that caught three earlier experiments: a knob that changes nothing."""
    real = [row("C1", ["a", "b"]), row("C2", ["c"])]
    sabotaged = [row("C1", ["a", "b"]), row("C2", ["c"])]
    assert exp.cases_with_different_order(real, sabotaged) == []


def test_a_reordered_case_is_detected():
    real = [row("C1", ["a", "b"]), row("C2", ["c"])]
    sabotaged = [row("C1", ["b", "a"]), row("C2", ["c"])]
    assert exp.cases_with_different_order(real, sabotaged) == ["C1"]


def test_order_matters_not_just_membership():
    """A reordering with the same ids is a real behavioural change.

    Comparing sets here would hide every rank change that does not alter the
    page's membership — which is most of what a ranking arm does.
    """
    assert exp.cases_with_different_order(
        [row("C1", ["a", "b", "c"])], [row("C1", ["c", "b", "a"])]) == ["C1"]


def test_a_case_present_in_only_one_run_is_not_counted_as_a_difference():
    assert exp.cases_with_different_order([row("C1", ["a"])], [row("C2", ["a"])]) == []


# --- paired comparison -----------------------------------------------------

def test_gains_and_losses_are_counted_from_the_reference():
    reference = [row("C1", [], 0), row("C2", [], 1), row("C3", [], 1), row("C4", [], 0)]
    arm = [row("C1", [], 1), row("C2", [], 0), row("C3", [], 1), row("C4", [], 0)]
    result = exp.paired(reference, arm)
    assert (result["gained"], result["lost"]) == (1, 1)
    assert result["winning_cases"] == ["C1"]
    assert result["losing_cases"] == ["C2"]
    assert (result["tied_both_1"], result["tied_both_0"]) == (1, 1)


def test_unmeasurable_cases_are_skipped_not_scored_as_zero():
    reference = [row("C1", [], exp.UNMEASURABLE)]
    arm = [row("C1", [], 1)]
    result = exp.paired(reference, arm)
    assert result["skipped_unmeasurable"] == 1
    assert result["gained"] == 0


# --- statistics ------------------------------------------------------------

def test_mcnemar_is_one_when_there_are_no_discordant_pairs():
    assert exp.exact_mcnemar_p(0, 0) == 1.0


def test_mcnemar_matches_a_hand_computed_value():
    # b=0, c=5: two-sided exact = 2 * 0.5**5 = 0.0625
    assert exp.exact_mcnemar_p(0, 5) == pytest.approx(0.0625)
    # b=0, c=8: 2 * 0.5**8 = 0.0078125, the smallest win count the rule accepts
    assert exp.exact_mcnemar_p(0, 8) == pytest.approx(0.0078125)
    assert exp.exact_mcnemar_p(5, 0) == exp.exact_mcnemar_p(0, 5), "must be symmetric"


def test_the_decision_rule_rejects_a_win_that_is_inside_noise():
    """7 gains and 0 losses gives p = 0.015625, under 0.05, yet the rule says no.

    The gain threshold is not a p-value in disguise: it was fixed at 8 before
    any result existed, and a rule that bends once is not a rule.
    """
    comparisons = {"no_confidence": exp.paired(
        [row(f"C{i}", [], 0) for i in range(7)] ,
        [row(f"C{i}", [], 1) for i in range(7)])}
    holm_table = exp.holm({"no_confidence": comparisons["no_confidence"]["exact_mcnemar_p"]})
    decision = exp.decide(comparisons, holm_table)
    assert comparisons["no_confidence"]["gained"] == 7
    assert comparisons["no_confidence"]["exact_mcnemar_p"] < 0.05
    assert decision["arms_meeting_the_rule"] == []
    assert decision["verdict"] == "keep the current default"


def test_the_decision_rule_accepts_a_clear_win():
    comparisons = {"no_confidence": exp.paired(
        [row(f"C{i}", [], 0) for i in range(9)],
        [row(f"C{i}", [], 1) for i in range(9)])}
    holm_table = exp.holm({"no_confidence": comparisons["no_confidence"]["exact_mcnemar_p"]})
    decision = exp.decide(comparisons, holm_table)
    assert decision["arms_meeting_the_rule"] == ["no_confidence"]
    assert decision["verdict"] == "change default to no_confidence"


def test_holm_never_lowers_a_p_value_and_stays_monotone():
    table = exp.holm({"a": 0.01, "b": 0.02, "c": 0.04, "d": 0.5})
    for name, entry in table.items():
        assert entry["p_holm"] >= entry["p_raw"]
    ordered = sorted(table.values(), key=lambda e: e["p_raw"])
    assert [e["p_holm"] for e in ordered] == sorted(e["p_holm"] for e in ordered)


def test_wilson_brackets_the_proportion():
    ci = exp.wilson(21, 130)
    assert ci["lower"] < ci["proportion"] < ci["upper"]
    assert exp.wilson(0, 0) == {"proportion": 0.0, "lower": 0.0, "upper": 0.0}


# --- the experiment's own configuration ------------------------------------

def test_the_reference_arm_is_the_one_search_actually_defaults_to():
    """If the production default moves, this experiment's reference must move with it."""
    import os
    import sys
    os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
    sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
    source = (REPO / "03_IMPLEMENTATION" / "packages" / "memory" / "controller.py").read_text(
        encoding="utf-8")
    assert "active_ranking_arm or RANKING_ARM_FUSED_SCORE" in source, (
        "search() no longer defaults to fused_score; the experiment compares against "
        "the wrong reference arm")
    assert exp.PRODUCTION_ARM == "fused_score"


def test_graph_expansion_is_off_because_the_arm_only_applies_there():
    """With expansion on, the arm is ignored and every comparison would be void."""
    source = _SCRIPT.read_text(encoding="utf-8")
    assert "enable_graph_expansion=False" in source
