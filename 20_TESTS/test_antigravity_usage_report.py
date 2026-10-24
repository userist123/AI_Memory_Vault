#!/usr/bin/env python3
"""Tests for Antigravity Usage & Token Telemetry Report (30_SCRIPTS/routing/antigravity_usage_report.py).

Verifies:
1. Strict separation of uncached input, cached input, and output tokens.
2. Handling of corrupt lines, invalid fields (negatives, non-integers, missing input_tokens), and unmetered events.
3. Correct formula calculation for `mean_uncached_input_tokens_per_measured_step`, median, min, max, and cache rate.
4. Rate card parsing, financial estimation flagging, and rejection of invalid rate cards.
5. Directory aggregation and single-file analysis without external dependencies or network calls.
"""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent
MODULE = ROOT / "30_SCRIPTS" / "routing" / "antigravity_usage_report.py"

# Import functions directly for testing
sys.path.insert(0, str(MODULE.parent))
from antigravity_usage_report import (  # noqa: E402
    RateCard,
    SessionTelemetry,
    aggregate_telemetry,
    compute_median,
    find_antigravity_transcripts,
    parse_rate_card,
    parse_transcript_file,
)


def test_parse_rate_card_valid_and_invalid():
    rc = parse_rate_card("0.15,0.0375,0.60")
    assert rc is not None
    assert rc.uncached_input_per_m == 0.15
    assert rc.cached_input_per_m == 0.0375
    assert rc.output_per_m == 0.60
    assert "0.15" in rc.name

    assert parse_rate_card(None) is None
    assert parse_rate_card("") is None

    with pytest.raises(ValueError, match="Invalid rate card format"):
        parse_rate_card("0.15,0.0375")

    with pytest.raises(ValueError, match="Token prices cannot be negative"):
        parse_rate_card("0.15,-0.0375,0.60")

    with pytest.raises(ValueError, match="Invalid numeric values"):
        parse_rate_card("abc,0.0375,0.60")


def test_compute_median():
    assert compute_median([]) == 0.0
    assert compute_median([100]) == 100.0
    assert compute_median([10, 20, 30]) == 20.0
    assert compute_median([10, 20, 30, 40]) == 25.0
    assert compute_median([40, 10, 30, 20]) == 25.0


def test_parse_transcript_clean_session(tmp_path: Path):
    session_dir = tmp_path / "brain" / "sess-1" / ".system_generated" / "logs"
    session_dir.mkdir(parents=True)
    tfile = session_dir / "transcript.jsonl"

    events = [
        {"type": "USER_INPUT", "source": "USER_EXPLICIT", "content": "Hello"},
        {"type": "PLANNER_RESPONSE", "source": "MODEL", "status": "DONE", "input_tokens": 1000, "cache_read_tokens": 4000, "output_tokens": 50},
        {"type": "PLANNER_RESPONSE", "source": "MODEL", "status": "DONE", "input_tokens": 2000, "cache_read_tokens": 8000, "output_tokens": 150},
    ]
    tfile.write_text("\n".join(json.dumps(e) for e in events) + "\n", encoding="utf-8")

    res = parse_transcript_file(tfile)
    assert res.session_id == "sess-1"
    assert res.total_events == 3
    assert res.measured_steps == 2
    assert res.unmetered_steps == 1
    assert res.corrupt_lines == 0
    assert res.invalid_field_events == 0

    assert res.total_uncached_input_tokens == 3000
    assert res.total_cached_input_tokens == 12000
    assert res.total_output_tokens == 200
    assert res.total_context_tokens == 15000

    assert res.mean_uncached_input_tokens_per_measured_step == 1500.0
    assert res.median_uncached_input_tokens_per_measured_step == 1500.0
    assert res.min_uncached_input_tokens_per_measured_step == 1000
    assert res.max_uncached_input_tokens_per_measured_step == 2000
    # cache hit rate: 12000 / (3000 + 12000) = 0.8
    assert res.cache_hit_rate == 0.8
    assert res.estimated_cost_usd is None


def test_parse_transcript_with_corrupt_and_invalid_data(tmp_path: Path):
    session_dir = tmp_path / "brain" / "sess-bad" / ".system_generated" / "logs"
    session_dir.mkdir(parents=True)
    tfile = session_dir / "transcript.jsonl"

    lines = [
        "",  # blank
        "NOT JSON",  # corrupt line
        json.dumps(["not a dict"]),  # corrupt structure
        json.dumps({"type": "CHECKPOINT", "content": "saved"}),  # unmetered
        json.dumps({"type": "PLANNER_RESPONSE", "input_tokens": -50, "output_tokens": 10}),  # negative -> invalid
        json.dumps({"type": "PLANNER_RESPONSE", "input_tokens": "one hundred", "output_tokens": 10}),  # str -> invalid
        json.dumps({"type": "PLANNER_RESPONSE", "cache_read_tokens": 500}),  # missing input_tokens -> invalid
        json.dumps({"type": "PLANNER_RESPONSE", "input_tokens": 500, "cache_read_tokens": 1500, "output_tokens": 25}),  # valid
    ]
    tfile.write_text("\n".join(lines) + "\n", encoding="utf-8")

    res = parse_transcript_file(tfile)
    assert res.total_events == 7
    assert res.corrupt_lines == 2
    assert res.unmetered_steps == 1
    assert res.invalid_field_events == 3
    assert res.measured_steps == 1
    assert res.total_uncached_input_tokens == 500
    assert res.total_cached_input_tokens == 1500
    assert res.total_output_tokens == 25
    assert res.mean_uncached_input_tokens_per_measured_step == 500.0


def test_aggregate_telemetry_with_rate_card():
    s1 = SessionTelemetry(
        session_id="s1", session_path="/p1", total_events=10, measured_steps=2,
        total_uncached_input_tokens=2000, total_cached_input_tokens=8000, total_output_tokens=100
    )
    s2 = SessionTelemetry(
        session_id="s2", session_path="/p2", total_events=5, measured_steps=1,
        total_uncached_input_tokens=4000, total_cached_input_tokens=6000, total_output_tokens=50
    )
    s_empty = SessionTelemetry(session_id="s_empty", session_path="/p3", total_events=2, measured_steps=0)

    rc = RateCard(uncached_input_per_m=1.0, cached_input_per_m=0.25, output_per_m=2.0, name="test_card")
    agg = aggregate_telemetry([s1, s2, s_empty], rc)

    assert agg["total_sessions_found"] == 3
    assert agg["measured_sessions"] == 2
    assert agg["ignored_sessions"] == 1
    assert agg["total_measured_steps"] == 3
    assert agg["total_uncached_input_tokens"] == 6000
    assert agg["total_cached_input_tokens"] == 14000
    assert agg["total_output_tokens"] == 150
    # Mean uncached: 6000 / 3 = 2000.0
    assert agg["mean_uncached_input_tokens_per_measured_step"] == 2000.0
    # Cache hit rate: 14000 / (6000 + 14000) = 0.70
    assert agg["cache_hit_rate"] == 0.70

    # Cost: (6000/1M * 1.0) + (14000/1M * 0.25) + (150/1M * 2.0)
    # = 0.006 + 0.0035 + 0.0003 = 0.0098
    assert agg["estimated_total_cost_usd"] == 0.0098
    assert "Approximation" in agg["cost_disclaimer"]


def test_cli_execution_json(tmp_path: Path):
    session_dir = tmp_path / "brain" / "sess-cli" / ".system_generated" / "logs"
    session_dir.mkdir(parents=True)
    tfile = session_dir / "transcript.jsonl"
    tfile.write_text(json.dumps({"type": "PLANNER_RESPONSE", "input_tokens": 1200, "cache_read_tokens": 4800, "output_tokens": 100}) + "\n")

    r = subprocess.run(
        [sys.executable, str(MODULE), "--brain-dir", str(tmp_path / "brain"), "--json"],
        capture_output=True, text=True, check=True
    )
    data = json.loads(r.stdout)
    assert data["summary"]["total_sessions_found"] == 1
    assert data["summary"]["total_measured_steps"] == 1
    assert data["summary"]["mean_uncached_input_tokens_per_measured_step"] == 1200.0
    assert len(data["sessions"]) == 1
    assert data["sessions"][0]["session_id"] == "sess-cli"
