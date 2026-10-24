#!/usr/bin/env python3
"""Offline token-accounting check for stream-json logs of the cost-router benchmark (no network, no claude run).

    python 30_SCRIPTS/evaluation/token_accounting_check.py <stream.jsonl | results_dir | dir_of_streams> [...]

For every stream it prints the per-role / per-model category table (input, cache_read, cache_creation, output, requests,
completeness), the completeness flags and the reconciliation status against modelUsage. It reuses the harness functions
(`claude_cost_router_benchmark.stream_metrics`, `attribute`, `reconciliation`); nothing is re-implemented here.

Exit codes: 0 = every stream is complete and reconciles; 1 = at least one stream is INCOMPLETE (no result event, or usage
fields missing -> totals are lower bounds) or MISMATCHED (input / cache_read / cache_creation differ from modelUsage, negative
residual, an attributed model without a modelUsage record, cost without tokens or tokens without cost); 2 = usage error.
The streaming output of a request is a lower bound by design, so an output residual alone is reported but is not a mismatch.
"""
from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

_HERE = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location("claude_cost_router_benchmark", _HERE / "claude_cost_router_benchmark.py")
bm = importlib.util.module_from_spec(_spec)
sys.modules["claude_cost_router_benchmark"] = bm
_spec.loader.exec_module(bm)

MISMATCH_FLAGS = ("usage_mismatch", "residual_negative", "attribution_mismatch", "cost_partial", "cost_without_tokens",
                  "tokens_without_cost")


def find_streams(arg: Path) -> list[Path]:
    if arg.is_file():
        return [arg]
    raw = arg / "raw" if (arg / "raw").is_dir() else arg
    return sorted(raw.glob("*.stream.jsonl")) or sorted(raw.glob("*.jsonl"))


def check_stream(path: Path) -> tuple[int, list[str]]:
    """(0|1, report lines) for one stream file."""
    m = bm.stream_metrics(path.read_text(encoding="utf-8", errors="replace"))
    lines = [f"== {path.name}"]
    rows = m.get("by_role_model") or []
    if rows:
        lines.append(f"{'role':<28}{'model':<22}{'input':>8}{'cache_read':>12}{'cache_creation':>16}{'output':>8}{'req':>5}  complete")
        for r in rows:
            lines.append(f"{r['role']:<28}{r['model']:<22}{r['input_tokens']:>8}{r['cache_read_input_tokens']:>12}"
                         f"{r['cache_creation_input_tokens']:>16}{r['output_tokens']:>8}{r['requests']:>5}  "
                         + ("yes" if r.get("usage_complete", True) else "NO missing=" + ",".join(r["missing_fields"])))
        lines.append(f"{'TOTAL':<50}{m['tok_input']:>8}{m['tok_cache_read']:>12}{m['tok_cache_creation']:>16}"
                     f"{m['tok_output']:>8}   total_tokens={m['total_tokens']}"
                     + (" (LOWER BOUND)" if m.get("total_tokens_lower_bound") else ""))
    else:
        lines.append("no per-request usage in this stream: tokens UNKNOWN (not 0)")
    flags = {k: m.get(k) for k in ("incomplete", "usage_complete", "tokens_complete", "missing_fields", *MISMATCH_FLAGS)
             if m.get(k) not in (None, False, [])}
    lines.append("flags: " + (", ".join(f"{k}={v}" for k, v in flags.items()) or "none")
                 + f" | cost_authoritative_usd={m.get('cost_authoritative_usd')} cost_policy_usd={m.get('cost_policy_usd')}")
    stem = path.name.split(".")[0]  # <task>_<condition>_<repeat> when it follows the harness naming, else the whole stem
    head, _, rep = stem.rpartition("_")
    task, _, cond = head.partition("_")
    rec = bm.reconciliation([{"task": task or stem, "condition": cond, "repeat": rep, **m}])
    lines += [ln for ln in rec.splitlines() if ln.startswith("| ") and not ln.startswith("| run") and "---" not in ln]
    problems = []
    if m.get("incomplete"):
        problems.append("INCOMPLETE (no result event)")
    if m.get("tokens_complete") is False and not m.get("incomplete"):
        problems.append("INCOMPLETE usage (missing " + ",".join(m.get("missing_fields") or ["all"]) + ")")
    problems += [f"MISMATCH {k}" for k in MISMATCH_FLAGS if m.get(k)]
    lines.append("status: " + ("; ".join(problems) if problems else "OK (reconciles with modelUsage; output residual is the expected streaming lower bound)"))
    return (1 if problems else 0), lines


def main(argv=None) -> int:
    args = [Path(a) for a in (sys.argv[1:] if argv is None else argv)]
    if not args:
        print(__doc__)
        return 2
    streams = [s for a in args for s in find_streams(a)]
    if not streams:
        print("no stream files found", file=sys.stderr)
        return 2
    bad = 0
    for s in streams:
        code, lines = check_stream(s)
        print("\n".join(lines))
        bad += code
    print(f"\nchecked {len(streams)} stream(s): {len(streams) - bad} ok, {bad} incomplete or mismatched")
    return 1 if bad else 0


if __name__ == "__main__":
    raise SystemExit(main())
