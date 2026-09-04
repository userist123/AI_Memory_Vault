#!/usr/bin/env python3
"""Manually append an outcome label for a real Council execution.

This is telemetry/evidence only. It does not modify canonical memory and does
not enqueue or promote MemoryCandidate records. Labels are append-only JSONL.
"""
from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_LEDGER = ROOT / "reports" / "outcomes" / "council_outcomes.jsonl"
ALLOWED_OUTCOMES = {"success", "partial", "failure"}
ALLOWED_SOURCES = {"human", "automatic", "hybrid"}


def append_outcome(
    run_id: str,
    outcome: str,
    *,
    reason: str = "",
    evidence: list[str] | None = None,
    source: str = "human",
    confidence: float | None = None,
    ledger_path: Path = DEFAULT_LEDGER,
) -> dict[str, Any]:
    if not run_id.strip():
        raise ValueError("run_id must not be empty")
    if outcome not in ALLOWED_OUTCOMES:
        raise ValueError(f"outcome must be one of {sorted(ALLOWED_OUTCOMES)}")
    if source not in ALLOWED_SOURCES:
        raise ValueError(f"source must be one of {sorted(ALLOWED_SOURCES)}")
    if confidence is not None and not 0.0 <= confidence <= 1.0:
        raise ValueError("confidence must be between 0 and 1")

    record = {
        "run_id": run_id,
        "outcome": outcome,
        "label_source": source,
        "confidence": confidence,
        "reason": reason,
        "evidence": evidence or [],
        "labeled_at": datetime.now(timezone.utc).isoformat(),
    }
    ledger_path.parent.mkdir(parents=True, exist_ok=True)
    with ledger_path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(record, ensure_ascii=False, sort_keys=True) + "\n")
    return record


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Append a manual Council outcome label")
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--outcome", choices=sorted(ALLOWED_OUTCOMES), required=True)
    parser.add_argument("--reason", default="")
    parser.add_argument("--evidence", action="append", default=[])
    parser.add_argument("--source", choices=sorted(ALLOWED_SOURCES), default="human")
    parser.add_argument("--confidence", type=float, default=None)
    parser.add_argument("--ledger", type=Path, default=DEFAULT_LEDGER)
    args = parser.parse_args(argv)

    record = append_outcome(
        args.run_id,
        args.outcome,
        reason=args.reason,
        evidence=args.evidence,
        source=args.source,
        confidence=args.confidence,
        ledger_path=args.ledger,
    )
    print(json.dumps(record, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
