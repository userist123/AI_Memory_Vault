"""test_generate_b4_baseline.py — Verification for scripts/generate_b4_baseline.py."""
from __future__ import annotations

import json
from pathlib import Path

from scripts.generate_b4_baseline import generate_b4_baseline, main


def test_generate_b4_baseline_execution(tmp_path: Path):
    reports_dir = tmp_path / "reports"

    # Run generator with 3 runs and fixed seed
    report = generate_b4_baseline(runs=3, seed=42, output_dir=reports_dir)

    b4_dir = reports_dir / "b4"
    b5_dir = reports_dir / "b5_baseline"

    # 1. Verify 3 individual B4 JSON files exist
    b4_files = sorted(b4_dir.glob("run_*.json"))
    assert len(b4_files) == 3
    for i, b4_file in enumerate(b4_files):
        assert b4_file.name == f"run_{i:04d}.json"
        data = json.loads(b4_file.read_text(encoding="utf-8"))
        assert data["run_id"] == f"run_{i:04d}"
        assert data["total_model_calls"] >= 3  # at least 2 specialists + 1 synthesis
        assert data["actual_total"] > 0

    # 2. Verify b5_report.json exists, is valid JSON, and has run_count == 3
    b5_json_file = b5_dir / "b5_report.json"
    assert b5_json_file.exists()
    b5_data = json.loads(b5_json_file.read_text(encoding="utf-8"))
    assert b5_data["run_count"] == 3
    assert report.run_count == 3
    assert b5_data["total_model_calls"] > 0

    # 3. Verify B5_TOKEN_EFFICIENCY_REPORT.md contains "Council efficiency verdict"
    md_file = b5_dir / "B5_TOKEN_EFFICIENCY_REPORT.md"
    assert md_file.exists()
    md_content = md_file.read_text(encoding="utf-8")
    assert "Council efficiency verdict" in md_content

    # 4. Verify all CSV files were created and are non-empty
    csv_files = ["b5_runs.csv", "b5_agents.csv", "b5_tiers.csv", "b5_models.csv"]
    for csv_name in csv_files:
        csv_path = b5_dir / csv_name
        assert csv_path.exists()
        assert len(csv_path.read_text(encoding="utf-8").strip()) > 0


def test_generate_b4_baseline_cli(tmp_path: Path):
    reports_dir = tmp_path / "cli_reports"
    exit_code = main(["--runs", "2", "--seed", "123", "--output-dir", str(reports_dir)])
    assert exit_code == 0

    b4_files = list((reports_dir / "b4").glob("run_*.json"))
    assert len(b4_files) == 2

    b5_json_file = reports_dir / "b5_baseline" / "b5_report.json"
    assert b5_json_file.exists()
    b5_data = json.loads(b5_json_file.read_text(encoding="utf-8"))
    assert b5_data["run_count"] == 2
