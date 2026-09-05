"""Runner script for Memory Ablation Benchmark (TASK: MEMORY_ABLATION_01).

Executes 20 paired tasks (40 total trials) comparing:
  CONTROL: No retrieved memory (empty context)
  TREATMENT: Secure memory retrieval via MemoryController.search()
Using real Ollama local model (qwen2.5-coder:3b).
Exports results to 07_EVALUATION/memory_ablation_2026-09.{json,md}.
"""
from __future__ import annotations

import os
import shutil
import sys
import time
from pathlib import Path

# Add project root to sys.path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
sys.path.insert(0, ".")

# Setup environment
os.environ["MEMORY_CONTROLLER_HMAC_SECRET"] = os.getenv(
    "MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_for_ablation_harness_32chars"
)
os.environ["GIT_COMMIT_SHA"] = "8a72389491dfe02fe3e48f2753e55378ce3ab85b"

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


def main():
    print("=" * 70)
    print("STARTING CONTROLLED MEMORY ABLATION EXPERIMENT (TASK: MEMORY_ABLATION_01)")
    print("=" * 70)

    # 1. Initialize local Ollama provider
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

    # 2. Initialize MemoryController and Harness
    controller = get_memory_controller()
    trace_dir = Path("telemetry")
    harness = RealAgentExecutionHarness(
        memory_controller=controller,
        trace_dir=trace_dir,
        model_executor=model_executor,
    )

    # 3. Setup Experiment Runner
    tasks = get_ablation_benchmark_tasks()
    ws_base = Path("telemetry/ablation_trials_ws")
    if ws_base.exists():
        shutil.rmtree(ws_base, ignore_errors=True)
    ws_base.mkdir(parents=True, exist_ok=True)

    runner = MemoryAblationExperimentRunner(
        harness=harness,
        model_executor=model_executor,
        experiment_id="exp_ablation_202609_01",
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

    # 4. Clean up temporary trial workspaces
    if ws_base.exists():
        shutil.rmtree(ws_base, ignore_errors=True)

    # 5. Export Artifacts
    json_path, md_path = export_ablation_artifacts(
        summary=summary,
        paired_results=paired_results,
        output_dir="07_EVALUATION",
        date_slug="2026-09",
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
