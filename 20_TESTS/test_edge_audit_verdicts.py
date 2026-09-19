"""The relation-audit verdicts must cover the sample exactly, and the report must quote them.

audit_verdicts.json holds one verdict per relation of audit_sample_50.json. The
plasticity report's audit section is generated from it; these tests recompute the
figures independently from the raw JSON and compare them with the report text.
"""
from __future__ import annotations

import copy
import importlib.util
import json
from collections import Counter
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
SAMPLE = REPO / "07_EVALUATION" / "edge_audit" / "audit_sample_50.json"
VERDICTS = REPO / "07_EVALUATION" / "edge_audit" / "audit_verdicts.json"
REPORT = REPO / "07_EVALUATION" / "neural_plasticity" / "NEURAL_PLASTICITY_REPORT.md"

_spec = importlib.util.spec_from_file_location(
    "generate_plasticity_report", REPO / "30_SCRIPTS" / "evaluation" / "generate_plasticity_report.py")
gpr = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(gpr)


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def test_verdicts_cover_exactly_the_sampled_relations():
    sample, doc = load(SAMPLE), load(VERDICTS)
    want = {(r["index"], r["source_id"], r["target_id"], r["relation"]) for r in sample["samples"]}
    got = [(r["index"], r["source_id"], r["target_id"], r["relation"]) for r in doc["verdicts"]]
    assert len(got) == len(set(got)) == len(want) == sample["sample_size"]
    assert set(got) == want


def test_every_row_has_evaluator_reason_and_a_known_verdict():
    for row in load(VERDICTS)["verdicts"]:
        assert row["evaluator"] == "claude-sonnet"
        assert row["verdict"] in {"ACCEPT", "REJECT"}
        assert len(row["rationale"].strip()) > 40, row["index"]
        assert (row["category"] is None) == (row["verdict"] == "ACCEPT"), row["index"]


def test_the_original_packet_is_not_modified():
    sample = load(SAMPLE)
    assert sample["status"] == "PENDING_AUDIT" and sample["audited"] is False
    assert all(r["verdict"] is None and r["evaluator"] is None for r in sample["samples"])


def test_verdicts_are_bound_to_this_sample_file():
    import hashlib
    assert load(VERDICTS)["sample_sha256"] == hashlib.sha256(SAMPLE.read_bytes()).hexdigest()


def test_report_figures_equal_those_computed_from_the_json():
    rows = load(VERDICTS)["verdicts"]
    report = REPORT.read_text(encoding="utf-8")
    assert "### Statut Audit Relații: REALIZAT" in report
    for tier, label in (("strong", "Tari (Strong)"), ("weak", "Slabe (Weak)")):
        sub = [r for r in rows if r["rel_tier"] == tier]
        acc = sum(1 for r in sub if r["verdict"] == "ACCEPT")
        assert f"| {label} | {acc} | {len(sub)} | " in report
    acc_all = sum(1 for r in rows if r["verdict"] == "ACCEPT")
    assert f"| **Total** | {acc_all} | {len(rows)} | " in report
    reasons = Counter(r["category"] for r in rows if r["verdict"] == "REJECT")
    for reason, count in reasons.items():
        line = next(l for l in report.splitlines() if l.startswith(f"| `{reason}` |"))
        assert line.rstrip().endswith(f"| {count} |")


def test_the_deviations_retractions_are_untouched():
    report = REPORT.read_text(encoding="utf-8")
    assert "Retragere afirmație \"50/50 audit\" (Runda 2)" in report


def test_summary_matches_an_independent_recount():
    summary = gpr.audit_summary()
    rows = load(VERDICTS)["verdicts"]
    assert summary["total"] == {"accepted": sum(r["verdict"] == "ACCEPT" for r in rows), "total": len(rows)}


# ---- negative controls: the checks must be able to fail ---------------------------------


def _write(tmp_path, mutate):
    doc = copy.deepcopy(load(VERDICTS))
    mutate(doc)
    path = tmp_path / "audit_verdicts.json"
    path.write_text(json.dumps(doc), encoding="utf-8")
    return path


def test_negative_control_a_missing_relation_is_refused(tmp_path):
    path = _write(tmp_path, lambda d: d["verdicts"].pop())
    with pytest.raises(ValueError, match="cover exactly"):
        gpr.audit_summary(SAMPLE, path)


def test_negative_control_a_duplicated_relation_is_refused(tmp_path):
    path = _write(tmp_path, lambda d: d["verdicts"].append(copy.deepcopy(d["verdicts"][0])))
    with pytest.raises(ValueError, match="cover exactly"):
        gpr.audit_summary(SAMPLE, path)


def test_negative_control_a_row_without_a_reason_is_refused(tmp_path):
    path = _write(tmp_path, lambda d: d["verdicts"][3].__setitem__("rationale", "  "))
    with pytest.raises(ValueError, match="evaluator and rationale"):
        gpr.audit_summary(SAMPLE, path)


def test_negative_control_an_unknown_verdict_is_refused(tmp_path):
    path = _write(tmp_path, lambda d: d["verdicts"][0].__setitem__("verdict", "MAYBE"))
    with pytest.raises(ValueError, match="unknown verdict"):
        gpr.audit_summary(SAMPLE, path)


def test_negative_control_verdicts_for_another_sample_are_refused(tmp_path):
    path = _write(tmp_path, lambda d: d.__setitem__("sample_sha256", "0" * 64))
    with pytest.raises(ValueError, match="different audit_sample"):
        gpr.audit_summary(SAMPLE, path)


def test_negative_control_flipping_a_verdict_changes_the_computed_precision(tmp_path):
    def flip(doc):
        row = next(r for r in doc["verdicts"] if r["verdict"] == "REJECT")
        row["verdict"], row["category"] = "ACCEPT", None
    path = _write(tmp_path, flip)
    flipped = gpr.audit_summary(SAMPLE, path)
    assert flipped["total"]["accepted"] == gpr.audit_summary()["total"]["accepted"] + 1
    assert f"| **Total** | {flipped['total']['accepted']} | " not in REPORT.read_text(encoding="utf-8")
