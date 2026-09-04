"""Master runner for NIGHTLY MASTER TASK V1.

This orchestration layer never fabricates an execution result. Each gate writes
stdout/stderr and an explicit status (PASS, FAIL, NOT_RUN, NOT_FOUND) into a
unique run directory. Historical benchmark artifacts are never overwritten.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Sequence

ROOT = Path(__file__).resolve().parents[1]
RUNS_ROOT = ROOT / "07_EVALUATION" / "nightly_master_task_v1_runs"


@dataclass
class GateResult:
    name: str
    status: str
    command: list[str]
    returncode: int | None
    duration_seconds: float
    stdout_file: str
    stderr_file: str
    note: str = ""


def run_id() -> str:
    forced = os.getenv("NIGHTLY_RUN_ID", "").strip()
    if forced:
        if not re.fullmatch(r"[A-Za-z0-9_.-]+", forced):
            raise SystemExit("NIGHTLY_RUN_ID contains unsafe characters")
        return forced
    return datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")


def current_commit() -> str:
    proc = subprocess.run(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"git rev-parse HEAD failed: {proc.stderr.strip()}")
    return proc.stdout.strip()


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def run_gate(
    *,
    name: str,
    command: Sequence[str],
    out_dir: Path,
    env: dict[str, str] | None = None,
) -> GateResult:
    safe_name = re.sub(r"[^A-Za-z0-9_.-]+", "_", name)
    stdout_path = out_dir / f"{safe_name}.stdout.txt"
    stderr_path = out_dir / f"{safe_name}.stderr.txt"
    started = time.perf_counter()
    proc = subprocess.run(
        list(command), cwd=ROOT, env=env, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False,
    )
    duration = round(time.perf_counter() - started, 3)
    write_text(stdout_path, proc.stdout)
    write_text(stderr_path, proc.stderr)
    return GateResult(
        name=name,
        status="PASS" if proc.returncode == 0 else "FAIL",
        command=list(command),
        returncode=proc.returncode,
        duration_seconds=duration,
        stdout_file=stdout_path.relative_to(ROOT).as_posix(),
        stderr_file=stderr_path.relative_to(ROOT).as_posix(),
    )


def discover_safety_tests() -> dict[str, list[Path]]:
    roots = [ROOT / "cognitive_core" / "tests", ROOT / "memory_controller" / "tests"]
    patterns = {
        "poisoning": re.compile(r"test_.*poison", re.IGNORECASE),
        "harmful_memory": re.compile(r"test_.*harmful.*memory|test_harmful_memory", re.IGNORECASE),
        "temporal": re.compile(r"test_.*temporal", re.IGNORECASE),
        "provenance": re.compile(r"test_.*provenance", re.IGNORECASE),
    }
    found: dict[str, list[Path]] = {key: [] for key in patterns}
    for root in roots:
        if not root.is_dir():
            continue
        for path in root.rglob("test_*.py"):
            for key, pattern in patterns.items():
                if pattern.search(path.name):
                    found[key].append(path)
    return found


def run_discovered_suites(out_dir: Path) -> list[GateResult]:
    results: list[GateResult] = []
    for category, paths in discover_safety_tests().items():
        if not paths:
            results.append(GateResult(
                name=f"dedicated_{category}", status="NOT_FOUND", command=[],
                returncode=None, duration_seconds=0.0, stdout_file="", stderr_file="",
                note="No dedicated executable test module matching the canonical category was found.",
            ))
            continue
        command = [sys.executable, "-m", "pytest", "-q", *[p.relative_to(ROOT).as_posix() for p in sorted(paths)]]
        results.append(run_gate(name=f"dedicated_{category}", command=command, out_dir=out_dir))
    return results


def main() -> int:
    parser = argparse.ArgumentParser(description="NIGHTLY MASTER TASK V1 master runner")
    parser.add_argument("--mode", choices=["plan", "deterministic", "real-ablation", "full"], default="plan")
    parser.add_argument("--allow-real-ablation", action="store_true")
    args = parser.parse_args()

    commit = current_commit()
    rid = run_id()
    out_dir = RUNS_ROOT / rid
    if out_dir.exists():
        raise SystemExit(f"Run directory already exists: {out_dir}")
    out_dir.mkdir(parents=True)

    manifest: dict[str, object] = {
        "experiment": "NIGHTLY_MASTER_TASK_V1",
        "run_id": rid,
        "git_commit_sha": commit,
        "started_utc": datetime.now(timezone.utc).isoformat(),
        "mode": args.mode,
        "gates": [],
    }

    plan = {
        "deterministic_regression": [sys.executable, "-m", "pytest", "cognitive_core/tests", "memory_controller/tests", "tests", "-q"],
        "planning_influence": [sys.executable, "-m", "pytest", "-q", "07_EVALUATION/luna/test_planning_influence_mve.py"],
        "planning_influence_runtime": [sys.executable, "07_EVALUATION/luna/planning_influence_mve.py"],
        "real_ablation": [sys.executable, "40_EXPERIMENTS/harnesses/run_ablation_experiment.py"],
        "dedicated_safety": "discover and execute only explicit matching test modules; NOT_FOUND is preserved as a gap",
    }
    write_text(out_dir / "PLAN.json", json.dumps(plan, indent=2) + "\n")

    if args.mode == "plan":
        manifest["gates"] = [
            {"name": name, "status": "PLANNED", "command": command}
            for name, command in plan.items()
        ]
    else:
        gates: list[GateResult] = []
        if args.mode in {"deterministic", "full"}:
            gates.append(run_gate(
                name="deterministic_regression",
                command=plan["deterministic_regression"], out_dir=out_dir,
            ))
            gates.append(run_gate(
                name="planning_influence_unit",
                command=plan["planning_influence"], out_dir=out_dir,
            ))
            gates.append(run_gate(
                name="planning_influence_runtime",
                command=plan["planning_influence_runtime"], out_dir=out_dir,
            ))
            gates.extend(run_discovered_suites(out_dir))

        if args.mode in {"real-ablation", "full"}:
            enabled = args.allow_real_ablation or os.getenv("RUN_REAL_ABLATION") == "1"
            if not enabled:
                gates.append(GateResult(
                    name="real_ablation",
                    status="NOT_RUN",
                    command=plan["real_ablation"],
                    returncode=None,
                    duration_seconds=0.0,
                    stdout_file="",
                    stderr_file="",
                    note="Real-provider ablation requires explicit --allow-real-ablation or RUN_REAL_ABLATION=1.",
                ))
            else:
                env = dict(os.environ)
                env["GIT_COMMIT_SHA"] = commit
                gates.append(run_gate(
                    name="real_ablation",
                    command=plan["real_ablation"], out_dir=out_dir, env=env,
                ))

        manifest["gates"] = [asdict(gate) for gate in gates]

    manifest["finished_utc"] = datetime.now(timezone.utc).isoformat()
    write_text(out_dir / "MANIFEST.json", json.dumps(manifest, indent=2, ensure_ascii=False) + "\n")

    print(f"NIGHTLY_MASTER_TASK_V1_RUN={rid}")
    print(f"GIT_COMMIT_SHA={commit}")
    for gate in manifest["gates"]:  # type: ignore[assignment]
        print(f"{gate['name']}={gate['status']}")
    print(f"EVIDENCE_DIR={out_dir.relative_to(ROOT).as_posix()}")

    statuses = {gate["status"] for gate in manifest["gates"]}  # type: ignore[union-attr]
    return 1 if "FAIL" in statuses else 0


if __name__ == "__main__":
    raise SystemExit(main())
