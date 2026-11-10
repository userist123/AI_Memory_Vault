#!/usr/bin/env python3
"""scripts/generate_b4_baseline.py — Generates B4 baseline audit reports using FakeModelProvider.

Runs the Council (via B1 execute_council_models) N times using FakeModelProvider,
generates individual B4 audit reports, aggregates them with B5, and exports:
  - reports/b4/run_XXXX.json
  - reports/b5_baseline/b5_report.json
  - reports/b5_baseline/b5_runs.csv
  - reports/b5_baseline/b5_agents.csv
  - reports/b5_baseline/b5_tiers.csv
  - reports/b5_baseline/b5_models.csv
  - reports/b5_baseline/B5_TOKEN_EFFICIENCY_REPORT.md
"""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import random
import sys
import time
from typing import Any, Dict, List, Mapping, Optional, Sequence

# Ensure repository root is on sys.path
REPO_ROOT = Path(__file__).resolve().parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from cognitive_core.council_efficiency_report import (
    CouncilEfficiencyReport,
    build_efficiency_report,
    to_csv_agents,
    to_csv_models,
    to_csv_runs,
    to_csv_tiers,
    to_markdown,
)
from cognitive_core.council_usage_audit import (
    CouncilUsageAuditReport,
    build_audit_report,
)
from cognitive_core.executive_model_execution_bridge import execute_council_models
from cognitive_core.fake_model_provider import FakeModelProvider

AGENT_TIERS: Dict[str, str] = {
    "ROUTER": "light",
    "RETRIEVAL": "light",
    "VERIFIER": "light",
    "CONSOLIDATOR": "standard",
    "CRITIC": "standard",
}
SYNTHESIS_ROLE = "SYNTHESIZER"
SYNTHESIS_TIER = "heavy"


class SubagentStub:
    """Minimal duck-typed SubagentSpec exposing model_tier."""

    def __init__(self, model_tier: str) -> None:
        self.model_tier = model_tier


class FakeCouncilRun:
    """Minimal duck-typed CouncilRun exposing .agent_packs and .telemetry."""

    def __init__(self, agent_packs: Mapping[str, Any]) -> None:
        self.agent_packs = agent_packs
        self.telemetry = object()


def generate_b4_baseline(
    runs: int = 30,
    seed: Optional[int] = None,
    output_dir: Optional[Path | str] = None,
) -> CouncilEfficiencyReport:
    """Execute N synthetic Council runs with FakeModelProvider and export B4/B5 reports."""
    if seed is not None:
        random.seed(seed)

    base_path = Path(output_dir) if output_dir else REPO_ROOT / "reports"
    b4_dir = base_path / "b4"
    b5_dir = base_path / "b5_baseline"

    os.makedirs(b4_dir, exist_ok=True)
    os.makedirs(b5_dir, exist_ok=True)

    available_agents = list(AGENT_TIERS.keys())
    reports: List[CouncilUsageAuditReport] = []

    for i in range(runs):
        run_id = f"run_{i:04d}"

        # 1. Pick 2-5 random agents from AGENT_TIERS
        num_agents = random.randint(2, min(5, len(available_agents)))
        chosen_agents = random.sample(available_agents, num_agents)

        # 2. Build agent_packs with variable content size for realistic distribution
        agent_packs: Dict[str, Any] = {}
        for agent_id in chosen_agents:
            payload_len = random.randint(100, 2000)
            agent_packs[agent_id] = {
                "source": agent_id,
                "content": "x" * payload_len,
                "confidence": round(random.uniform(0.7, 1.0), 2),
            }

        council_run = FakeCouncilRun(agent_packs=agent_packs)

        # 3. Build subagent_specs mapping
        subagent_specs: Dict[str, SubagentStub] = {
            agent_id: SubagentStub(AGENT_TIERS[agent_id])
            for agent_id in chosen_agents
        }
        subagent_specs[SYNTHESIS_ROLE] = SubagentStub(SYNTHESIS_TIER)

        # 4. Execute council models and measure wall time
        t0 = time.perf_counter()
        result = execute_council_models(
            council_run=council_run,
            subagent_specs=subagent_specs,
            task=f"synthetic task {i}",
            synthesis_role=SYNTHESIS_ROLE,
            model_execution_enabled=True,
            provider_factories={
                "fake": lambda m: FakeModelProvider(provider_name="fake", model_name=m)
            },
        )
        elapsed = time.perf_counter() - t0

        # 5. Build and persist B4 report
        b4_report = build_audit_report(
            run_id=run_id,
            council_run_with_execution=result,
            wall_time_seconds=elapsed,
        )

        b4_file = b4_dir / f"{run_id}.json"
        b4_file.write_text(b4_report.to_json(), encoding="utf-8")
        reports.append(b4_report)

    # 6. Aggregate with B5
    b5_report = build_efficiency_report(reports)

    # 7. Export B5 artifacts
    (b5_dir / "b5_report.json").write_text(b5_report.to_json(), encoding="utf-8")
    (b5_dir / "b5_runs.csv").write_text(to_csv_runs(reports), encoding="utf-8")
    (b5_dir / "b5_agents.csv").write_text(to_csv_agents(b5_report), encoding="utf-8")
    (b5_dir / "b5_tiers.csv").write_text(to_csv_tiers(b5_report), encoding="utf-8")
    (b5_dir / "b5_models.csv").write_text(to_csv_models(b5_report), encoding="utf-8")
    (b5_dir / "B5_TOKEN_EFFICIENCY_REPORT.md").write_text(to_markdown(b5_report), encoding="utf-8")

    # 8. Print concise stdout summary
    v = b5_report.verdict
    print("=== B4/B5 Baseline Summary ===")
    print(f"Runs analyzed: {b5_report.run_count}")
    print(f"Total model calls: {b5_report.total_model_calls}")
    print(f"Avg actual tokens per run: {v.avg_actual_tokens_per_run:.1f}")
    print(f"Median actual tokens per run: {v.median_actual_tokens_per_run:.1f}")
    print(f"P95 actual tokens per run: {v.p95_actual_tokens_per_run:.1f}")
    print(f"Estimated vs actual variance: {v.estimated_vs_actual_percent:+.2f}%")
    print(f"Top optimization candidate: {v.top_optimization_candidate}")
    print(f"Top optimization reason: {v.top_optimization_reason}")

    return b5_report


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description="Generate B4 usage audit baseline with FakeModelProvider and aggregate via B5."
    )
    parser.add_argument("--runs", type=int, default=30, help="Number of Council runs (default: 30)")
    parser.add_argument("--seed", type=int, default=None, help="Random seed for reproducible distributions")
    parser.add_argument("--output-dir", type=str, default=None, help="Custom output directory for reports")

    args = parser.parse_args(argv)
    generate_b4_baseline(runs=args.runs, seed=args.seed, output_dir=args.output_dir)
    return 0


if __name__ == "__main__":
    sys.exit(main())
