"""memory_controller/memory_trace.py — Runtime Observed Memory Trace Emitter.

Captures the deterministic, machine-verifiable set of memory IDs that were ACTUALLY
included in the final context pack delivered to the model.

Invariants:
1. OBSERVED = Memory was present in the final context pack.
2. OBSERVED != USED (Causal influence remains unmeasured and out of scope).
3. Passive execution: Telemetry failures NEVER alter normal context pack construction.
4. Append-only persistence under telemetry/ (isolated from canonical vault notes).
5. Data minimization: Only IDs, scores, and token/byte metrics are logged (no prompt/content leakage).
"""
from __future__ import annotations

import json
import os
import threading
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Set


DEFAULT_TELEMETRY_DIR = Path(os.getenv("ANTIGRAVITY_TELEMETRY_DIR", "telemetry"))
_TRACE_LOCK = threading.Lock()


@dataclass
class ObservedMemoryTrace:
    run_id: str
    timestamp: str
    retrieved_memory_ids: List[str]
    retrieval_scores: Dict[str, float] = field(default_factory=dict)
    context_size_bytes: int = 0
    estimated_tokens: int = 0
    project_id: Optional[str] = None

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> ObservedMemoryTrace:
        return cls(
            run_id=data.get("run_id", "unknown_run"),
            timestamp=data.get("timestamp", datetime.now(timezone.utc).isoformat()),
            retrieved_memory_ids=data.get("retrieved_memory_ids", []),
            retrieval_scores=data.get("retrieval_scores", {}),
            context_size_bytes=int(data.get("context_size_bytes", 0)),
            estimated_tokens=int(data.get("estimated_tokens", 0)),
            project_id=data.get("project_id"),
        )


def _resolve_trace_path(telemetry_dir: Optional[Path] = None) -> Path:
    if telemetry_dir is not None:
        base = Path(telemetry_dir)
    else:
        base = Path(os.getenv("ANTIGRAVITY_TELEMETRY_DIR", "telemetry"))
    base.mkdir(parents=True, exist_ok=True)
    return base / "observed_memory_traces.jsonl"



def record_observed_memory_trace(
    run_id: str,
    results: List[Dict[str, Any]],
    context_size_bytes: int = 0,
    estimated_tokens: int = 0,
    telemetry_dir: Optional[Path] = None,
    project_id: Optional[str] = None,
) -> Optional[ObservedMemoryTrace]:
    """Passively records the exact memory IDs present in the final context pack."""
    try:
        now_ts = datetime.now(timezone.utc).isoformat()
        memory_ids: List[str] = []
        scores: Dict[str, float] = {}

        for item in results:
            if not isinstance(item, dict):
                continue
            m_id = item.get("id") or item.get("note_id") or item.get("path")
            if m_id and isinstance(m_id, str) and m_id not in memory_ids:
                memory_ids.append(m_id)
                # Capture existing score if available, without recalculation
                score_val = item.get("score")
                if score_val is None:
                    score_val = item.get("relevance_score")
                if score_val is not None:
                    try:
                        scores[m_id] = round(float(score_val), 4)
                    except (ValueError, TypeError):
                        pass

        trace = ObservedMemoryTrace(
            run_id=run_id or "unspecified_run",
            timestamp=now_ts,
            retrieved_memory_ids=memory_ids,
            retrieval_scores=scores,
            context_size_bytes=context_size_bytes,
            estimated_tokens=estimated_tokens,
            project_id=str(project_id).strip() if project_id else None,
        )

        trace_path = _resolve_trace_path(telemetry_dir)
        with _TRACE_LOCK:
            with open(trace_path, "a", encoding="utf-8") as f:
                f.write(json.dumps(trace.to_dict()) + "\n")

        return trace
    except Exception:
        # Passive hook: Never crash context construction on telemetry error
        return None


def load_observed_memory_traces(
    run_id: Optional[str] = None,
    telemetry_dir: Optional[Path] = None,
    project_id: Optional[str] = None,
) -> List[ObservedMemoryTrace]:
    """Loads recorded traces from append-only telemetry storage."""
    trace_path = _resolve_trace_path(telemetry_dir)
    if not trace_path.exists():
        return []

    traces: List[ObservedMemoryTrace] = []
    with _TRACE_LOCK:
        try:
            with open(trace_path, "r", encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if not line:
                        continue
                    try:
                        data = json.loads(line)
                        trace = ObservedMemoryTrace.from_dict(data)
                        if run_id is not None and trace.run_id != run_id:
                            continue
                        if project_id is not None and trace.project_id != project_id:
                            continue
                        traces.append(trace)
                    except Exception:
                        continue
        except Exception:
            return []
    return traces



def reconcile_observed_trace(
    declared_memory_ids: List[str],
    observed_memory_ids: Optional[List[str]],
) -> Dict[str, Any]:
    """Reconciles agent-declared memory IDs against deterministically observed memory IDs."""
    declared_set = set(declared_memory_ids or [])
    if observed_memory_ids is None:
        return {
            "status": "OBSERVATION_FAILED",
            "declared_count": len(declared_set),
            "observed_count": 0,
            "acknowledged": [],
            "fabrications": sorted(list(declared_set)),
            "unacknowledged": [],
        }

    observed_set = set(observed_memory_ids)

    acknowledged = sorted(list(declared_set.intersection(observed_set)))
    fabrications = sorted(list(declared_set.difference(observed_set)))
    unacknowledged = sorted(list(observed_set.difference(declared_set)))

    if not fabrications and not unacknowledged:
        status = "ACKNOWLEDGED_CLEAN"
    elif fabrications and not unacknowledged:
        status = "FABRICATION_DETECTED"
    elif unacknowledged and not fabrications:
        status = "UNACKNOWLEDGED_RETRIEVAL"
    else:
        status = "PARTIAL_MISMATCH"

    return {
        "status": status,
        "declared_count": len(declared_set),
        "observed_count": len(observed_set),
        "acknowledged": acknowledged,

        "fabrications": fabrications,
        "unacknowledged": unacknowledged,
    }
