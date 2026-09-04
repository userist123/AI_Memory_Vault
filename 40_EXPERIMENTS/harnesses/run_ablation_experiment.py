"""Runner script for Memory Ablation Benchmark (TASK: MEMORY_ABLATION_01).

Executes 20 paired tasks (40 total trials) comparing:
  CONTROL: No retrieved memory (empty context)
  TREATMENT: Secure memory retrieval via MemoryController.search()
Using real Ollama local model (qwen2.5-coder:3b).

Results are exported to a unique run directory so historical benchmark artifacts
are never overwritten.
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))
os.chdir(REPO_ROOT)

# Test-only secret required by the secure memory gateway. Production credentials
# must come from the environment and are never written by this harness.
os.environ["MEMORY_CONTROLLER_HMAC_SECRET"] = os.getenv(
    "MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_for_ablation_harness_32chars"
)

try:
    os.environ["GIT_COMMIT_SHA"] = subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=REPO_ROOT, text=True
    ).strip()
except (OSError, subprocess.CalledProcessError) as exc:
    raise SystemExit(f"Cannot determine canonical git commit: {exc}") from exc

from cognitive_core.local_provider import LocalProvider
from cognitive_core.memory_ablation_benchmark import (
    MemoryAblationExperimentRunner,
    export_ablation_artifacts,
    get_ablation_benchmark_tasks,
)
from cognitive_core.real_execution_harness import (
    AgentModelExecutor,
    RealAgentExecutionHarness,
)
from cognitive_core.recall_cli import get_memory_controller


def main() -> None:
    print("=" * 70)
    print("STARTING CONTROLLED MEMORY ABLATION EXPERIMENT (TASK: MEMORY_ABLATION_01)")
    print("=" * 70)
    print(f"Git Commit: {os.environ['GIT_COMMIT_SHA']}")

    model_name = os.getenv("REAL_PROVIDER_MODEL", "qwen2.5-coder:3b")
    base_url = os.getenv("REAL_PROVIDER_BASE_URL", "http://127.0.0.1:11434")

    provider = LocalProvider(model_name=model_name, base_url=base_url)
    health = provider.health()
    print(f"Provider Health: {health}")
    if health.get("status") != "ok":
        print("ERROR: Provider is not healthy. Aborting.")
        sys.exit(1)

    model_executor = AgentModelExecutor(
        provider_mode="local",
        provider=provider,
        model_name=model_name,
    )

    controller = get_memory_controller()
    run_id = os.getenv("NIGHTLY_RUN_ID") or datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    trace_dir = Path("07_EVALUATION") / "nightly_master_task_v1_runs" / run_id / "traces"
    trace_dir.mkdir(parents=True, exist_ok=True)
    harness = RealAgentExecutionHarness(
        memory_controller=controller,
        trace_dir=trace_dir,
        model_executor=model_executor,
    )

    tasks = get_ablation_benchmark_tasks()
    ws_base = Path("07_EVALUATION") / "nightly_master_task_v1_runs" / run_id / "ablation_workspaces"
    if ws_base.exists():
        shutil.rmtree(ws_base, ignore_errors=True)
    ws_base.mkdir(parents=True, exist_ok=True)

    runner = MemoryAblationExperimentRunner(
        harness=harness,
        model_executor=model_executor,
        experiment_id=f"exp_ablation_{run_id}",
        base_workspace_dir=ws_base,
        tasks=tasks,
    )

    print(f"Benchmark Version: {runner.benchmark_version}")
    print(f"Benchmark Hash: {runner.benchmark_hash}")
    print(f"Task Count: {len(tasks)} (Total Trials: {len(tasks) * 2})")
    print(f"Executing paired trials against real model '{model_name}'...\n")

    t_start = time.perf_counter()
    summary, paired_results = runner.run_benchmark()
    elapsed_total = round(time.perf_counter() - t_start, 2)

    if ws_base.exists():
        shutil.rmtree(ws_base, ignore_errors=True)

    output_dir = Path("07_EVALUATION") / "nightly_master_task_v1_runs" / run_id / "ablation"
    json_path, md_path = export_ablation_artifacts(
        summary=summary,
        paired_results=paired_results,
        output_dir=output_dir,
        date_slug=run_id,
    )

    print("\n" + "=" * 70)
    print("EXPERIMENT EXECUTION COMPLETE")
    print("=" * 70)
    print(f"Total Wall Clock Time: {elapsed_total}s")
    print(f"Control Successes: {summary.control_successes}/{summary.control_trials} ({summary.control_success_rate * 100:.1f}%)")
    print(f"Treatment Successes: {summary.treatment_successes}/{summary.treatment_trials} ({summary.treatment_success_rate * 100:.1f}%)")
    print(f"Absolute Delta: {summary.absolute_delta * 100:+.1f} percentage points")
    print(f"Relative Delta: {summary.relative_delta:+.1f}%")
    print(f"Conclusion Status: {summary.conclusion_status}")
    print(f"JSON Artifact: {json_path}")
    print(f"Markdown Artifact: {md_path}")
    print("=" * 70)


if __name__ == "__main__":
    main()
