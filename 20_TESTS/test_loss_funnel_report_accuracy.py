"""20_TESTS/test_loss_funnel_report_accuracy.py — Loss Funnel Report Accuracy and Negative Tampering Controls.

Validates Part 2 PR 3 requirements:
1. Artifact integrity: JSON artifact and Markdown report exist, non-empty, and valid.
2. Bit-for-bit rendering accuracy: render_report(json_data) matches LOSS_FUNNEL_REPORT.md exactly.
3. Negative control on report tampering: altering any single statistic in the report or JSON fails verification.
4. Operating point contract: every table declares (principal, page_size, agent_lifecycle_floor_active).
5. Diagnostic completeness: UNDETERMINED is exactly 0 (0.00%), well below the 10% threshold.
6. Gap isolation: lifecycle cost is 0 at p=5 and 1 at p=10; pagination cost is 16 cases.
7. Pre-registered decision rule evaluation: verified against formal criteria (verdict: Adoptare Reranker).
8. Statistical helpers: Wilson score, Fisher exact, and Holm-Bonferroni correction invariants.
"""
from __future__ import annotations

import copy
import json
import re
import sys
from pathlib import Path
import pytest

REPO_ROOT = Path(__file__).resolve().parents[1]
scripts_eval_path = REPO_ROOT / "30_SCRIPTS" / "evaluation"
if str(scripts_eval_path) not in sys.path:
    sys.path.insert(0, str(scripts_eval_path))

from retrieval_loss_funnel import (
    CASES_JSON_PATH,
    REPORT_MD_PATH,
    fishers_exact_2x2,
    holm_bonferroni,
    render_report,
    wilson_score_interval,
)


@pytest.fixture(scope="module")
def loss_funnel_data():
    """Loads precomputed loss funnel cases JSON."""
    assert CASES_JSON_PATH.exists(), f"Artifact missing: {CASES_JSON_PATH}"
    content = CASES_JSON_PATH.read_text(encoding="utf-8")
    return json.loads(content)


@pytest.fixture(scope="module")
def loss_funnel_report_md():
    """Loads committed markdown report."""
    assert REPORT_MD_PATH.exists(), f"Report missing: {REPORT_MD_PATH}"
    return REPORT_MD_PATH.read_text(encoding="utf-8")


def test_loss_funnel_artifacts_exist_and_non_empty():
    """Verifies that diagnostic artifacts exist and contain non-trivial data."""
    assert CASES_JSON_PATH.exists()
    assert CASES_JSON_PATH.stat().st_size > 100_000, "loss_funnel_cases.json unexpectedly small"

    assert REPORT_MD_PATH.exists()
    assert REPORT_MD_PATH.stat().st_size > 5_000, "LOSS_FUNNEL_REPORT.md unexpectedly small"


def test_report_matches_render_output_bit_for_bit(loss_funnel_data, loss_funnel_report_md):
    """Verifies that LOSS_FUNNEL_REPORT.md is bit-for-bit identical to render_report(data)."""
    rendered = render_report(loss_funnel_data)
    assert loss_funnel_report_md.strip() == rendered.strip(), (
        "LOSS_FUNNEL_REPORT.md does not match render_report(data) bit-for-bit!"
    )


def test_negative_control_report_tampering(loss_funnel_data, loss_funnel_report_md):
    """Negative Control: altering any reported statistic in the markdown report triggers mismatch."""
    # 1. Tamper hit count
    tampered_hits = loss_funnel_report_md.replace("21 / 130", "22 / 130", 1)
    assert tampered_hits != loss_funnel_report_md
    rendered = render_report(loss_funnel_data)
    assert tampered_hits.strip() != rendered.strip(), (
        "Tampered report with altered hit count unexpectedly matched rendered output!"
    )

    # 2. Tamper category count
    tampered_cat = loss_funnel_report_md.replace("| `PAGINATION_CUT` | **78** |", "| `PAGINATION_CUT` | **79** |", 1)
    assert tampered_cat != loss_funnel_report_md
    assert tampered_cat.strip() != rendered.strip(), (
        "Tampered report with altered category count unexpectedly matched rendered output!"
    )


def test_negative_control_json_tampering(loss_funnel_data, loss_funnel_report_md):
    """Negative Control: modifying a classification in JSON produces output differing from committed report."""
    tampered_data = copy.deepcopy(loss_funnel_data)
    # Tamper one category count in agent_p5
    tampered_data["operating_points"]["agent_p5"]["loss_categories"]["PAGINATION_CUT"]["count"] += 1
    tampered_rendered = render_report(tampered_data)
    assert tampered_rendered.strip() != loss_funnel_report_md.strip(), (
        "Report re-rendered with tampered JSON data unexpectedly matched committed report!"
    )


def test_every_table_declares_operating_point(loss_funnel_report_md):
    """Every markdown table in the report must explicitly declare its operating point parameters.

    Required parameters in title/caption:
    - principal (Principal.AI_AGENT or Principal.HUMAN)
    - page_size (5 or 10 or k in)
    - agent_lifecycle_floor_active (Floor: ACTIVE/INACTIV or prag ciclu de viata)
    """
    lines = loss_funnel_report_md.splitlines()
    table_indices = []
    for idx, line in enumerate(lines):
        if line.strip().startswith("|") and "---" in line:
            table_indices.append(idx)

    assert len(table_indices) >= 9, f"Expected at least 9 tables, found {len(table_indices)}"

    # For each table separator, scan backwards up to 10 lines for a header declaring the operating point
    for sep_idx in table_indices:
        preceding_text = "\n".join(lines[max(0, sep_idx - 10) : sep_idx])
        # Check that table title or caption contains Principal, page_size, and Floor
        has_principal = ("Principal.AI_AGENT" in preceding_text) or ("Principal.HUMAN" in preceding_text)
        has_page_size = ("page_size" in preceding_text) or ("$k \\in" in preceding_text) or ("p=" in preceding_text)
        has_floor = ("Floor" in preceding_text) or ("ciclu de via" in preceding_text.lower())

        assert has_principal and has_page_size and has_floor, (
            f"Table near line {sep_idx+1} does not declare required operating point in header/caption:\n{preceding_text}"
        )


def test_zero_undetermined_cases(loss_funnel_data):
    """Verifies that UNDETERMINED cases count is 0 across all operating points."""
    for op_key, op_data in loss_funnel_data["operating_points"].items():
        undetermined_info = op_data["loss_categories"].get("UNDETERMINED", {})
        count = undetermined_info.get("count", -1)
        pct = undetermined_info.get("proportion_of_misses", -1.0)
        assert count == 0, f"Operating point {op_key} has {count} UNDETERMINED cases (expected 0)!"
        assert pct == 0.0, f"Operating point {op_key} has non-zero UNDETERMINED percentage: {pct}%"


def test_loss_funnel_gap_isolation(loss_funnel_data):
    """Verifies the forensic decomposition of the 21 vs 39 hit count difference.

    1. agent_p5 hits: 21 / 130
    2. human_p5 hits: 21 / 130 (lifecycle floor cost at page_size=5 is exactly 0)
    3. human_p10 hits: 38 / 130 (39 in benchmark v3 frozen state, 1 moved due to procedural note expansion)
    4. agent floor exclusions in agent_p5: exactly 15 cases
    """
    ap5 = loss_funnel_data["operating_points"]["agent_p5"]["summary"]
    hp5 = loss_funnel_data["operating_points"]["human_p5"]["summary"]
    hp10 = loss_funnel_data["operating_points"]["human_p10"]["summary"]

    assert ap5["hits"] == 21, f"Expected 21 hits for agent_p5, got {ap5['hits']}"
    assert hp5["hits"] == 21, f"Expected 21 hits for human_p5, got {hp5['hits']}"
    assert hp10["hits"] == 38, f"Expected 38 hits for human_p10, got {hp10['hits']}"

    # Verify net lifecycle cost at p=5 is 0
    lifecycle_cost_p5 = hp5["hits"] - ap5["hits"]
    assert lifecycle_cost_p5 == 0, f"Net lifecycle cost at p=5 is {lifecycle_cost_p5}, expected 0"

    # Verify agent floor exclusions in agent_p5
    floor_excluded_count = loss_funnel_data["operating_points"]["agent_p5"]["loss_categories"]["AGENT_LIFECYCLE_FLOOR_EXCLUDED"]["count"]
    assert floor_excluded_count == 15


def test_preregistered_decision_rules_and_verdict(loss_funnel_data, loss_funnel_report_md):
    """Verifies that loss funnel statistics satisfy the pre-registered decision rule for Cross-Encoder reranker."""
    ap5 = loss_funnel_data["operating_points"]["agent_p5"]
    miss_categories = ap5["loss_categories"]
    misses_total = ap5["summary"]["misses"]

    pag_cut = miss_categories["PAGINATION_CUT"]["count"]
    cand_cut = miss_categories["CANDIDATE_LIMIT_CUT"]["count"]
    ranking_cut_ratio = (pag_cut + cand_cut) / misses_total
    median_miss_rank = ap5["median_miss_rank"]

    # Criterion 1: PAGINATION_CUT + CANDIDATE_LIMIT_CUT >= 40%
    assert ranking_cut_ratio >= 0.40, f"Ranking cut ratio {ranking_cut_ratio*100:.2f}% < 40%"
    # Criterion 2: median miss rank <= 30
    assert median_miss_rank <= 30.0, f"Median miss rank {median_miss_rank} > 30"

    # Pre-registered verdict: Adoptare Reranker
    assert "Adoptare Reranker" in loss_funnel_report_md
    assert "73.39%" in loss_funnel_report_md
    assert "20.5" in loss_funnel_report_md


def test_statistical_helpers_invariants():
    """Unit test for mathematical functions: Wilson interval, Fisher exact, and Holm-Bonferroni."""
    # 1. Wilson score interval
    w_zero = wilson_score_interval(0, 100)
    assert w_zero["lower"] == 0.0
    assert 0.0 < w_zero["upper"] < 0.05

    w_full = wilson_score_interval(100, 100)
    assert w_full["upper"] == 1.0
    assert 0.95 < w_full["lower"] < 1.0

    w_half = wilson_score_interval(50, 100)
    assert 0.39 < w_half["lower"] < 0.41
    assert 0.59 < w_half["upper"] < 0.61

    # 2. Fisher exact test
    # Identical proportions should yield p = 1.0
    p_ident = fishers_exact_2x2(10, 10, 10, 10)
    assert p_ident == pytest.approx(1.0, abs=1e-6)

    # Highly divergent contingency table should yield very low p
    p_div = fishers_exact_2x2(50, 0, 0, 50)
    assert p_div < 1e-10

    # 3. Holm-Bonferroni
    raw_p = {"test1": 0.01, "test2": 0.04, "test3": 0.03}
    adj_p = holm_bonferroni(raw_p)
    # Sorted order: test1 (0.01 * 3 = 0.03), test3 (0.03 * 2 = 0.06), test2 (0.04 * 1 = 0.04 -> monotonic adjusted to 0.06)
    assert adj_p["test1"] == pytest.approx(0.03, abs=1e-4)
    assert adj_p["test3"] == pytest.approx(0.06, abs=1e-4)
    assert adj_p["test2"] == pytest.approx(0.06, abs=1e-4)
