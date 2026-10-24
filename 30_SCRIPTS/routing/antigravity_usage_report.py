#!/usr/bin/env python3
"""Antigravity Usage and Token Telemetry Report.

Parses native Antigravity transcript logs (transcript.jsonl) from
~/.gemini/antigravity/brain/*/logs/transcript.jsonl or an explicitly provided directory.

Design & Invariants:
1. Strict separation of uncached input, cached input, and output tokens.
2. Safe offline execution: Read-only access to transcripts, zero network calls, zero external deps.
3. Robust error handling: Corrupt JSONL lines, negative numbers, missing fields, and non-token events
   are tracked and reported without crashing.
4. Nomenclature precision: Uses `mean_uncached_input_tokens_per_measured_step`, avoiding confusing 'weighted' labels.
5. Financial estimates disabled by default. If --rate-card is provided, results are explicitly marked
   as user-configured approximations, never actual billed invoices.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Dict, Iterator, List, Optional, Tuple


@dataclass(frozen=True)
class RateCard:
    """User-configured estimation rate card (cost per million tokens)."""
    uncached_input_per_m: float = 0.0
    cached_input_per_m: float = 0.0
    output_per_m: float = 0.0
    name: str = "custom"


@dataclass
class StepRecord:
    step_index: int
    created_at: Optional[str]
    uncached_input_tokens: int
    cached_input_tokens: int
    output_tokens: int
    total_context_tokens: int


@dataclass
class SessionTelemetry:
    session_id: str
    session_path: str
    total_events: int = 0
    measured_steps: int = 0
    unmetered_steps: int = 0
    corrupt_lines: int = 0
    invalid_field_events: int = 0

    total_uncached_input_tokens: int = 0
    total_cached_input_tokens: int = 0
    total_output_tokens: int = 0
    total_context_tokens: int = 0

    mean_uncached_input_tokens_per_measured_step: float = 0.0
    median_uncached_input_tokens_per_measured_step: float = 0.0
    min_uncached_input_tokens_per_measured_step: int = 0
    max_uncached_input_tokens_per_measured_step: int = 0
    cache_hit_rate: float = 0.0

    estimated_cost_usd: Optional[float] = None
    rate_card_applied: Optional[str] = None

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


def parse_rate_card(rate_str: Optional[str]) -> Optional[RateCard]:
    """Parse format 'uncached,cached,output' in USD per million tokens."""
    if not rate_str:
        return None
    parts = [p.strip() for p in rate_str.split(",")]
    if len(parts) != 3:
        raise ValueError(f"Invalid rate card format '{rate_str}'. Expected 'uncached,cached,output' (e.g. '0.15,0.0375,0.60')")
    try:
        u, c, o = float(parts[0]), float(parts[1]), float(parts[2])
        if u < 0 or c < 0 or o < 0:
            raise ValueError("Token prices cannot be negative.")
        return RateCard(uncached_input_per_m=u, cached_input_per_m=c, output_per_m=o, name=f"custom({u}/{c}/{o})")
    except ValueError as e:
        raise ValueError(f"Invalid numeric values in rate card: {e}") from e


def compute_median(values: List[int]) -> float:
    if not values:
        return 0.0
    s = sorted(values)
    n = len(s)
    mid = n // 2
    if n % 2 == 1:
        return float(s[mid])
    return (s[mid - 1] + s[mid]) / 2.0


def parse_transcript_file(tpath: Path, rate_card: Optional[RateCard] = None) -> SessionTelemetry:
    """Safely parse a single Antigravity transcript file."""
    # Path layout: <brain_dir>/<session_id>/.system_generated/logs/transcript.jsonl
    session_id = tpath.parent.parent.parent.name if tpath.parent.parent.name == ".system_generated" else tpath.parent.name
    telemetry = SessionTelemetry(
        session_id=session_id,
        session_path=str(tpath.resolve())
    )

    if not tpath.is_file():
        return telemetry

    step_uncached: List[int] = []

    try:
        with open(tpath, mode="r", encoding="utf-8", errors="replace") as f:
            for line_idx, line in enumerate(f, 1):
                line_str = line.strip()
                if not line_str:
                    continue
                telemetry.total_events += 1
                try:
                    data = json.loads(line_str)
                except Exception:
                    telemetry.corrupt_lines += 1
                    continue

                if not isinstance(data, dict):
                    telemetry.corrupt_lines += 1
                    continue

                # Check for token metrics
                has_inp = "input_tokens" in data
                has_cache = "cache_read_tokens" in data
                has_out = "output_tokens" in data

                if not has_inp and not has_cache and not has_out:
                    telemetry.unmetered_steps += 1
                    continue

                # Validate types and non-negativity
                inp_val = data.get("input_tokens")
                cache_val = data.get("cache_read_tokens", 0)
                out_val = data.get("output_tokens", 0)

                # Ensure all present values are non-negative ints
                valid = True
                for val in (inp_val, cache_val, out_val):
                    if val is not None:
                        if not isinstance(val, int) or isinstance(val, bool) or val < 0:
                            valid = False
                            break

                if not valid or inp_val is None:
                    telemetry.invalid_field_events += 1
                    continue

                # Verified measured step
                telemetry.measured_steps += 1
                c_val = cache_val if cache_val is not None else 0
                o_val = out_val if out_val is not None else 0

                telemetry.total_uncached_input_tokens += inp_val
                telemetry.total_cached_input_tokens += c_val
                telemetry.total_output_tokens += o_val
                telemetry.total_context_tokens += (inp_val + c_val)
                step_uncached.append(inp_val)

    except OSError:
        # File unreadable or locked
        return telemetry

    if telemetry.measured_steps > 0:
        telemetry.mean_uncached_input_tokens_per_measured_step = round(
            telemetry.total_uncached_input_tokens / telemetry.measured_steps, 2
        )
        telemetry.median_uncached_input_tokens_per_measured_step = round(
            compute_median(step_uncached), 2
        )
        telemetry.min_uncached_input_tokens_per_measured_step = min(step_uncached)
        telemetry.max_uncached_input_tokens_per_measured_step = max(step_uncached)

        tot_in = telemetry.total_uncached_input_tokens + telemetry.total_cached_input_tokens
        if tot_in > 0:
            telemetry.cache_hit_rate = round(telemetry.total_cached_input_tokens / tot_in, 4)

        if rate_card is not None:
            c_uncached = (telemetry.total_uncached_input_tokens / 1_000_000.0) * rate_card.uncached_input_per_m
            c_cached = (telemetry.total_cached_input_tokens / 1_000_000.0) * rate_card.cached_input_per_m
            c_out = (telemetry.total_output_tokens / 1_000_000.0) * rate_card.output_per_m
            telemetry.estimated_cost_usd = round(c_uncached + c_cached + c_out, 4)
            telemetry.rate_card_applied = rate_card.name

    return telemetry


def find_antigravity_transcripts(brain_root: Path) -> List[Path]:
    """Find all transcript.jsonl under brain_root/<session_id>/.system_generated/logs/."""
    transcripts = []
    if not brain_root.is_dir():
        return transcripts
    for item in brain_root.iterdir():
        if item.is_dir() and not item.name.startswith("."):
            t = item / ".system_generated" / "logs" / "transcript.jsonl"
            if t.is_file():
                transcripts.append(t)
    return sorted(transcripts, key=lambda p: p.stat().st_mtime)


def default_brain_dir() -> Path:
    return Path.home() / ".gemini" / "antigravity" / "brain"


def aggregate_telemetry(sessions: List[SessionTelemetry], rate_card: Optional[RateCard] = None) -> Dict[str, Any]:
    """Aggregate a collection of session telemetry objects."""
    total_sessions = len(sessions)
    active_sessions = [s for s in sessions if s.measured_steps > 0]
    ignored_sessions = total_sessions - len(active_sessions)

    total_events = sum(s.total_events for s in sessions)
    total_measured_steps = sum(s.measured_steps for s in sessions)
    total_unmetered_steps = sum(s.unmetered_steps for s in sessions)
    total_corrupt_lines = sum(s.corrupt_lines for s in sessions)
    total_invalid_events = sum(s.invalid_field_events for s in sessions)

    total_uncached = sum(s.total_uncached_input_tokens for s in sessions)
    total_cached = sum(s.total_cached_input_tokens for s in sessions)
    total_output = sum(s.total_output_tokens for s in sessions)
    total_context = sum(s.total_context_tokens for s in sessions)

    mean_uncached = round(total_uncached / total_measured_steps, 2) if total_measured_steps > 0 else 0.0
    tot_in = total_uncached + total_cached
    cache_rate = round(total_cached / tot_in, 4) if tot_in > 0 else 0.0

    total_estimated_cost = None
    if rate_card is not None and total_measured_steps > 0:
        c_u = (total_uncached / 1_000_000.0) * rate_card.uncached_input_per_m
        c_c = (total_cached / 1_000_000.0) * rate_card.cached_input_per_m
        c_o = (total_output / 1_000_000.0) * rate_card.output_per_m
        total_estimated_cost = round(c_u + c_c + c_o, 4)

    return {
        "total_sessions_found": total_sessions,
        "measured_sessions": len(active_sessions),
        "ignored_sessions": ignored_sessions,
        "total_events": total_events,
        "total_measured_steps": total_measured_steps,
        "total_unmetered_steps": total_unmetered_steps,
        "total_corrupt_lines": total_corrupt_lines,
        "total_invalid_events": total_invalid_events,
        "total_uncached_input_tokens": total_uncached,
        "total_cached_input_tokens": total_cached,
        "total_output_tokens": total_output,
        "total_context_tokens": total_context,
        "mean_uncached_input_tokens_per_measured_step": mean_uncached,
        "cache_hit_rate": cache_rate,
        "estimated_total_cost_usd": total_estimated_cost,
        "rate_card_applied": rate_card.name if rate_card else None,
        "cost_disclaimer": "Approximation based on user-supplied rate card; not a verified billed invoice." if rate_card else None
    }


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Antigravity Usage and Token Telemetry Report")
    parser.add_argument("--brain-dir", type=Path, default=None, help="Path to Antigravity brain directory (defaults to ~/.gemini/antigravity/brain)")
    parser.add_argument("--file", type=Path, default=None, help="Path to a single transcript.jsonl file to analyze")
    parser.add_argument("--rate-card", type=str, default=None, help="Optional user estimation rate card: 'uncached,cached,output' USD per million tokens (e.g. '0.15,0.0375,0.60')")
    parser.add_argument("--json", action="store_true", help="Output report as structured JSON")
    args = parser.parse_args(argv)

    try:
        rc = parse_rate_card(args.rate_card)
    except ValueError as err:
        sys.stderr.write(f"Error: {err}\n")
        return 1

    transcripts: List[Path] = []
    if args.file:
        if not args.file.is_file():
            sys.stderr.write(f"Error: Specified file does not exist: {args.file}\n")
            return 1
        transcripts.append(args.file)
    else:
        bdir = args.brain_dir or default_brain_dir()
        transcripts = find_antigravity_transcripts(bdir)

    session_reports = [parse_transcript_file(t, rc) for t in transcripts]
    summary = aggregate_telemetry(session_reports, rc)

    if args.json:
        payload = {
            "summary": summary,
            "sessions": [s.to_dict() for s in session_reports]
        }
        print(json.dumps(payload, indent=2))
        return 0

    # Prose output
    print("=" * 80)
    print(" ANTIGRAVITY USAGE & TOKEN TELEMETRY REPORT")
    print("=" * 80)
    print(f"Total sessions found:    {summary['total_sessions_found']}")
    print(f"Measured active sessions:{summary['measured_sessions']}")
    print(f"Ignored sessions:        {summary['ignored_sessions']} (no measured steps)")
    print(f"Total events processed:  {summary['total_events']}")
    print(f"  - Measured steps:      {summary['total_measured_steps']}")
    print(f"  - Unmetered events:    {summary['total_unmetered_steps']}")
    print(f"  - Corrupt lines:       {summary['total_corrupt_lines']}")
    print(f"  - Invalid field events:{summary['total_invalid_events']}")
    print("-" * 80)
    print("TOKEN ACCUMULATION:")
    print(f"  Uncached input tokens: {summary['total_uncached_input_tokens']:,}")
    print(f"  Cached input tokens:   {summary['total_cached_input_tokens']:,}")
    print(f"  Output tokens:         {summary['total_output_tokens']:,}")
    print(f"  Total context tokens:  {summary['total_context_tokens']:,}")
    print("-" * 80)
    print("EFFICIENCY METRICS:")
    print(f"  Mean uncached input tokens per measured step: {summary['mean_uncached_input_tokens_per_measured_step']:,.2f}")
    print(f"  Cache hit rate:                               {summary['cache_hit_rate'] * 100:.2f}%")
    if rc:
        print("-" * 80)
        print(f"FINANCIAL ESTIMATION ({rc.name}):")
        print(f"  Estimated cost (USD):  ${summary['estimated_total_cost_usd']:,.4f}")
        print(f"  Notice: {summary['cost_disclaimer']}")
    print("=" * 80)

    return 0


if __name__ == "__main__":
    sys.exit(main())
