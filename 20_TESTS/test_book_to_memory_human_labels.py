"""Tests for the B06 labelling packet and the label ingestion / calibration (PR #209 B06).

Labels in these tests are SYNTHETIC fixtures that exercise the arithmetic. The committed packet has no label in it,
and a test asserts that.
"""
import csv
import importlib.util
import io
import json
from pathlib import Path

import pytest

from lifecycle.validation.book_to_memory_human_labels import (
    ALLOWED,
    COLUMNS,
    MIN_LABELS,
    STATUS_CALIBRATED,
    STATUS_INSUFFICIENT,
    STATUS_WAITING,
    TYPE_H1,
    TYPE_NOTE,
    LabelError,
    build_rows,
    calibrate,
    read_labels,
    render_report_markdown,
    rows_to_csv,
)
from lifecycle.validation.book_to_memory_paired_stats import wilson_interval

REPO = Path(__file__).resolve().parents[1]
PACKET_DIR = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "b06_labelling_packet"


def load_cli(name):
    spec = importlib.util.spec_from_file_location(name, REPO / "30_SCRIPTS" / "evaluation" / f"{name}.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


# ---------------------------------------------------------------- the committed packet
def test_committed_packet_has_no_label_in_it():
    rows = list(csv.DictReader((PACKET_DIR / "labels_to_fill.csv").open(encoding="utf-8")))
    assert len(rows) == 131 and list(rows[0]) == COLUMNS
    for r in rows:
        assert r["label"] == r["labeler_id"] == r["other_relevant_ids"] == r["comment"] == ""
        assert r["allowed_values"] == "|".join(ALLOWED[r["item_type"]])
    assert {r["item_type"] for r in rows} == {TYPE_H1, TYPE_NOTE}
    assert sum(r["item_type"] == TYPE_H1 for r in rows) == 80 and sum(r["item_type"] == TYPE_NOTE for r in rows) == 51


def test_committed_packet_is_a_fresh_build():
    assert load_cli("generate_b06_labelling_packet").main(["--check"]) == 0


def test_instructions_say_what_to_do_and_that_b06_is_waiting():
    text = (PACKET_DIR / "OWNER_INSTRUCTIONS.md").read_text(encoding="utf-8")
    assert "waiting on owner labels" in text and "b2m_ingest_labels.py" in text and "labeler_id" in text
    assert (PACKET_DIR / "items_readable.md").read_text(encoding="utf-8").count("## ") == 131


def test_committed_calibration_report_says_waiting_and_claims_nothing():
    rep = json.loads((REPO / "07_EVALUATION" / "b2m_calibration" / "CALIBRATION_REPORT.json").read_text(encoding="utf-8"))
    assert rep["status"] == STATUS_WAITING and rep["labelled_items"] == 0 and rep["metrics"] == {}


# ---------------------------------------------------------------- synthetic labelling
def small_inputs(n_cases=30, n_notes=25):
    cases = {"cases": [{"id": f"C{i:02d}", "family": "direct_lexical", "split": "held_out" if i % 2 == 0 else "development",
                        "query": f"query {i}", "gold_relevant_notes": [f"g{i}"]} for i in range(n_cases)]}
    corpus = {"notes": [{"id": f"g{i}", "title": f"Gold {i}", "excerpt": f"excerpt {i}"} for i in range(n_cases)]}
    tasks = [{"note_id": f"n{i}", "term": f"term{i}", "note_definition": f"def {i}", "source_passage": f"passage {i}"} for i in range(n_notes)]
    conf = {f"n{i}": "high" for i in range(n_notes)}
    return cases, corpus, tasks, conf


def fill(rows, labeler, rule):
    out = []
    for r in rows:
        r = dict(r)
        lab = rule(r)
        if lab:
            r["label"], r["labeler_id"] = lab, labeler
        out.append(r)
    return out


def parse(rows):
    ids = {r["item_id"]: r["item_type"] for r in rows}
    return read_labels(rows_to_csv(rows), ids)


def test_build_rows_covers_every_gold_row_and_every_note_and_never_prefills():
    cases, corpus, tasks, conf = small_inputs()
    rows = build_rows(cases, corpus, tasks, conf)
    assert len(rows) == 30 + 25 and {r["priority"] for r in rows} == {"1", "2"}
    assert all(r["label"] == "" for r in rows)
    held = [r for r in rows if r["item_type"] == TYPE_H1 and "held_out" in r["context"]]
    assert held and all(r["priority"] == "1" for r in held)
    with pytest.raises(LabelError):
        build_rows(cases, {"notes": []}, tasks, conf)


def test_no_labels_is_waiting_and_reports_no_rate():
    rows = build_rows(*small_inputs())
    rep = calibrate(rows, [])
    assert rep["status"] == STATUS_WAITING and rep["metrics"] == {}
    assert "claims" in render_report_markdown(rep) or "none is claimed" in render_report_markdown(rep)


def test_too_few_labels_is_insufficient_and_gives_no_rate():
    rows = build_rows(*small_inputs())
    few = fill(rows, "owner", lambda r: "yes" if r["item_id"].endswith(("C00|g0", "C01|g1")) else "")
    rep = calibrate(rows, [parse(few)])
    assert rep["metrics"]["h1_gold"]["status"] == STATUS_INSUFFICIENT and "gold_judged_relevant" not in rep["metrics"]["h1_gold"]
    assert rep["status"] == STATUS_INSUFFICIENT


def test_calibration_rates_wilson_intervals_and_adjusted_recall():
    cases, corpus, tasks, conf = small_inputs()
    rows = build_rows(cases, corpus, tasks, conf)
    # synthetic rule: gold of cases 0..23 relevant, 24..29 not; notes: 20 faithful, 5 unfaithful
    def rule(r):
        if r["item_type"] == TYPE_H1:
            return "yes" if int(r["item_id"].split("|")[1][1:]) < 24 else "no"
        return "faithful" if int(r["item_id"].split("|")[1][1:]) < 20 else "unfaithful"
    labelled = parse(fill(rows, "owner", rule))
    baseline = [{"id": c["id"], "split": c["split"], "gold_relevant_notes": c["gold_relevant_notes"], "hits_at_10": int(c["id"][1:]) % 3 == 0} for c in cases["cases"]]
    rep = calibrate(rows, [labelled], baseline_cases=baseline, note_confidence={f"n{i}": "high" for i in range(25)})
    assert rep["status"] == STATUS_CALIBRATED
    g = rep["metrics"]["h1_gold"]
    assert g["gold_judged_relevant"]["k"] == 24 and g["gold_judged_relevant"]["n"] == 30
    lo, hi = wilson_interval(24, 30)
    assert g["gold_judged_relevant"]["wilson95"] == [round(lo, 4), round(hi, 4)]
    adj = g["baseline_recall_at_10"]
    assert adj["as_published_held_out"]["n"] == 15 and adj["human_confirmed_gold_only"]["n"] < 15
    assert adj["cases_with_gold_not_confirmed"] == 3        # held-out cases 24, 26, 28
    n = rep["metrics"]["candidate_notes"]
    assert n["faithful"]["k"] == 20 and n["by_claimed_confidence"]["high"]["rate"] == 0.8


def test_labelers_that_disagree_are_reported_and_disputed_items_leave_the_rates():
    cases, corpus, tasks, conf = small_inputs()
    rows = build_rows(cases, corpus, tasks, conf)
    only_h1 = lambda lab: (lambda r: lab if r["item_type"] == TYPE_H1 else "")
    a = parse(fill(rows, "owner", only_h1("yes")))
    flip = lambda r: ("no" if r["item_id"].endswith(("C00|g0", "C01|g1", "C02|g2")) else "yes") if r["item_type"] == TYPE_H1 else ""
    b = parse(fill(rows, "second", flip))
    rep = calibrate(rows, [a, b])
    assert len(rep["disputed_items"]) == 3
    assert rep["metrics"]["h1_gold"]["gold_judged_relevant"]["n"] == 27
    assert rep["agreement"]["cohens_kappa"] is not None and rep["agreement"]["items_with_2plus_labelers"] == 30


def test_malformed_label_files_are_refused():
    rows = build_rows(*small_inputs())
    ids = {r["item_id"]: r["item_type"] for r in rows}
    good = fill(rows, "owner", lambda r: "yes" if r["item_type"] == TYPE_H1 else "")
    text = rows_to_csv(good)
    assert read_labels(text, ids)["labels"]
    with pytest.raises(LabelError, match="unknown item_id"):
        read_labels(text.replace("H1G|C00|g0", "H1G|C00|zzz", 1), ids)
    with pytest.raises(LabelError, match="not one of"):
        read_labels(rows_to_csv(fill(rows, "owner", lambda r: "maybe")), ids)
    with pytest.raises(LabelError, match="not one of"):                        # a note label on an H1 row
        read_labels(rows_to_csv(fill(rows, "owner", lambda r: "faithful")), ids)
    nolabeler = [dict(r, label="yes", labeler_id="") if r["item_type"] == TYPE_H1 else r for r in rows]
    with pytest.raises(LabelError, match="labeler_id"):
        read_labels(rows_to_csv(nolabeler), ids)
    with pytest.raises(LabelError, match="appears twice"):
        read_labels(rows_to_csv(rows + rows[:1]), ids)
    with pytest.raises(LabelError, match="missing columns"):
        read_labels("item_id,label\nx,yes\n", ids)
    swapped = [dict(r, item_type=TYPE_NOTE) if r is rows[0] else r for r in rows]
    with pytest.raises(LabelError, match="item_type changed"):
        read_labels(rows_to_csv(swapped), ids)


def test_labels_are_case_insensitive_and_blank_rows_count_as_unlabelled():
    rows = build_rows(*small_inputs())
    ids = {r["item_id"]: r["item_type"] for r in rows}
    some = [dict(r, label="YES", labeler_id="owner") if i < 3 and r["item_type"] == TYPE_H1 else r for i, r in enumerate(rows)]
    parsed = read_labels(rows_to_csv(some), ids)
    assert len(parsed["labels"]) == 3 and parsed["unlabelled"] == len(rows) - 3


# ---------------------------------------------------------------- the ingest CLI
def test_ingest_cli_without_labels_writes_a_waiting_report(tmp_path):
    cli = load_cli("b2m_ingest_labels")
    assert cli.main(["--out-dir", str(tmp_path)]) == 2
    assert json.loads((tmp_path / "CALIBRATION_REPORT.json").read_text(encoding="utf-8"))["status"] == STATUS_WAITING


def test_ingest_cli_calibrates_a_fully_synthetic_label_file(tmp_path):
    cli = load_cli("b2m_ingest_labels")
    rows = list(csv.DictReader((PACKET_DIR / "labels_to_fill.csv").open(encoding="utf-8")))
    filled = fill(rows, "synthetic-labeler", lambda r: "yes" if r["item_type"] == TYPE_H1 else "faithful")
    f = tmp_path / "labels.csv"
    f.write_text(rows_to_csv(filled), encoding="utf-8")
    assert cli.main(["--labels", str(f), "--out-dir", str(tmp_path / "out")]) == 0
    rep = json.loads((tmp_path / "out" / "CALIBRATION_REPORT.json").read_text(encoding="utf-8"))
    assert rep["status"] == STATUS_CALIBRATED and rep["metrics"]["h1_gold"]["gold_judged_relevant"]["rate"] == 1.0
    assert rep["metrics"]["candidate_notes"]["by_claimed_confidence"]["high"]["n"] == 51
    # an edited item id in the file is refused, not guessed
    bad = tmp_path / "bad.csv"
    bad.write_text(f.read_text(encoding="utf-8").replace("PN|", "PX|", 1), encoding="utf-8")
    with pytest.raises(LabelError):
        cli.main(["--labels", str(bad), "--out-dir", str(tmp_path / "o2")])


def test_min_labels_constant_is_stated_in_the_instructions():
    assert f"at least {MIN_LABELS} labelled" in (PACKET_DIR / "OWNER_INSTRUCTIONS.md").read_text(encoding="utf-8")
