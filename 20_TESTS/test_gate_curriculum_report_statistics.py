"""Tests for gate_curriculum_report_statistics.py.

Verifies:
1. Canonical curriculum reports pass statistical audit without error.
2. Negative controls:
   - Missing sample size (N or n).
   - Missing confidence intervals (Wilson score 95% CI).
   - Dual-arm comparative report missing paired statistical tests (McNemar test).
3. JSON and Markdown report format validations.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path
import pytest

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "30_SCRIPTS" / "verification"))

from gate_curriculum_report_statistics import (
    audit_curriculum_reports,
    check_report_statistics,
)


class TestGateCurriculumReportStatistics:
    def test_existing_curriculum_reports_pass_audit(self):
        total, failed = audit_curriculum_reports()
        assert total > 0, "Expected at least one curriculum report to be audited"
        assert failed == 0, f"Expected 0 audit failures, but got {failed}/{total}"

    def test_markdown_positive_control(self):
        valid_md = """# Benchmark Report (N = 12)
## Results
- Control: 0/12 ([0.000, 0.242])
- Treatment: 7/12 ([0.320, 0.807])
## Statistical Analysis
Exact McNemar paired test p-value: 0.01562.
"""
        ok, errors = check_report_statistics(valid_md, is_json=False)
        assert ok is True
        assert len(errors) == 0

    def test_markdown_negative_control_missing_sample_size(self):
        bad_md = """# Benchmark Report
## Results
- Control: 0/12 ([0.000, 0.242])
- Treatment: 7/12 ([0.320, 0.807])
## Statistical Analysis
Exact McNemar paired test p-value: 0.01562.
"""
        ok, errors = check_report_statistics(bad_md, is_json=False)
        assert ok is False
        assert any("sample size" in e.lower() for e in errors)

    def test_markdown_negative_control_missing_ci(self):
        bad_md = """# Benchmark Report (n=12)
## Results
- Control: 0/12
- Treatment: 7/12
## Statistical Analysis
Exact McNemar paired test p-value: 0.01562.
"""
        ok, errors = check_report_statistics(bad_md, is_json=False)
        assert ok is False
        assert any("confidence interval" in e.lower() for e in errors)

    def test_markdown_negative_control_dual_arm_missing_mcnemar(self):
        bad_md = """# Benchmark Report (n=12)
## Results
- Control: 0/12 ([0.000, 0.242])
- Treatment: 7/12 ([0.320, 0.807])
## Summary
Treatment performed better than control by 7 questions.
"""
        ok, errors = check_report_statistics(bad_md, is_json=False)
        assert ok is False
        assert any("mcnemar" in e.lower() or "paired" in e.lower() for e in errors)

    def test_json_positive_control(self):
        valid_json_data = {
            "control_arm": {
                "review_questions": {
                    "total": 12,
                    "wilson_95_ci": {"lower": 0.0, "upper": 0.242}
                }
            },
            "treatment_arm": {
                "review_questions": {
                    "total": 12,
                    "wilson_95_ci": {"lower": 0.320, "upper": 0.807}
                }
            },
            "paired_statistics": {
                "sample_size_n": 12,
                "mcnemar_exact": {"two_sided_p_value": 0.01562}
            }
        }
        ok, errors = check_report_statistics(json.dumps(valid_json_data), is_json=True)
        assert ok is True
        assert len(errors) == 0

    def test_json_negative_control_missing_sample_size(self):
        bad_json_data = {
            "control_arm": {
                "review_questions": {
                    "wilson_95_ci": {"lower": 0.0, "upper": 0.242}
                }
            },
            "treatment_arm": {
                "review_questions": {
                    "wilson_95_ci": {"lower": 0.320, "upper": 0.807}
                }
            },
            "paired_statistics": {
                "mcnemar_exact": {"two_sided_p_value": 0.01562}
            }
        }
        ok, errors = check_report_statistics(json.dumps(bad_json_data), is_json=True)
        assert ok is False
        assert any("sample size" in e.lower() for e in errors)

    def test_json_negative_control_missing_ci(self):
        bad_json_data = {
            "control_arm": {
                "review_questions": {
                    "total": 12
                }
            },
            "treatment_arm": {
                "review_questions": {
                    "total": 12
                }
            },
            "paired_statistics": {
                "sample_size_n": 12,
                "mcnemar_exact": {"two_sided_p_value": 0.01562}
            }
        }
        ok, errors = check_report_statistics(json.dumps(bad_json_data), is_json=True)
        assert ok is False
        assert any("wilson_95_ci" in e.lower() for e in errors)

    def test_json_negative_control_dual_arm_missing_mcnemar(self):
        bad_json_data = {
            "control_arm": {
                "review_questions": {
                    "total": 12,
                    "wilson_95_ci": {"lower": 0.0, "upper": 0.242}
                }
            },
            "treatment_arm": {
                "review_questions": {
                    "total": 12,
                    "wilson_95_ci": {"lower": 0.320, "upper": 0.807}
                }
            },
            "paired_statistics": {
                "sample_size_n": 12
            }
        }
        ok, errors = check_report_statistics(json.dumps(bad_json_data), is_json=True)
        assert ok is False
        assert any("mcnemar" in e.lower() for e in errors)
