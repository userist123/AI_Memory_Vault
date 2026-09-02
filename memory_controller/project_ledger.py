"""memory_controller/project_ledger.py — Project Session Ledger & Skill Effectiveness Engine.

Connects individual runs to specific projects and computes real-world skill effectiveness
from confirmed OBSERVED traces and verified outcomes (never declared claims).

Rules:
1. project_id is established once explicitly by human or orchestrator, NEVER inferred by LLM.
2. Join records stored under telemetry/project_sessions.jsonl without duplicating raw telemetry.
3. Reports aggregate only confirmed OBSERVED_TRACE data (source of truth is physical presence).
4. Skill effectiveness is computed strictly from (success_runs / total_observed_runs) per skill.
"""
from __future__ import annotations

import json
import os
import threading
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple

from memory_controller.memory_trace import ObservedMemoryTrace, load_observed_memory_traces
from memory_controller.outcome_tracker import OutcomeRecord, OutcomeTracker

DEFAULT_TELEMETRY_DIR = Path(os.getenv("ANTIGRAVITY_TELEMETRY_DIR", "telemetry"))
_LEDGER_LOCK = threading.Lock()


def _resolve_sessions_path(telemetry_dir: Optional[Path | str] = None) -> Path:
    if telemetry_dir is not None:
        base = Path(telemetry_dir)
    else:
        base = Path(os.getenv("ANTIGRAVITY_TELEMETRY_DIR", "telemetry"))
    base.mkdir(parents=True, exist_ok=True)
    return base / "project_sessions.jsonl"


@dataclass(frozen=True)
class ProjectSessionRecord:
    """Join record mapping a run_id to a specific project_id."""

    project_id: str
    run_id: str
    timestamp: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())

    def __post_init__(self) -> None:
        if not self.project_id or not str(self.project_id).strip():
            raise ValueError("project_id must be a non-empty string")
        if not self.run_id or not str(self.run_id).strip():
            raise ValueError("run_id must be a non-empty string")

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


def record_project_session(
    project_id: str,
    run_id: str,
    timestamp: Optional[str] = None,
    telemetry_dir: Optional[Path | str] = None,
    ledger_path: Optional[Path | str] = None,
) -> ProjectSessionRecord:
    """Links an existing run_id to a project_id. Strictly append-only join."""
    p_id = str(project_id or "").strip()
    r_id = str(run_id or "").strip()
    if not p_id:
        raise ValueError("project_id must be a non-empty string")
    if not r_id:
        raise ValueError("run_id must be a non-empty string")

    ts = timestamp or datetime.now(timezone.utc).isoformat()
    record = ProjectSessionRecord(project_id=p_id, run_id=r_id, timestamp=ts)

    path = Path(ledger_path) if ledger_path else _resolve_sessions_path(telemetry_dir)
    path.parent.mkdir(parents=True, exist_ok=True)

    with _LEDGER_LOCK:
        with open(path, "a", encoding="utf-8") as f:
            f.write(json.dumps(record.to_dict()) + "\n")

    return record


def load_project_sessions(
    project_id: Optional[str] = None,
    telemetry_dir: Optional[Path | str] = None,
    ledger_path: Optional[Path | str] = None,
) -> List[ProjectSessionRecord]:
    """Loads recorded project session mappings."""
    path = Path(ledger_path) if ledger_path else _resolve_sessions_path(telemetry_dir)
    if not path.exists():
        return []

    sessions: List[ProjectSessionRecord] = []
    with _LEDGER_LOCK:
        try:
            with open(path, "r", encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if not line:
                        continue
                    try:
                        data = json.loads(line)
                        rec = ProjectSessionRecord(**data)
                        if project_id is not None and rec.project_id != project_id:
                            continue
                        sessions.append(rec)
                    except Exception:
                        continue
        except Exception:
            return []
    return sessions


def _categorize_item(item_id: str) -> str:
    """Categorize item ID into agent, skill, or knowledge."""
    lower = item_id.lower()
    if lower.startswith("agent-") or lower.startswith("agent_") or "agent" in lower:
        return "agent"
    if lower.startswith("skill-") or lower.startswith("skill_") or "skills" in lower or lower.endswith(".md") and "skill" in lower:
        return "skill"
    return "knowledge"


def project_report(
    project_id: str,
    telemetry_dir: Optional[Path | str] = None,
    ledger_path: Optional[Path | str] = None,
    outcomes_path: Optional[Path | str] = None,
) -> Dict[str, Any]:
    """Generates an aggregated usage and outcome report for a specific project.

    Aggregates:
    - All run_ids associated with the project
    - All agents, skills, and knowledge present in OBSERVED_TRACE
    - Aggregated outcomes (success, fail, partial, unknown)
    """
    p_id = str(project_id or "").strip()
    if not p_id:
        raise ValueError("project_id must be a non-empty string")

    tel_dir = Path(telemetry_dir) if telemetry_dir else DEFAULT_TELEMETRY_DIR

    # 1. Discover all run_ids mapped to this project
    sessions = load_project_sessions(project_id=p_id, telemetry_dir=tel_dir, ledger_path=ledger_path)
    run_ids: Set[str] = {s.run_id for s in sessions}

    # Also check traces and outcomes directly tagged with project_id
    all_traces = load_observed_memory_traces(telemetry_dir=tel_dir)
    for t in all_traces:
        if t.project_id == p_id:
            run_ids.add(t.run_id)

    tracker = OutcomeTracker(ledger_path=outcomes_path or (tel_dir / "outcomes" / "council_outcomes.jsonl"))
    all_outcomes = tracker.list_records(project_id=p_id)
    for o in all_outcomes:
        run_ids.add(o.run_id)

    # 2. Extract OBSERVED items across all matching runs
    observed_agents: Set[str] = set()
    observed_skills: Set[str] = set()
    observed_knowledge: Set[str] = set()

    for r_id in run_ids:
        r_traces = [t for t in all_traces if t.run_id == r_id]
        for t in r_traces:
            for mem_id in t.retrieved_memory_ids:
                cat = _categorize_item(mem_id)
                if cat == "agent":
                    observed_agents.add(mem_id)
                elif cat == "skill":
                    observed_skills.add(mem_id)
                else:
                    observed_knowledge.add(mem_id)

    # 3. Aggregate Outcomes
    outcome_counts = {"success": 0, "fail": 0, "partial": 0, "unknown": 0}
    for r_id in run_ids:
        rec = tracker.get_record(r_id)
        if rec:
            outcome_counts[rec.outcome] = outcome_counts.get(rec.outcome, 0) + 1
        else:
            outcome_counts["unknown"] += 1

    total_runs = len(run_ids)
    m_success = outcome_counts["success"]
    k_fail = outcome_counts["fail"]
    u_unknown = outcome_counts["unknown"] + outcome_counts.get("partial", 0)

    summary_text = (
        f"Proiect {p_id} a folosit agenții {sorted(list(observed_agents))}, "
        f"skill-urile {sorted(list(observed_skills))}, "
        f"din cunoștințele {sorted(list(observed_knowledge))}. "
        f"Din {total_runs} runde: {m_success} success, {k_fail} fail, restul {u_unknown} unknown."
    )

    return {
        "project_id": p_id,
        "run_ids": sorted(list(run_ids)),
        "agents": sorted(list(observed_agents)),
        "skills": sorted(list(observed_skills)),
        "knowledge": sorted(list(observed_knowledge)),
        "outcomes": {
            "total": total_runs,
            "success": m_success,
            "fail": k_fail,
            "partial": outcome_counts.get("partial", 0),
            "unknown": outcome_counts.get("unknown", 0),
        },
        "summary_text": summary_text,
    }


def skill_effectiveness_report(
    telemetry_dir: Optional[Path | str] = None,
    outcomes_path: Optional[Path | str] = None,
) -> Dict[str, Any]:
    """Computes cross-project skill effectiveness strictly from confirmed OBSERVED traces.

    Formula:
        success_rate = (runs with outcome='success' AND skill in OBSERVED_TRACE) /
                       (total runs with skill in OBSERVED_TRACE)

    Hard Invariant:
        Never calculates effectiveness from DECLARED_TRACE.
    """
    tel_dir = Path(telemetry_dir) if telemetry_dir else DEFAULT_TELEMETRY_DIR
    all_traces = load_observed_memory_traces(telemetry_dir=tel_dir)
    tracker = OutcomeTracker(ledger_path=outcomes_path or (tel_dir / "outcomes" / "council_outcomes.jsonl"))

    # Map run_id -> set of observed items
    run_observed_map: Dict[str, Set[str]] = {}
    for t in all_traces:
        if t.run_id not in run_observed_map:
            run_observed_map[t.run_id] = set()
        run_observed_map[t.run_id].update(t.retrieved_memory_ids)

    # Collect all unique skills observed
    all_observed_skills: Set[str] = set()
    for items in run_observed_map.values():
        for item in items:
            if _categorize_item(item) == "skill" or item.startswith("SKILL-") or "skill" in item.lower():
                all_observed_skills.add(item)

    skill_stats: Dict[str, Dict[str, Any]] = {}
    for skill in sorted(list(all_observed_skills)):
        total_observed_runs = 0
        success_runs = 0
        fail_runs = 0
        partial_runs = 0
        unknown_runs = 0

        for r_id, items in run_observed_map.items():
            if skill in items:
                total_observed_runs += 1
                rec = tracker.get_record(r_id)
                if rec:
                    if rec.outcome == "success":
                        success_runs += 1
                    elif rec.outcome == "fail":
                        fail_runs += 1
                    elif rec.outcome == "partial":
                        partial_runs += 1
                    else:
                        unknown_runs += 1
                else:
                    unknown_runs += 1

        success_rate = round(success_runs / total_observed_runs, 3) if total_observed_runs > 0 else 0.0
        success_percentage = round((success_runs / total_observed_runs) * 100, 1) if total_observed_runs > 0 else 0.0

        skill_stats[skill] = {
            "total_observed_runs": total_observed_runs,
            "success_runs": success_runs,
            "fail_runs": fail_runs,
            "partial_runs": partial_runs,
            "unknown_runs": unknown_runs,
            "success_rate": success_rate,
            "success_percentage": success_percentage,
        }

    return {
        "skills": skill_stats,
        "total_skills_analyzed": len(skill_stats),
    }
