#!/usr/bin/env python3
"""PostToolUse hook (matcher "Agent|Task") for the cost-router skill: subagent usage telemetry.

Records, per finished subagent, the resolved model, the 4-way token split, duration and an estimated
cost (lib/transcript_usage.estimate_cost_usd) in the session state (lib/route_state.record_agent_usage).
Idempotent on tool_use_id. It never stores the prompt or the subagent's content and prints nothing:
efficiency telemetry, not authorization, so every error exits 0 silently.

`--report [session_id]` prints a compact table of the recorded usage (read-only; default: the newest
session in the state directory).
"""
from __future__ import annotations

import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

SPAWN_TOOLS = ("Agent", "Task")
SPLIT_KEYS = ("input_tokens", "cache_read_input_tokens", "cache_creation_input_tokens", "output_tokens")


def _int(value) -> int:
    try:
        return int(value or 0)
    except (TypeError, ValueError):
        return 0


def _opt(value):
    """int, or None when the field is missing / not a number (unknown is never stored as 0)."""
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    return int(value)


def _cap(value):
    return None if value is None else str(value)[:64]


def build_entry(data: dict, policy: dict) -> dict:
    from transcript_usage import estimate_cost_usd  # noqa: E402
    resp = data["tool_response"]
    tool_input = data.get("tool_input") if isinstance(data.get("tool_input"), dict) else {}
    usage = resp.get("usage") if isinstance(resp.get("usage"), dict) else {}
    split = {k: _opt(usage.get(k)) for k in SPLIT_KEYS}
    missing = [k for k in SPLIT_KEYS if split[k] is None]
    cc = usage.get("cache_creation") if isinstance(usage.get("cache_creation"), dict) else {}
    split["cache_creation_1h_input_tokens"] = _int(cc.get("ephemeral_1h_input_tokens"))
    split["cache_creation_5m_input_tokens"] = _int(cc.get("ephemeral_5m_input_tokens"))
    model = resp.get("resolvedModel")
    return {"ts": int(time.time()), "tool_use_id": _cap(data.get("tool_use_id")) or "",
            "agent_id": _cap(resp.get("agentId")), "agent_type": _cap(resp.get("agentType")),
            "resolved_model": _cap(model), "requested_model": _cap(tool_input.get("model")),
            **split, "usage_complete": not missing, "missing_fields": missing,
            "total_tokens": _opt(resp.get("totalTokens")),
            "duration_ms": _int(resp.get("totalDurationMs")),
            "tool_use_count": _int(resp.get("totalToolUseCount")),
            "est_cost_usd": None if missing else estimate_cost_usd(model, split, policy)}


def load_policy() -> dict:
    from route import policy_path  # noqa: E402
    return json.loads(Path(policy_path()).read_text(encoding="utf-8"))


def report(session_id=None) -> int:
    import route_state  # noqa: E402
    sid = session_id
    if not sid:
        d = route_state.state_dir()
        files = sorted(d.glob("route-*.agent_usage.jsonl"), key=lambda f: f.stat().st_mtime) if d.is_dir() else []
        sid = files[-1].name[len("route-"):-len(".agent_usage.jsonl")] if files else None
    entries = route_state.agent_usage(sid) if sid else []
    if not entries:
        print("cost-router: no recorded subagent usage")
        return 0
    print(f"session {sid}: {len(entries)} subagent(s)")
    print(f"{'model':<20}{'type':<18}{'in':>9}{'cache_r':>10}{'cache_w':>10}{'out':>8}{'usd':>10}")
    totals: dict = {}
    unknown_n = 0
    for e in entries:
        m = str(e.get("resolved_model") or "?")
        c = e.get("est_cost_usd")
        ok = isinstance(c, (int, float))
        t = totals.setdefault(m, [0, 0.0, False])
        t[0] += 1
        if ok:
            t[1] += c
            t[2] = True
        unknown_n += e.get("usage_complete") is False
        cell = lambda k: "unknown" if _opt(e.get(k)) is None else str(_opt(e.get(k)))  # noqa: E731
        print(f"{m:<20}{str(e.get('agent_type') or '?')[:17]:<18}{cell('input_tokens'):>9}"
              f"{cell('cache_read_input_tokens'):>10}{cell('cache_creation_input_tokens'):>10}"
              f"{cell('output_tokens'):>8}{(f'{c:.4f}' if ok else 'n/a'):>10}")
    for m, (n, usd, known) in totals.items():
        print(f"total {m}: {n} spawn(s), est {usd:.4f} USD" if known else f"total {m}: {n} spawn(s), est n/a")
    costs = [e["est_cost_usd"] for e in entries if isinstance(e.get("est_cost_usd"), (int, float))]
    lower = f" (lower bound: {unknown_n} spawn(s) with unknown usage not priced)" if unknown_n else ""
    print(f"sum est_cost_usd: {sum(costs):.4f}{lower}" if costs else "sum est_cost_usd: n/a")
    return 0


def main() -> int:
    try:  # COST_ROUTER_OFF=1: equal-arm benchmarks / owner kill switch; no output, no state
        import route_state as _rs  # noqa: E402
        if "--report" not in sys.argv and _rs.disabled_by_env():
            return 0
    except Exception:
        pass
    if "--report" in sys.argv:
        try:
            rest = [a for a in sys.argv[1:] if a != "--report"]
            return report(rest[0] if rest else None)
        except Exception:
            return 0
    try:
        data = json.load(sys.stdin)
        if not isinstance(data, dict) or data.get("tool_name") not in SPAWN_TOOLS:
            return 0
        if not isinstance(data.get("tool_response"), dict) or not data.get("tool_use_id"):
            return 0
        from route import is_shadowed  # noqa: E402
        if is_shadowed(Path(__file__)):
            return 0
        import route_state  # noqa: E402
        sid = data.get("session_id")
        if route_state.state_path(sid) is None:
            return 0
        if route_state.seen_event(sid, "post:" + str(data["tool_use_id"])):
            return 0
        try:
            policy = load_policy()
        except Exception:
            policy = {}
        route_state.record_agent_usage(sid, build_entry(data, policy))
    except Exception:
        return 0
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
