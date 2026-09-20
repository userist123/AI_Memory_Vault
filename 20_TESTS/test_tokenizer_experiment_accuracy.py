"""20_TESTS/test_tokenizer_experiment_accuracy.py — Tokenizer Normalization Experiment Accuracy & Controls.

Validates Part 3 PR 1:
1. Artifact integrity: PREREGISTRATION.md, tokenizer_experiment_cases.json, and TOKENIZER_EXPERIMENT_REPORT.md exist and non-empty.
2. Frozen benchmark immutability: SHA-256 matches frozen hash eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa.
3. Bit-for-bit rendering accuracy: render_report(json_data) matches TOKENIZER_EXPERIMENT_REPORT.md exactly.
4. Negative controls on report tampering: modifying any count or delta in report or JSON fails verification.
5. Operating point contract: every table declares (Principal.AI_AGENT, page_size=5, Floor: ACTIV).
6. Pre-registered decision rule evaluation: verified against formal criteria (verdict: MENȚINERE BASELINE, H-TOKEN-1 INFIRMATĂ).
7. Discordant pairs integrity: b = 0, c = 0, p_mcnemar = 1.000000 across all comparisons.
8. Unit testing of statistical functions (McNemar exact test, Wilson score interval) and tokenizer behaviors.
"""
from __future__ import annotations

import copy
import hashlib
import json
import sys
from pathlib import Path
import pytest

REPO_ROOT = Path(__file__).resolve().parents[1]
scripts_eval_path = REPO_ROOT / "30_SCRIPTS" / "evaluation"
if str(scripts_eval_path) not in sys.path:
    sys.path.insert(0, str(scripts_eval_path))

from eval_tokenizer_experiment import (
    ARTIFACT_JSON_PATH,
    BENCHMARK_PATH,
    BENCHMARK_SHA_PATH,
    PREREG_PATH,
    REPORT_MD_PATH,
    mcnemar_exact_test,
    render_report,
    strip_diacritics,
    tokenize_baseline,
    tokenize_stripped,
    tokenize_unicode,
    wilson_score_interval,
)


@pytest.fixture(scope="module")
def tokenizer_data():
    """Loads precomputed tokenizer experiment JSON artifact."""
    assert ARTIFACT_JSON_PATH.exists(), f"Artifact missing: {ARTIFACT_JSON_PATH}"
    content = ARTIFACT_JSON_PATH.read_text(encoding="utf-8")
    return json.loads(content)


@pytest.fixture(scope="module")
def tokenizer_report_md():
    """Loads committed markdown report."""
    assert REPORT_MD_PATH.exists(), f"Report missing: {REPORT_MD_PATH}"
    return REPORT_MD_PATH.read_text(encoding="utf-8")


def test_tokenizer_experiment_artifacts_exist_and_non_empty():
    """Verifies that preregistration, JSON artifact, and report exist and are non-empty."""
    assert PREREG_PATH.exists(), "PREREGISTRATION.md is missing"
    assert PREREG_PATH.stat().st_size > 2000, "PREREGISTRATION.md unexpectedly small"

    assert ARTIFACT_JSON_PATH.exists(), "tokenizer_experiment_cases.json is missing"
    assert ARTIFACT_JSON_PATH.stat().st_size > 10_000, "tokenizer_experiment_cases.json unexpectedly small"

    assert REPORT_MD_PATH.exists(), "TOKENIZER_EXPERIMENT_REPORT.md is missing"
    assert REPORT_MD_PATH.stat().st_size > 2000, "TOKENIZER_EXPERIMENT_REPORT.md unexpectedly small"


def test_frozen_benchmark_integrity():
    """Verifies that retrieval_benchmark_v3.json has not been modified."""
    assert BENCHMARK_PATH.exists()
    assert BENCHMARK_SHA_PATH.exists()
    expected_sha = BENCHMARK_SHA_PATH.read_text(encoding="utf-8").split()[0]
    actual_sha = hashlib.sha256(BENCHMARK_PATH.read_bytes()).hexdigest()
    assert actual_sha == expected_sha == "eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa"


def test_report_matches_render_output_bit_for_bit(tokenizer_data, tokenizer_report_md):
    """Verifies that TOKENIZER_EXPERIMENT_REPORT.md is bit-for-bit identical to render_report(data)."""
    rendered = render_report(tokenizer_data)
    assert tokenizer_report_md.strip() == rendered.strip(), (
        "TOKENIZER_EXPERIMENT_REPORT.md does not match render_report(data) bit-for-bit!"
    )


def test_negative_control_report_tampering(tokenizer_data, tokenizer_report_md):
    """Negative Control: altering any reported statistic in the markdown report triggers mismatch."""
    # 1. Tamper hit count
    tampered_hits = tokenizer_report_md.replace("21 / 130", "22 / 130", 1)
    assert tampered_hits != tokenizer_report_md
    rendered = render_report(tokenizer_data)
    assert tampered_hits.strip() != rendered.strip(), (
        "Tampered report with altered hit count unexpectedly matched rendered output!"
    )

    # 2. Tamper percentage
    tampered_pct = tokenizer_report_md.replace("16.15%", "16.50%", 1)
    assert tampered_pct != tokenizer_report_md
    assert tampered_pct.strip() != rendered.strip(), (
        "Tampered report with altered percentage unexpectedly matched rendered output!"
    )

    # 3. Tamper verdict
    tampered_verdict = tokenizer_report_md.replace("INFIRMATĂ", "CONFIRMATĂ", 1)
    assert tampered_verdict != tokenizer_report_md
    assert tampered_verdict.strip() != rendered.strip(), (
        "Tampered report with altered verdict unexpectedly matched rendered output!"
    )


def test_negative_control_json_tampering(tokenizer_data, tokenizer_report_md):
    """Negative Control: modifying statistics in JSON produces output differing from committed report."""
    tampered_data = copy.deepcopy(tokenizer_data)
    # Tamper Romanian hits in Arm 2
    tampered_data["arms"]["arm2_unicode"]["hits_ro"] += 3
    tampered_rendered = render_report(tampered_data)
    assert tampered_rendered.strip() != tokenizer_report_md.strip(), (
        "Report re-rendered with tampered JSON data unexpectedly matched committed report!"
    )


def test_every_table_declares_operating_point(tokenizer_report_md):
    """Every markdown table in the report must explicitly declare its operating point parameters.

    Required parameters in title/caption:
    - Principal.AI_AGENT
    - page_size=5
    - Floor: ACTIV
    """
    lines = tokenizer_report_md.splitlines()
    table_indices = []
    for idx, line in enumerate(lines):
        if line.strip().startswith("|") and "---" in line:
            table_indices.append(idx)

    assert len(table_indices) >= 4, f"Expected at least 4 tables, found {len(table_indices)}"

    for sep_idx in table_indices:
        preceding_text = "\n".join(lines[max(0, sep_idx - 6) : sep_idx])
        has_principal = "Principal.AI_AGENT" in preceding_text
        has_page_size = "page_size=5" in preceding_text
        has_floor = "Floor: ACTIV" in preceding_text

        assert has_principal and has_page_size and has_floor, (
            f"Table near line {sep_idx+1} does not declare required operating point in header/caption:\n{preceding_text}"
        )


def test_preregistered_verdicts_and_rules(tokenizer_data, tokenizer_report_md):
    """Verifies that measured outcomes conform strictly to pre-registered hypotheses and decision rules."""
    verdicts = tokenizer_data["verdicts"]
    assert verdicts["hypothesis_H_TOKEN_1"] == "INFIRMATĂ"
    assert verdicts["non_regression_english"] == "CONFIRMATĂ"
    assert verdicts["net_total_gain"] == "INFIRMATĂ"
    assert verdicts["decision"] == "MENȚINERE BASELINE (RESPINGERE ADOPTARE TOKENIZATOR NOU)"

    # Report mentions these conclusions explicitly
    assert "MENȚINERE BASELINE (RESPINGERE ADOPTARE TOKENIZATOR NOU)" in tokenizer_report_md
    assert "INFIRMATĂ" in tokenizer_report_md
    assert "Tokenizatorul de producție din `hybrid_retrieval.py` rămâne neschimbat pe `main`" in tokenizer_report_md

    # Arm results check
    for arm_key in ["arm1_baseline", "arm2_unicode", "arm3_stripped"]:
        arm = tokenizer_data["arms"][arm_key]
        assert arm["hits_all"] == 21
        assert arm["total_cases"] == 130
        assert arm["hits_ro"] == 9
        assert arm["total_ro"] == 61
        assert arm["hits_en"] == 12
        assert arm["total_en"] == 69
        assert arm["reachable_in_top200_ro"] == 46

    # Discordant cases check
    comp_u = tokenizer_data["comparisons"]["arm2_unicode_vs_baseline"]
    comp_s = tokenizer_data["comparisons"]["arm3_stripped_vs_baseline"]
    for comp in [comp_u, comp_s]:
        assert comp["all"]["gains_b"] == 0
        assert comp["all"]["losses_c"] == 0
        assert comp["all"]["p_mcnemar"] == 1.0
        assert comp["ro"]["gains_b"] == 0
        assert comp["ro"]["losses_c"] == 0
        assert comp["ro"]["p_mcnemar"] == 1.0
        assert len(comp["discordant_cases"]) == 0


def test_statistical_helpers_mcnemar_and_wilson():
    """Unit tests for McNemar exact test and Wilson score intervals."""
    # 1. McNemar exact
    # Concordant / no discordant pairs
    assert mcnemar_exact_test(0, 0) == 1.0
    assert mcnemar_exact_test(1, 1) == 1.0
    assert mcnemar_exact_test(10, 10) == 1.0

    # 5 gains, 0 losses: sum_{k=0}^0 comb(5, 0) * 0.5^5 = 1/32 = 0.03125. Two-tailed: 2 * 0.03125 = 0.0625
    assert mcnemar_exact_test(5, 0) == pytest.approx(0.0625, abs=1e-5)
    assert mcnemar_exact_test(0, 5) == pytest.approx(0.0625, abs=1e-5)

    # 10 gains, 1 loss: n=11, k<=1: (1 + 11) * 0.5^11 = 12 / 2048 = 0.005859375. Two-tailed: 0.01171875
    assert mcnemar_exact_test(10, 1) == pytest.approx(0.01171875, abs=1e-5)

    # 2. Wilson score interval
    w = wilson_score_interval(21, 130)
    assert 0.10 < w["lower"] < 0.16
    assert 0.16 < w["upper"] < 0.25
    assert w["proportion"] == round(21 / 130, 4)


def test_tokenizer_behaviors():
    """Unit tests demonstrating the exact character behavior of each tokenizer arm."""
    text_ro = "științific și regăsire"

    # Baseline: splits on Romanian diacritics into fragments
    # Note: 'științific' splits into 'tiin' and 'ific'
    # Note: In 'învățare', 'are' is removed because it collides with English stopword 'are'
    toks_base = tokenize_baseline(text_ro)
    assert "tiin" in toks_base
    assert "ific" in toks_base
    assert "reg" in toks_base
    assert "sire" in toks_base

    # Unicode: preserves Romanian letters in single tokens
    toks_uni = tokenize_unicode(text_ro)
    assert "științific" in toks_uni
    assert "regăsire" in toks_uni

    # Stripped: removes diacritics symmetrically
    toks_strip = tokenize_stripped(text_ro)
    assert "stiintific" in toks_strip
    assert "regasire" in toks_strip
