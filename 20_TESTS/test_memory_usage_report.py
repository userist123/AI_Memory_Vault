"""memory_usage_report.py: numbers computed from the usage log, nothing else in the output."""
from __future__ import annotations

import importlib.util
import json
import os
import subprocess
import sys
import uuid
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

_spec = importlib.util.spec_from_file_location("memory_usage_report", REPO / "30_SCRIPTS" / "evaluation" / "memory_usage_report.py")
rep = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(rep)

_fx_spec = importlib.util.spec_from_file_location("memory_vault_fixture", REPO / "20_TESTS" / "memory_vault_fixture.py")
fx = importlib.util.module_from_spec(_fx_spec)
_fx_spec.loader.exec_module(fx)


def row(tool="memory_search", day="2026-09-19", n=2, latency=100.0, outcome="ok", ids=(), qhash=None, lifecycle=None, client="claude-code"):
    entry = {"ts": f"{day}T10:00:00.000+00:00", "tool": tool, "client": client, "query_sha256": qhash or uuid.uuid4().hex,
             "n_results": n, "ids": list(ids), "latency_ms": latency, "outcome": outcome}
    if lifecycle:
        entry["lifecycle_counts"] = lifecycle
    return entry


ROWS = [
    row(latency=100), row(latency=200), row(latency=300), row(latency=400), row(n=0, latency=500),
    row(day="2026-09-20", latency=600, lifecycle={"ACTIVE": 1, "ARCHIVED": 1}),
    row(tool="recall_cli", n=0, latency=700, client="cli"),
    row(tool="memory_get", n=1, latency=50, ids=["a"]),
    row(tool="memory_search", outcome="error", latency=0.0),
]


def test_counts_per_day_and_tool():
    report = rep.build_report(ROWS)
    assert report["calls_total"] == 9 and report["calls_ok"] == 8 and report["calls_error"] == 1
    assert report["calls_per_day"]["2026-09-19"]["memory_search"] == 6  # five ok + the error line
    assert report["calls_per_day"]["2026-09-19"]["recall_cli"] == 1
    assert report["calls_per_day"]["2026-09-20"] == {"memory_search": 1}
    assert report["calls_per_tool"]["memory_get"] == 1


def test_zero_result_share_counts_ok_searches_only():
    # ok searches: 5 memory_search on the 19th + 1 on the 20th + 1 recall_cli = 7; zero results: 1 + 1 = 2
    report = rep.build_report(ROWS)
    zero = report["searches_with_zero_results"]
    assert (zero["k"], zero["n"], zero["text"]) == (2, 7, "2/7")
    assert zero["percent"] == pytest.approx(100 * 2 / 7)


def test_latency_percentiles_are_nearest_rank_over_ok_calls():
    latencies = [100, 200, 300, 400, 500, 600, 700, 50]  # the error call is excluded
    ordered = sorted(latencies)
    report = rep.build_report(ROWS)
    assert report["latency_ms"]["n"] == 8
    assert report["latency_ms"]["p50"] == ordered[3]      # ceil(0.5 * 8) = 4th value
    assert report["latency_ms"]["p95"] == ordered[7]      # ceil(0.95 * 8) = 8th value
    assert rep.percentile([], 50) is None
    assert rep.percentile([5], 95) == 5


def test_lifecycle_of_returned_notes_is_summed():
    assert rep.build_report(ROWS)["returned_notes_by_lifecycle"] == {"ACTIVE": 1, "ARCHIVED": 1}


def test_proposals_and_attestation_are_read_from_the_vault(tmp_path):
    from memory_controller.storage.serializer import serialize
    vault = fx.make_vault(tmp_path)
    ids = {}
    for verification in ("verified", "unverified"):
        note_id = str(uuid.uuid4())
        ids[verification] = note_id
        text = fx.note_text(f"proposal {verification}", "corp", note_id, lifecycle="REVIEW").replace(
            "verification: unverified", f"verification: {verification}")
        (vault / "01_ARCHITECTURE" / "knowledge" / f"p_{verification}.md").write_text(text, encoding="utf-8")
    missing = str(uuid.uuid4())
    rows = [row(tool="memory_propose", n=1, ids=[ids["verified"]]), row(tool="memory_propose", n=1, ids=[ids["unverified"]]),
            row(tool="memory_propose", n=1, ids=[missing]), row(tool="memory_propose", outcome="error")]
    report = rep.build_report(rows, vault)
    assert report["proposals"] == 3 and report["proposed_note_ids"] == 3
    assert report["proposals_attested"]["text"] == "1/3"
    assert serialize is not None


def test_report_text_contains_no_query_text_or_note_content():
    planted = dict(row(), query="TEXTUL INTREBARII", content="CONTINUT DE NOTA", secret="SECRETUL")
    text = rep.render(rep.build_report([planted, row()]))
    for forbidden in ("TEXTUL INTREBARII", "CONTINUT DE NOTA", "SECRETUL"):
        assert forbidden not in text


def test_negative_control_changing_the_log_changes_the_numbers():
    base = rep.build_report(ROWS)
    changed = rep.build_report(ROWS + [row(n=0), row(n=0)])
    assert changed["searches_with_zero_results"]["k"] == base["searches_with_zero_results"]["k"] + 2
    assert changed["latency_ms"]["n"] == base["latency_ms"]["n"] + 2


def test_rendered_numbers_equal_the_computed_ones():
    report = rep.build_report(ROWS)
    text = rep.render(report)
    assert f"Calls: {report['calls_total']} ({report['calls_ok']} ok, {report['calls_error']} error)" in text
    assert f"Searches with zero results: {report['searches_with_zero_results']['text']}" in text
    assert f"p50 {report['latency_ms']['p50']:.1f}, p95 {report['latency_ms']['p95']:.1f}" in text


def test_command_line_reads_a_log_file(tmp_path):
    log = tmp_path / "usage.jsonl"
    log.write_text("\n".join(json.dumps(r) for r in ROWS) + "\n", encoding="utf-8")
    res = subprocess.run([sys.executable, str(REPO / "30_SCRIPTS" / "evaluation" / "memory_usage_report.py"),
                          "--log", str(log), "--vault", str(tmp_path)],
                         capture_output=True, text=True, encoding="utf-8", cwd=REPO)
    assert res.returncode == 0, res.stderr
    assert "Searches with zero results: 2/7" in res.stdout
