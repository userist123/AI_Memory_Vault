"""Unit tests for the graph-budget measurement's aggregation and controls.

The measurement itself runs the whole vault (30_SCRIPTS/evaluation/run_graph_budget_arms.py);
these tests cover only the pure functions, so they are fast and do not touch the
controller or its HMAC secret.
"""
from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT = REPO_ROOT / "30_SCRIPTS" / "evaluation" / "run_graph_budget_arms.py"

_spec = importlib.util.spec_from_file_location("run_graph_budget_arms", SCRIPT)
gb = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(gb)


def row(i, seeds, expanded, cand=1, ctx=0, status="ok"):
    return {"id": i, "seeds": seeds, "expanded": expanded, "candidate_recall": cand,
            "context_recall": ctx, "graph_status": status}


class TestDefaultBudgetControl(unittest.TestCase):
    def test_passes_when_no_nodes_added_with_many_seeds(self):
        rows = [row("a", 200, 0), row("b", 20, 0), row("c", 9, 0), row("d", 5, 3)]
        ctl = gb.default_budget_control(rows)
        self.assertTrue(ctl["passed"])
        self.assertEqual(ctl["queries_expansion_impossible"]["text"], "2/4")
        self.assertEqual(ctl["nodes_added_where_impossible"], 0)

    def test_negative_control_fails_when_nodes_added_with_many_seeds(self):
        # A default-budget arm that adds nodes at 20+ seeds contradicts the
        # documented formula, so the control must fail.
        rows = [row("a", 200, 1), row("b", 9, 0)]
        ctl = gb.default_budget_control(rows)
        self.assertFalse(ctl["passed"])
        self.assertEqual(ctl["nodes_added_where_impossible"], 1)

    def test_boundary_is_inclusive_at_twenty(self):
        self.assertEqual(gb.default_budget_control([row("a", 20, 0)])["queries_expansion_impossible"]["k"], 1)
        self.assertEqual(gb.default_budget_control([row("a", 19, 0)])["queries_expansion_impossible"]["k"], 0)


class TestAggregation(unittest.TestCase):
    def test_raw_fractions_and_means(self):
        rows = [row("a", 200, 5, cand=1, ctx=1), row("b", 200, 0, cand=0, ctx=0),
                row("c", 9, 3, cand=1, ctx=0)]
        s = gb.aggregate_arm(rows)
        self.assertEqual(s["candidate_recall"]["text"], "2/3")
        self.assertEqual(s["context_recall"]["text"], "1/3")
        self.assertEqual(s["queries_with_expansion"]["text"], "2/3")
        self.assertAlmostEqual(s["mean_new_nodes_per_query"], 8 / 3)
        self.assertAlmostEqual(s["mean_new_nodes_when_expanded"], 4.0)

    def test_unmeasurable_cases_leave_the_recall_denominator(self):
        rows = [row("a", 200, 0, cand=1, ctx=1),
                row("b", 200, 0, cand=gb.UNMEASURABLE, ctx=gb.UNMEASURABLE)]
        s = gb.aggregate_arm(rows)
        self.assertEqual(s["candidate_recall"]["text"], "1/1")
        self.assertEqual(s["queries_with_expansion"]["text"], "0/2")

    def test_paired_counts_gain_and_loss(self):
        ref = [row("a", 0, 0, cand=0), row("b", 0, 0, cand=1), row("c", 0, 0, cand=1)]
        arm = [row("a", 9, 2, cand=1), row("b", 9, 2, cand=0), row("c", 9, 2, cand=1)]
        self.assertEqual(gb.paired_vs_reference(ref, arm, "candidate_recall"), {"gained": 1, "lost": 1})

    def test_paired_skips_unmeasurable(self):
        ref = [row("a", 0, 0, cand=gb.UNMEASURABLE)]
        arm = [row("a", 9, 2, cand=1)]
        self.assertEqual(gb.paired_vs_reference(ref, arm, "candidate_recall"), {"gained": 0, "lost": 0})


class TestReportHasNoHandWrittenFigures(unittest.TestCase):
    def _results(self, added_at_default):
        rows_default = [row("a", 200, added_at_default), row("b", 9, 0)]
        rows_20 = [row("a", 200, 20), row("b", 9, 0)]
        rows_off = [row("a", 0, 0), row("b", 0, 0)]

        def arm(rows, on, budget):
            return {"graph_on": on, "budget": budget, "errors": [], "rows": rows,
                    "summary": {**gb.aggregate_arm(rows),
                                "candidate_recall_vs_off": {"gained": 0, "lost": 0},
                                "context_recall_vs_off": {"gained": 0, "lost": 0}}}

        return {
            "benchmark": "x", "ranking_arm": "y", "corpus_notes": 1, "n_cases": 2,
            "seed_distribution_budget_default": gb.seed_distribution(rows_default),
            "default_budget_negative_control": gb.default_budget_control(rows_default),
            "arms": {"graph_off": arm(rows_off, False, None),
                     "budget_default": arm(rows_default, True, None),
                     "budget_20": arm(rows_20, True, 20)},
        }

    def test_report_reflects_control_result(self):
        self.assertIn("Control: **PASS**", gb.render_report(self._results(0)))

    def test_report_flags_failed_control(self):
        report = gb.render_report(self._results(4))
        self.assertIn("Control: **FAIL**", report)
        self.assertNotIn("Control: **PASS**", report)

    def test_report_states_the_small_sample_caveat(self):
        self.assertIn("1–2 cases is noise", gb.render_report(self._results(0)))


if __name__ == "__main__":
    unittest.main()
