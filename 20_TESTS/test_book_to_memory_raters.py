"""Tests for multi-rater records, agreement statistics and aggregation (PR #209 B05).

The statistics are checked against textbook worked examples:
* Cohen's kappa: the 2x2 example of Cohen (1960) as reproduced on Wikipedia, kappa = 0.40.
* Fleiss' kappa: the 10-subject, 14-rater, 5-category example of Fleiss (1971), kappa = 0.210.
* Krippendorff's alpha: the 3-coder, 15-unit example with missing values, nominal 0.691, interval 0.811.
"""
import json
from pathlib import Path

import pytest

from lifecycle.validation.book_to_memory_raters import (
    RATING_RECORD_SCHEMA,
    SCHEMA_VERSION,
    STATUS_INSUFFICIENT_RATERS,
    RatingError,
    RatingRecord,
    aggregate_ratings,
    band,
    cohens_kappa,
    fleiss_kappa,
    interpret_kappa,
    krippendorff_alpha,
    load_ratings,
    validate_rating,
)

REPO = Path(__file__).resolve().parents[1]


# ---------------------------------------------------------------- textbook examples
def test_cohens_kappa_textbook_2x2():
    # 50 applications; both yes 20, A yes/B no 5, A no/B yes 10, both no 15: po = 0.7, pe = 0.5, kappa = 0.4
    a = ["y"] * 20 + ["y"] * 5 + ["n"] * 10 + ["n"] * 15
    b = ["y"] * 20 + ["n"] * 5 + ["y"] * 10 + ["n"] * 15
    assert cohens_kappa(a, b) == pytest.approx(0.4, abs=1e-12)


def test_cohens_kappa_perfect_and_chance_and_undefined():
    x = ["a", "b", "a", "b", "c", "c"]
    assert cohens_kappa(x, list(x)) == pytest.approx(1.0)
    assert cohens_kappa(["a", "a", "b", "b"], ["a", "b", "a", "b"]) == pytest.approx(0.0)
    assert cohens_kappa(["a", "a"], ["a", "a"]) is None  # one category used by both: chance agreement is 1


def test_cohens_kappa_quadratic_weights_match_an_independent_formula():
    a = [1, 2, 3, 1, 2, 3, 2, 3, 1, 3]
    b = [1, 2, 3, 2, 3, 3, 2, 2, 1, 1]
    n = len(a)
    observed = sum((x - y) ** 2 for x, y in zip(a, b)) / n
    expected = sum((x - y) ** 2 for x in a for y in b) / (n * n)
    assert cohens_kappa(a, b, "quadratic") == pytest.approx(1 - observed / expected)
    observed_l = sum(abs(x - y) for x, y in zip(a, b)) / n
    expected_l = sum(abs(x - y) for x in a for y in b) / (n * n)
    assert cohens_kappa(a, b, "linear") == pytest.approx(1 - observed_l / expected_l)


def test_cohens_kappa_rejects_bad_input():
    with pytest.raises(RatingError):
        cohens_kappa([1, 2], [1])
    with pytest.raises(RatingError):
        cohens_kappa([], [])
    with pytest.raises(RatingError):
        cohens_kappa([1], [1], "cubic")


FLEISS_TABLE = [
    [0, 0, 0, 0, 14], [0, 2, 6, 4, 2], [0, 0, 3, 5, 6], [0, 3, 9, 2, 0], [2, 2, 8, 1, 1],
    [7, 7, 0, 0, 0], [3, 2, 6, 3, 0], [2, 5, 3, 2, 2], [6, 5, 2, 1, 0], [0, 2, 2, 3, 7],
]


def test_fleiss_kappa_textbook_example():
    assert fleiss_kappa(FLEISS_TABLE) == pytest.approx(0.210, abs=5e-4)


def test_fleiss_kappa_extremes_and_guards():
    assert fleiss_kappa([[3, 0], [0, 3], [3, 0]]) == pytest.approx(1.0)
    assert fleiss_kappa([[3, 0], [3, 0]]) is None            # everyone said the same thing
    with pytest.raises(RatingError):
        fleiss_kappa([[2, 1], [1, 1]])                       # unequal rater counts
    with pytest.raises(RatingError):
        fleiss_kappa([[1, 0]])                               # one rater
    with pytest.raises(RatingError):
        fleiss_kappa([])


_N = None
KRIPPENDORFF = {
    "A": [_N, _N, _N, _N, _N, 3, 4, 1, 2, 1, 1, 3, 3, _N, 3],
    "B": [1, _N, 2, 1, 3, 3, 4, 3, _N, _N, _N, _N, _N, _N, _N],
    "C": [_N, _N, 2, 1, 3, 4, 4, _N, 2, 1, 1, 3, 3, _N, 4],
}


def _units():
    return {u: [r[u] for r in KRIPPENDORFF.values() if r[u] is not None] for u in range(15)}


def test_krippendorff_alpha_textbook_example():
    assert krippendorff_alpha(_units(), "nominal") == pytest.approx(0.691, abs=5e-4)
    assert krippendorff_alpha(_units(), "interval") == pytest.approx(0.811, abs=5e-4)


def test_krippendorff_alpha_ordinal_and_ratio_lie_between_nominal_and_one():
    nominal = krippendorff_alpha(_units(), "nominal")
    for level in ("ordinal", "ratio"):
        assert nominal < krippendorff_alpha(_units(), level) < 1.0


def test_krippendorff_alpha_perfect_chance_and_degenerate():
    perfect = {i: [v, v, v] for i, v in enumerate([1, 2, 3, 2, 1])}
    assert krippendorff_alpha(perfect, "nominal") == pytest.approx(1.0)
    assert krippendorff_alpha(perfect, "interval") == pytest.approx(1.0)
    assert krippendorff_alpha({0: [1, 1], 1: [1, 1]}) is None       # one value overall
    # Two coders disagreeing on every unit are worse than chance.
    assert krippendorff_alpha({0: [1, 2], 1: [2, 1], 2: [1, 2], 3: [2, 1]}) < 0
    with pytest.raises(RatingError):
        krippendorff_alpha({0: [1]})
    with pytest.raises(RatingError):
        krippendorff_alpha({0: [1, 2]}, "bogus")


def test_krippendorff_matches_a_direct_pairwise_computation_for_complete_two_coder_data():
    """alpha = 1 - (observed pair disagreement) / (disagreement over all value pairs), n corrected."""
    a = [1, 2, 3, 3, 2, 1, 4, 1, 2, 5]
    b = [1, 2, 3, 3, 3, 2, 4, 1, 2, 4]
    units = {i: [x, y] for i, (x, y) in enumerate(zip(a, b))}
    n = 2 * len(a)
    pooled = a + b
    d_o = sum(2 * (x - y) ** 2 for x, y in zip(a, b)) / n
    d_e = sum((p - q) ** 2 for i, p in enumerate(pooled) for j, q in enumerate(pooled) if i != j) / (n * (n - 1))
    assert krippendorff_alpha(units, "interval") == pytest.approx(1 - d_o / d_e)


def test_interpretation_bands():
    assert interpret_kappa(None) == "undefined"
    assert [interpret_kappa(v) for v in (-0.1, 0.1, 0.3, 0.5, 0.7, 0.9)] == \
        ["poor", "slight", "fair", "moderate", "substantial", "almost perfect"]


def test_policy_bands():
    assert [band(x) for x in (10, 8, 7.9, 5, 4.9, 0)] == ["PASS", "PASS", "RETRY", "RETRY", "FAIL", "FAIL"]


# ---------------------------------------------------------------- record schema
def rec(rater="R1", kind="human", model=None, blind=True, item="ITM-1", label=7, why="clear and correct"):
    return {"schema_version": SCHEMA_VERSION, "rater_id": rater, "rater_kind": kind, "model": model,
            "blind": blind, "item_id": item, "label": label, "rationale": why}


def test_valid_records_round_trip():
    r = validate_rating(rec())
    assert isinstance(r, RatingRecord) and r.label == 7 and r.timestamp
    m = validate_rating(rec(rater="M1", kind="model", model="model-x", label="PASS"))
    assert m.source_key == "model:model-x"


@pytest.mark.parametrize("bad", [
    {**rec(), "extra": 1},
    {k: v for k, v in rec().items() if k != "rationale"},
    {**rec(), "schema_version": "other"},
    rec(rater=""), rec(item=" "), rec(kind="robot"),
    rec(kind="model", model=None), rec(kind="human", model="x"),
    rec(blind="yes"), rec(label=None), rec(label=True), rec(label=float("nan")), rec(label="  "),
    rec(why=" "),
])
def test_invalid_records_are_refused(bad):
    with pytest.raises(RatingError):
        validate_rating(bad)


def test_schema_file_is_the_module_schema():
    on_disk = json.loads((REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "rating_record.schema.json").read_text(encoding="utf-8"))
    assert on_disk == json.loads(json.dumps(RATING_RECORD_SCHEMA))
    assert set(on_disk["required"]) == {"schema_version", "rater_id", "rater_kind", "model", "blind", "item_id", "label", "rationale"}


def test_load_ratings_reports_the_bad_line():
    lines = [json.dumps(rec()), "", json.dumps(rec(label=None))]
    with pytest.raises(RatingError, match="line 3"):
        load_ratings(lines)
    assert len(load_ratings(lines[:2])) == 1


# ---------------------------------------------------------------- aggregation
def _records(scores_by_rater, kind="human", model=None, blind=True):
    out = []
    for rater, scores in scores_by_rater.items():
        for i, s in enumerate(scores):
            out.append(validate_rating(rec(rater=rater, kind=kind if model is None else "model",
                                           model=model, blind=blind, item=f"ITM-{i}", label=s)))
    return out


def test_one_rater_is_not_enough():
    res = aggregate_ratings(_records({"R1": [7, 8, 9]}))
    assert res["status"] == STATUS_INSUFFICIENT_RATERS and res["items"] == {}


def test_two_raters_with_the_same_model_are_one_source():
    recs = _records({"M1": [7, 8, 9]}, model="model-x") + _records({"M2": [6, 8, 9]}, model="model-x")
    assert aggregate_ratings(recs)["status"] == STATUS_INSUFFICIENT_RATERS


def test_unblind_ratings_are_set_aside_not_mixed_in():
    recs = _records({"R1": [7, 8, 9], "R2": [7, 7, 9]}) + _records({"R3": [10, 10, 10]}, blind=False)
    res = aggregate_ratings(recs)
    assert res["status"] == "OK" and len(res["excluded_unblind"]) == 3
    assert "R3" not in res["raters"]
    assert aggregate_ratings(recs, require_blind=False)["raters"] == ["R1", "R2", "R3"]


def test_consensus_and_agreement_for_three_raters():
    recs = _records({"R1": [9, 8, 4, 2], "R2": [9, 7, 5, 1], "R3": [8, 8, 6, 2]})
    res = aggregate_ratings(recs)
    assert res["status"] == "OK"
    assert res["items"]["ITM-0"]["mean"] == pytest.approx(26 / 3)
    assert res["items"]["ITM-0"]["median"] == 9.0
    assert res["items"]["ITM-0"]["unanimous"] is False
    ag = res["agreement"]
    assert ag["krippendorff_alpha_interval"] > 0.9
    assert ag["fleiss_kappa_bands"] is not None and "cohens_kappa" not in ag   # three raters: no Cohen


def test_two_raters_get_cohens_kappa_on_bands():
    res = aggregate_ratings(_records({"R1": [9, 8, 4, 2, 6], "R2": [9, 9, 4, 3, 5]}))
    assert res["agreement"]["cohens_kappa"] == pytest.approx(1.0)
    assert res["agreement"]["cohens_kappa_n_items"] == 5


def test_disagreeing_raters_have_low_agreement():
    res = aggregate_ratings(_records({"R1": [9, 1, 9, 1, 9, 1], "R2": [1, 9, 1, 9, 1, 9]}))
    assert res["agreement"]["krippendorff_alpha_interval"] < 0
    assert res["agreement"]["cohens_kappa"] < 0


def test_duplicate_rating_is_an_error():
    recs = _records({"R1": [7, 8], "R2": [7, 8]})
    recs.append(recs[0])
    with pytest.raises(RatingError, match="duplicate"):
        aggregate_ratings(recs)


def test_categorical_labels_use_plurality_consensus():
    recs = []
    for rater, labels in {"R1": ["a", "a", "b"], "R2": ["a", "b", "b"], "R3": ["a", "a", "b"]}.items():
        for i, lab in enumerate(labels):
            recs.append(validate_rating(rec(rater=rater, item=f"I{i}", label=lab)))
    res = aggregate_ratings(recs, numeric=False)
    assert res["items"]["I0"]["consensus"] == "a" and res["items"]["I2"]["consensus"] == "b"
    assert res["agreement"]["krippendorff_alpha_nominal"] is not None
