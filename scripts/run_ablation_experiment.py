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

# Setup environment. CI must provide a real secret; local ablation runs use a
# deterministic low-entropy test value that cannot be mistaken for a secret.
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
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
