"""20_TESTS/test_remaining_typed_edge_audit_verdicts.py — Proves integrity of remaining 65 typed edge audit verdicts.

Guarantees:
1. Verdicts cover all 65 sampled relations exactly without duplicates.
2. Every row has a valid verdict ('ACCEPT' or 'REJECT'), valid category when rejected, and non-empty rationale.
3. The verdicts file binds to audit_sample_declared_remaining_65.json via SHA-256 digest.
4. Report figures in AUDIT_RESULT.md match computed figures from raw JSON verdicts.
5. All accepted and rejected edges map to the declared sample indices.
"""
from __future__ import annotations

import copy
import hashlib
import json
from collections import Counter
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
PKG_DIR = REPO / "07_EVALUATION" / "edge_audit_v2_remaining"
SAMPLE_FILE = PKG_DIR / "audit_sample_declared_remaining_65.json"
SHA_FILE = PKG_DIR / "audit_sample_declared_remaining_65.json.sha256"
VERDICTS_FILE = PKG_DIR / "audit_verdicts_remaining_65.json"
REPORT_FILE = PKG_DIR / "AUDIT_RESULT.md"


def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def test_verdicts_cover_all_65_samples():
    sample = load_json(SAMPLE_FILE)
    verdicts_doc = load_json(VERDICTS_FILE)

    assert verdicts_doc.get("evaluated_count") == 65
    assert len(verdicts_doc.get("verdicts", [])) == 65

    sample_indices = {s["index"] for s in sample["samples"]}
    verdict_indices = {v["index"] for v in verdicts_doc["verdicts"]}

    assert sample_indices == verdict_indices
    assert len(verdict_indices) == 65


def test_verdicts_integrity_and_fields():
    doc = load_json(VERDICTS_FILE)
    allowed_categories = {"wrong_type", "unsupported", "unrelated", "shared_terms_only", "wrong_direction"}

    accepted_count = 0
    rejected_count = 0

    for v in doc["verdicts"]:
        assert v["verdict"] in {"ACCEPT", "REJECT"}
        assert len(v.get("rationale", "").strip()) >= 30, f"Rationale too short for index {v['index']}"

        if v["verdict"] == "ACCEPT":
            accepted_count += 1
            assert v.get("category") is None, f"Accepted row {v['index']} must have null category"
        else:
            rejected_count += 1
            assert v.get("category") in allowed_categories, f"Invalid category for rejected row {v['index']}"

    assert accepted_count == 10
    assert rejected_count == 55
    assert doc.get("accepted_count") == 10
    assert doc.get("rejected_count") == 55


def test_verdicts_sample_sha_binding():
    raw_bytes = SAMPLE_FILE.read_bytes()
    norm = raw_bytes.replace(b"\r\n", b"\n")
    digest = hashlib.sha256(norm).hexdigest()

    sha_text = SHA_FILE.read_text(encoding="utf-8").strip()
    recorded_sha = sha_text.split()[0]
    assert digest == recorded_sha

    doc = load_json(VERDICTS_FILE)
    assert doc.get("sample_sha256") == digest


def test_audit_result_report_matches_computed_figures():
    doc = load_json(VERDICTS_FILE)
    verdicts = doc["verdicts"]
    report = REPORT_FILE.read_text(encoding="utf-8")

    # Overall totals
    total_acc = sum(1 for v in verdicts if v["verdict"] == "ACCEPT")
    total_rej = sum(1 for v in verdicts if v["verdict"] == "REJECT")
    assert f"| **Total** | **{total_acc}/65** |" in report
    assert f"| **Total Rejected** | **{total_rej}** |" in report

    # Category counts
    cat_counts = Counter(v["category"] for v in verdicts if v["verdict"] == "REJECT")
    for cat, count in cat_counts.items():
        assert f"| `{cat}` | {count} |" in report

    # Relation breakdown
    by_rel = {}
    for v in verdicts:
        rel = v["relation"]
        by_rel.setdefault(rel, {"acc": 0, "tot": 0})
        by_rel[rel]["tot"] += 1
        if v["verdict"] == "ACCEPT":
            by_rel[rel]["acc"] += 1

    for rel, c in by_rel.items():
        assert f"| `{rel}` | {c['acc']}/{c['tot']} |" in report


def test_negative_control_missing_verdict_fails():
    doc = copy.deepcopy(load_json(VERDICTS_FILE))
    doc["verdicts"].pop()

    sample = load_json(SAMPLE_FILE)
    sample_indices = {s["index"] for s in sample["samples"]}
    verdict_indices = {v["index"] for v in doc["verdicts"]}

    assert sample_indices != verdict_indices
