"""Tests for offline curriculum evaluation, regression agreement, and paired statistics.

Verifies:
1. Pure offline evaluation reproduces exact OpenStax Psychology #164 verdicts:
   - Control: 0/12 supported, 10/10 traps pass, 0 wrong.
   - Treatment: 6/12 supported, 10/10 traps pass, 0 wrong.
2. Wilson score interval analytical boundaries (0/n, n/n, small sample).
3. Exact McNemar test on discordant pairs (binomial exact, small sample significance).
"""
from __future__ import annotations

import json
import sys
from pathlib import Path
import pytest

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "30_SCRIPTS" / "evaluation"))

from run_curriculum_eval import (
    evaluate_raw_responses,
    exact_mcnemar_test,
    wilson_score_interval,
)

RAW_PSYCH_RESPONSES = REPO_ROOT / "07_EVALUATION" / "curriculum" / "raw_responses" / "psychology-memory-ch08-v1_responses.json"
FROZEN_TEST_SET = REPO_ROOT / "07_EVALUATION" / "curriculum" / "openstax_ch08_frozen_test_set.json"


class TestCurriculumOfflineRegression:
    def test_psychology_offline_judgment_matches_issue_164_verdicts(self):
        assert RAW_PSYCH_RESPONSES.exists(), f"Raw responses not found at {RAW_PSYCH_RESPONSES}"
        raw_data = json.loads(RAW_PSYCH_RESPONSES.read_text(encoding="utf-8"))
        test_data = json.loads(FROZEN_TEST_SET.read_text(encoding="utf-8"))

        eval_res = evaluate_raw_responses(raw_data, test_data)

        # Control arm checks
        c_rq = eval_res["control_arm"]["review_questions"]
        c_tq = eval_res["control_arm"]["trap_questions"]
        assert c_rq["correct_supported"] == "0/12"
        assert c_rq["wrong"] == "0/12"
        assert c_tq["trap_pass"] == "10/10"
        assert c_tq["trap_fail"] == "0/10"

        # Treatment arm checks
        t_rq = eval_res["treatment_arm"]["review_questions"]
        t_tq = eval_res["treatment_arm"]["trap_questions"]
        assert t_rq["correct_supported"] == "6/12"
        assert t_rq["correct_unsupported"] == "0/12"
        assert t_rq["wrong"] == "0/12"
        assert t_tq["trap_pass"] == "10/10"
        assert t_tq["trap_fail"] == "0/10"

        # McNemar test check
        mcn = eval_res["paired_statistics"]["mcnemar_exact"]
        assert mcn["b_improved"] == 6
        assert mcn["c_regressed"] == 0
        assert mcn["two_sided_p_value"] == 0.03125
        assert mcn["is_significant_at_alpha_05"] is True


class TestStatisticalFunctions:
    def test_wilson_interval_boundaries(self):
        # 0 / 12
        p0, low0, high0 = wilson_score_interval(0, 12, 0.95)
        assert p0 == 0.0
        assert low0 == 0.0
        assert 0.20 < high0 < 0.26

        # 12 / 12
        p1, low1, high1 = wilson_score_interval(12, 12, 0.95)
        assert p1 == 1.0
        assert 0.74 < low1 < 0.80
        assert high1 == 1.0

        # 6 / 12
        p_mid, low_mid, high_mid = wilson_score_interval(6, 12, 0.95)
        assert p_mid == 0.5
        assert 0.24 < low_mid < 0.26
        assert 0.74 < high_mid < 0.76

    def test_exact_mcnemar_test_calculations(self):
        # 6 improved, 0 regressed -> significant at 0.05
        b, c, p_val, is_sig = exact_mcnemar_test(6, 0)
        assert b == 6
        assert c == 0
        assert p_val == pytest.approx(0.03125, rel=1e-4)
        assert is_sig is True

        # 2 improved, 0 regressed -> not significant at 0.05 (p = 0.5)
        _, _, p_val2, is_sig2 = exact_mcnemar_test(2, 0)
        assert p_val2 == pytest.approx(0.50, rel=1e-4)
        assert is_sig2 is False

        # 0 discordant pairs -> p = 1.0
        _, _, p_val0, is_sig0 = exact_mcnemar_test(0, 0)
        assert p_val0 == 1.0
        assert is_sig0 is False
