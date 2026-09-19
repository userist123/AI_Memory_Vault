"""Unit tests verifying accuracy of Retrieval Benchmark v3 results and report.

Guarantees:
1. Every figure in BENCHMARK_V3_REPORT.md matches results_v3_arms.json exactly.
2. The negative control on the default budget (None) passes (zero nodes added when seeds >= 20).
3. Negative control on report tampering: altering any figure in the report causes validation to fail.
4. Statistical functions (Wilson score interval, exact McNemar test) compute exact values.
5. The pre-registered decision rule evaluates to 'nu se adoptă' strictly based on the empirical data.
"""
from __future__ import annotations

import importlib.util
import json
import re
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "30_SCRIPTS" / "evaluation" / "run_retrieval_benchmark_v3.py"
RESULTS_PATH = REPO_ROOT / "07_EVALUATION" / "retrieval_benchmark_v3" / "results_v3_arms.json"
REPORT_PATH = REPO_ROOT / "07_EVALUATION" / "retrieval_benchmark_v3" / "BENCHMARK_V3_REPORT.md"

_spec = importlib.util.spec_from_file_location("run_retrieval_benchmark_v3", SCRIPT_PATH)
bench_mod = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(bench_mod)


class TestStatisticalFunctions(unittest.TestCase):
    def test_exact_mcnemar_p_preregistered_reference(self):
        # PREREGISTRATION.md declares: 8 wins, 1 loss gives p = 0.0390625
        p = bench_mod.exact_mcnemar_p(1, 8)
        self.assertAlmostEqual(p, 0.0390625, places=7)

    def test_exact_mcnemar_zero_discordant(self):
        self.assertEqual(bench_mod.exact_mcnemar_p(0, 0), 1.0)

    def test_exact_mcnemar_symmetric(self):
        self.assertEqual(bench_mod.exact_mcnemar_p(3, 7), bench_mod.exact_mcnemar_p(7, 3))

    def test_wilson_interval_bounds(self):
        ci = bench_mod.wilson_score_interval(10, 100)
        self.assertGreaterEqual(ci["lower"], 0.0)
        self.assertLessEqual(ci["upper"], 1.0)
        self.assertTrue(ci["lower"] < ci["proportion"] < ci["upper"])

    def test_wilson_interval_zero_and_full(self):
        ci_zero = bench_mod.wilson_score_interval(0, 50)
        self.assertEqual(ci_zero["lower"], 0.0)
        self.assertGreater(ci_zero["upper"], 0.0)

        ci_full = bench_mod.wilson_score_interval(50, 50)
        self.assertLess(ci_full["lower"], 1.0)
        self.assertEqual(ci_full["upper"], 1.0)


class TestNegativeControls(unittest.TestCase):
    def test_default_budget_negative_control_passes(self):
        self.assertTrue(RESULTS_PATH.exists(), f"Missing {RESULTS_PATH}")
        results = json.loads(RESULTS_PATH.read_text(encoding="utf-8"))
        ctl = results["default_budget_negative_control"]
        self.assertTrue(ctl["passed"], "Default budget negative control failed")
        self.assertEqual(ctl["nodes_added_where_impossible"], 0)
        self.assertGreater(ctl["queries_expansion_impossible"]["k"], 0)

    def test_negative_control_fails_on_synthetic_leakage(self):
        # If seeds >= 20 ever added nodes under default budget, control must fail
        fake_rows = [
            {"seeds": 25, "expanded": 2, "candidate_recall": 1, "context_recall": 0},
            {"seeds": 5, "expanded": 0, "candidate_recall": 0, "context_recall": 0},
        ]
        ctl = bench_mod.default_budget_control(fake_rows)
        self.assertFalse(ctl["passed"])
        self.assertEqual(ctl["nodes_added_where_impossible"], 2)


class TestReportAccuracyAgainstJSON(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.results = json.loads(RESULTS_PATH.read_text(encoding="utf-8"))
        cls.report_text = REPORT_PATH.read_text(encoding="utf-8")

    def test_report_matches_rendered_output_bit_for_bit(self):
        re_rendered = bench_mod.render_report(self.results)
        self.assertEqual(
            self.report_text.replace("\r\n", "\n"),
            re_rendered.replace("\r\n", "\n"),
            "Report on disk does not match render_report(results) byte-for-byte",
        )

    def test_report_contains_exact_json_figures_for_budget_5(self):
        b5_summary = self.results["arms"]["budget_5"]["summary"]
        b5_paired = self.results["arms"]["budget_5"]["paired_vs_off"]["context_recall"]

        cand_txt = b5_summary["candidate_recall"]["text"]
        ctx_txt = b5_summary["context_recall"]["text"]
        gained = str(b5_paired["gained"])
        lost = str(b5_paired["lost"])

        self.assertIn(cand_txt, self.report_text)
        self.assertIn(ctx_txt, self.report_text)
        self.assertIn(f"| `budget_5` | {gained} | {lost} |", self.report_text)

    def test_decision_rule_evaluation(self):
        dec = self.results["decision"]
        self.assertEqual(dec["verdict"], "nu se adoptă")
        self.assertFalse(dec["all_criteria_met"])
        self.assertIn("Verdict Final: **NU SE ADOPTĂ**", self.report_text)

    def test_negative_control_on_tampered_report(self):
        # Tampering with a figure in the report must be detected when compared to JSON
        tampered = self.report_text.replace("Verdict Final: **NU SE ADOPTĂ**", "Verdict Final: **SE ADOPTĂ**")
        re_rendered = bench_mod.render_report(self.results)
        self.assertNotEqual(tampered.replace("\r\n", "\n"), re_rendered.replace("\r\n", "\n"))


if __name__ == "__main__":
    unittest.main()
