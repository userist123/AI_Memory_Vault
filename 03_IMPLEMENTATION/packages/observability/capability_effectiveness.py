"""memory_controller/capability_effectiveness.py — Empirical Capability Effectiveness Matrix.

Computes sample-size-calibrated capability effectiveness across task categories
using strictly OBSERVED runtime execution traces and verified outcome events.

Core Invariants:
1. DECLARED != OBSERVED: Unverified declarations/claims are completely excluded.
2. Capability Types: Explicit separation between `skills`, `agents`, `knowledge_refs`, and `procedure_refs`.
3. Controlled Task Categories: Categories must be valid according to `memory_controller/task_categories.py`.
4. Run Deduplication: A single run_id contributes at most ONE observation to any (capability_type, capability_id, task_category) cell.
5. Statistical Rigor: Wilson lower bound confidence intervals and Laplace smoothing are computed via `memory_controller/effectiveness_stats.py`.
6. Sample Size Guard: Cells with < MIN_SAMPLE_SIZE observations are flagged INSUFFICIENT_DATA.
7. Trend Analysis: Compares chronological recent vs previous windows using deterministic criteria.
"""
from __future__ import annotations

import json
import os
from collections import defaultdict
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple

from memory_controller.effectiveness_stats import (
    MIN_SAMPLE_SIZE,
    evaluate_proportion,
    laplace_smoothed_rate,
    wilson_lower_bound,
)
from memory_controller.memory_trace import ObservedMemoryTrace, load_observed_memory_traces
from memory_controller.outcome_tracker import OutcomeRecord, OutcomeTracker
from memory_controller.task_categories import (
    VALID_TASK_CATEGORIES,
    validate_task_category,
)

VALID_CAPABILITY_TYPES: Set[str] = {
    "skills",
    "agents",
    "knowledge_refs",
    "procedure_refs",
}

_TYPE_ALIAS_MAP = {
    "skill": "skills",
    "skills": "skills",
    "agent": "agents",
    "agents": "agents",
    "knowledge": "knowledge_refs",
    "knowledge_ref": "knowledge_refs",
    "knowledge_refs": "knowledge_refs",
    "procedure": "procedure_refs",
    "procedure_ref": "procedure_refs",
    "procedure_refs": "procedure_refs",
}


def normalize_capability_type(cap_type: str) -> str:
    """Normalizes capability type string to canonical plural form."""
    norm = str(cap_type or "").strip().lower()
    if norm in _TYPE_ALIAS_MAP:
        return _TYPE_ALIAS_MAP[norm]
    raise ValueError(
        f"Invalid capability_type '{cap_type}'. Must be one of: {sorted(list(VALID_CAPABILITY_TYPES))}"
    )


@dataclass
class CapabilityCell:
    """Statistical summary cell for a (capability_type, capability_id, task_category) tuple."""

    capability_type: str
    capability_id: str
    task_category: str
    total_runs: int = 0
    success_runs: int = 0
    fail_runs: int = 0
    partial_runs: int = 0
    unknown_runs: int = 0
    observed_rate: float = 0.0
    wilson_lower_bound: float = 0.0
    smoothed_rate: float = 0.0
    status: str = "INSUFFICIENT_DATA"
    min_sample_size: int = MIN_SAMPLE_SIZE
    project_id: Optional[str] = None

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


def _classify_memory_id(memory_id: str) -> Tuple[str, str]:
    """Helper to classify a raw memory note ID into capability type and ID."""
    clean = str(memory_id or "").strip()
    if clean.startswith("PROC-") or clean.startswith("03_PROCEDURES/"):
        return "procedure_refs", clean
    elif clean.startswith("SKILL-") or clean.startswith(".agents/skills/"):
        return "skills", clean
    elif clean.startswith("AGENT-"):
        return "agents", clean
    else:
        return "knowledge_refs", clean


def _extract_run_capabilities(
    traces: List[ObservedMemoryTrace],
) -> Dict[str, Set[str]]:
    """Extracts observed capabilities for a given run_id strictly from ObservedMemoryTrace.

    In accordance with Task 3.1 evidence boundary:
    ObservedMemoryTrace.retrieved_memory_ids is the sole authoritative evidence source.
    OutcomeRecord.observed_capabilities is never used to independently create matrix cells.
    """
    caps: Dict[str, Set[str]] = {
        "skills": set(),
        "agents": set(),
        "knowledge_refs": set(),
        "procedure_refs": set(),
    }

    for trace in traces:
        for mid in trace.retrieved_memory_ids:
            if mid and str(mid).strip():
                c_type, c_id = _classify_memory_id(str(mid).strip())
                caps[c_type].add(c_id)

    return caps


def effectiveness_matrix(
    outcome_records: Optional[List[OutcomeRecord]] = None,
    traces: Optional[List[ObservedMemoryTrace]] = None,
    project_id: Optional[str] = None,
    task_category: Optional[str] = None,
    capability_type: Optional[str] = None,
    capability_id: Optional[str] = None,
    telemetry_dir: Optional[Path | str] = None,
    outcomes_ledger: Optional[Path | str] = None,
    traces_ledger: Optional[Path | str] = None,
    confidence: float = 0.95,
) -> Dict[str, Any]:
    """Computes the empirical capability effectiveness matrix.

    Returns:
        Dictionary with:
        - "matrix": Dict[str, Dict[str, Any]] keyed by '<type>:<id>:<category>'
        - "summary": Dict with counts of cells, valid cells, total runs
        - "metadata": Filter and configuration details
    """
    # 1. Load OutcomeRecords if not provided
    if outcome_records is None:
        if outcomes_ledger:
            tracker = OutcomeTracker(ledger_path=Path(outcomes_ledger))
        else:
            base = Path(telemetry_dir) if telemetry_dir else Path("telemetry")
            tracker = OutcomeTracker(ledger_path=base / "outcomes" / "council_outcomes.jsonl")
        outcome_records = tracker.list_records(project_id=project_id)
    elif project_id is not None:
        outcome_records = [r for r in outcome_records if r.project_id == project_id]

    # Map latest outcome record by run_id
    outcomes_by_run: Dict[str, OutcomeRecord] = {}
    for r in outcome_records:
        outcomes_by_run[r.run_id] = r

    # 2. Load ObservedMemoryTraces if not provided
    if traces is None:
        t_dir = Path(telemetry_dir) if telemetry_dir else None
        traces = load_observed_memory_traces(
            project_id=project_id,
            telemetry_dir=t_dir,
        )
    elif project_id is not None:
        traces = [t for t in traces if t.project_id == project_id]

    # Group traces by run_id
    traces_by_run: Dict[str, List[ObservedMemoryTrace]] = defaultdict(list)
    for t in traces:
        traces_by_run[t.run_id].append(t)

    # 3. Identify all distinct run_ids to analyze
    all_run_ids = set(outcomes_by_run.keys()) | set(traces_by_run.keys())

    # Optional target filter normalizations
    target_type = normalize_capability_type(capability_type) if capability_type else None
    target_category = validate_task_category(task_category) if task_category else None
    target_id = str(capability_id).strip() if capability_id else None

    # Aggregator: (capability_type, capability_id, category) -> outcome counters & chronological events
    cell_stats: Dict[Tuple[str, str, str], Dict[str, int]] = defaultdict(
        lambda: {"success": 0, "fail": 0, "partial": 0, "unknown": 0}
    )

    # Process each run_id exactly once (Anti-Duplication Invariant)
    for r_id in sorted(list(all_run_ids)):
        rec = outcomes_by_run.get(r_id)
        run_traces = traces_by_run.get(r_id, [])

        # Determine task_category
        cat = rec.task_category if rec else "unknown"
        if not cat or cat not in VALID_TASK_CATEGORIES:
            cat = "unknown"

        if target_category and cat != target_category:
            continue

        # Determine outcome
        outcome = rec.outcome if rec else "unknown"
        if outcome not in {"success", "fail", "partial", "unknown"}:
            outcome = "unknown"

        # Extract capabilities observed in this run (strictly from ObservedMemoryTrace)
        run_caps = _extract_run_capabilities(run_traces)

        # Attribute outcome once per (capability_type, capability_id, cat)
        for c_type, ids in run_caps.items():
            if target_type and c_type != target_type:
                continue
            for c_id in ids:
                if target_id and c_id != target_id:
                    continue
                key = (c_type, c_id, cat)
                if outcome == "success":
                    cell_stats[key]["success"] += 1
                elif outcome == "fail":
                    cell_stats[key]["fail"] += 1
                elif outcome == "partial":
                    cell_stats[key]["partial"] += 1
                else:
                    cell_stats[key]["unknown"] += 1

    # 4. Construct output matrix
    matrix: Dict[str, Dict[str, Any]] = {}
    valid_cells_count = 0
    total_cells_count = 0

    for (c_type, c_id, cat), counts in sorted(cell_stats.items()):
        total = counts["success"] + counts["fail"] + counts["partial"] + counts["unknown"]
        successes = counts["success"]

        prop_eval = evaluate_proportion(successes=successes, trials=total, confidence=confidence)

        cell = CapabilityCell(
            capability_type=c_type,
            capability_id=c_id,
            task_category=cat,
            total_runs=total,
            success_runs=counts["success"],
            fail_runs=counts["fail"],
            partial_runs=counts["partial"],
            unknown_runs=counts["unknown"],
            observed_rate=prop_eval["observed_rate"],
            wilson_lower_bound=prop_eval["wilson_lower_bound"],
            smoothed_rate=prop_eval["smoothed_rate"],
            status=prop_eval["status"],
            min_sample_size=MIN_SAMPLE_SIZE,
            project_id=project_id,
        )

        matrix_key = f"{c_type}:{c_id}:{cat}"
        matrix[matrix_key] = cell.to_dict()

        total_cells_count += 1
        if cell.status == "VALID":
            valid_cells_count += 1

    return {
        "matrix": matrix,
        "summary": {
            "total_cells": total_cells_count,
            "valid_cells": valid_cells_count,
            "insufficient_data_cells": total_cells_count - valid_cells_count,
            "total_unique_runs": len(all_run_ids),
        },
        "metadata": {
            "project_id": project_id,
            "task_category": target_category,
            "capability_type": target_type,
            "capability_id": target_id,
            "confidence": confidence,
            "min_sample_size": MIN_SAMPLE_SIZE,
        },
    }


def effectiveness_trend(
    capability_type: str,
    capability_id: str,
    task_category: Optional[str] = None,
    outcome_records: Optional[List[OutcomeRecord]] = None,
    traces: Optional[List[ObservedMemoryTrace]] = None,
    project_id: Optional[str] = None,
    window_size: int = 5,
    min_sample_size: int = MIN_SAMPLE_SIZE,
    telemetry_dir: Optional[Path | str] = None,
    outcomes_ledger: Optional[Path | str] = None,
    traces_ledger: Optional[Path | str] = None,
) -> Dict[str, Any]:
    """Analyzes performance trend (recent vs previous window) for a capability."""
    # Normalize capability type
    c_type = normalize_capability_type(capability_type)
    c_id = str(capability_id).strip()
    target_cat = validate_task_category(task_category) if task_category else None

    # Load records if not passed
    if outcome_records is None:
        if outcomes_ledger:
            tracker = OutcomeTracker(ledger_path=Path(outcomes_ledger))
        else:
            base = Path(telemetry_dir) if telemetry_dir else Path("telemetry")
            tracker = OutcomeTracker(ledger_path=base / "outcomes" / "council_outcomes.jsonl")
        outcome_records = tracker.list_records(project_id=project_id)
    elif project_id is not None:
        outcome_records = [r for r in outcome_records if r.project_id == project_id]

    outcomes_by_run: Dict[str, OutcomeRecord] = {}
    for r in outcome_records:
        outcomes_by_run[r.run_id] = r

    if traces is None:
        t_dir = Path(telemetry_dir) if telemetry_dir else None
        traces = load_observed_memory_traces(
            project_id=project_id,
            telemetry_dir=t_dir,
        )
    elif project_id is not None:
        traces = [t for t in traces if t.project_id == project_id]

    traces_by_run: Dict[str, List[ObservedMemoryTrace]] = defaultdict(list)
    for t in traces:
        traces_by_run[t.run_id].append(t)

    all_run_ids = set(outcomes_by_run.keys()) | set(traces_by_run.keys())

    # Collect chronological events where (c_type, c_id) was observed
    events: List[Tuple[str, str, str]] = []  # (timestamp, run_id, outcome)

    for r_id in all_run_ids:
        rec = outcomes_by_run.get(r_id)
        run_traces = traces_by_run.get(r_id, [])

        cat = rec.task_category if rec else "unknown"
        if not cat or cat not in VALID_TASK_CATEGORIES:
            cat = "unknown"

        if target_cat and cat != target_cat:
            continue

        outcome = rec.outcome if rec else "unknown"
        ts = rec.timestamp if rec else (run_traces[0].timestamp if run_traces else "")

        run_caps = _extract_run_capabilities(run_traces)
        if c_id in run_caps.get(c_type, set()):
            events.append((ts, r_id, outcome))

    # Sort events chronologically
    events.sort(key=lambda x: (x[0], x[1]))

    total_events = len(events)
    w_size = max(window_size, min_sample_size)

    if total_events < (2 * w_size):
        return {
            "capability_type": c_type,
            "capability_id": c_id,
            "task_category": target_cat,
            "total_runs": total_events,
            "trend": "INSUFFICIENT_DATA",
            "status": "INSUFFICIENT_DATA",
            "recent_rate": None,
            "previous_rate": None,
            "rate_delta": None,
            "window_size": w_size,
            "min_required_runs": 2 * w_size,
        }

    # Split into previous and recent windows
    recent_events = events[-w_size:]
    previous_events = events[-(2 * w_size) : -w_size]

    recent_successes = sum(1 for e in recent_events if e[2] == "success")
    previous_successes = sum(1 for e in previous_events if e[2] == "success")

    recent_rate = recent_successes / float(w_size)
    previous_rate = previous_successes / float(w_size)
    rate_delta = recent_rate - previous_rate

    # Deterministic trend evaluation threshold (>= +0.05 improving, <= -0.05 degrading)
    if rate_delta >= 0.05:
        trend = "IMPROVING"
    elif rate_delta <= -0.05:
        trend = "DEGRADING"
    else:
        trend = "STABLE"

    return {
        "capability_type": c_type,
        "capability_id": c_id,
        "task_category": target_cat,
        "total_runs": total_events,
        "trend": trend,
        "status": "VALID",
        "recent_rate": round(recent_rate, 4),
        "previous_rate": round(previous_rate, 4),
        "rate_delta": round(rate_delta, 4),
        "window_size": w_size,
        "recent_window_runs": w_size,
        "previous_window_runs": w_size,
    }
