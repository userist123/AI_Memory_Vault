"""Context size of a Claude Code transcript, read from its last assistant `usage` record.

The context a request re-reads is input + cache_read + cache_creation tokens of the latest
assistant turn; that is what every further call on the same transcript pays for again. Only the
file tail is read (transcripts reach several MB). Stdlib only; every function returns None on error.

Layout (Claude Code, 2026-10): the main transcript is `<project>/<session_id>.jsonl`; its
subagents live in `<project>/<session_id>/subagents/agent-<agentId>.jsonl` with a
`agent-<agentId>.meta.json` next to it (agentType, description, optional name).
"""
from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Optional

TAIL_BYTES = 512 * 1024


USAGE_KEYS = ("input_tokens", "cache_read_input_tokens", "cache_creation_input_tokens", "output_tokens")


def context_tokens(path) -> Optional[int]:
    split = usage_split(path)
    return None if split is None else split["context_tokens"]


def estimate_cost_usd(model_id, usage: dict, policy: dict) -> Optional[float]:
    """USD cost of one usage dict at the policy rate card (policy["models"][alias]["usd_per_mtok"]),
    matched by alias, exact model_id or the longest model_id prefix (end, "-" or "[" must follow). input at the input price, output at the output
    price; cache_read and cache_creation use the card's own `cache_read` / `cache_write_5m` /
    `cache_write_1h` prices (1h writes: `cache_creation_1h_input_tokens`, else counted as 5m) when present, else the standard prompt-caching multipliers: 10% and 125% of the input price.
    Returns None for an unknown model or an unusable card: prices are never invented."""
    try:
        want = str(model_id or "").strip().lower()
        if not want or not isinstance(usage, dict):
            return None
        models = policy.get("models") or {}
        spec = None
        for alias, cand in models.items():  # alias or exact model_id first
            if want == alias.lower() or want == str(cand.get("model_id") or "").lower():
                spec = cand
                break
        if spec is None:  # else the LONGEST model_id prefix followed by end, "-" or "["
            best = 0
            for cand in models.values():
                mid = str(cand.get("model_id") or "").lower()
                if (len(mid) > best and want.startswith(mid)
                        and (len(want) == len(mid) or want[len(mid)] in "-[")):
                    spec, best = cand, len(mid)
        if spec is None:
            return None
        price = spec["usd_per_mtok"]
        p_in, p_out = float(price["input"]), float(price["output"])
        p_cr = float(price.get("cache_read", p_in * 0.10))
        p_cw = float(price.get("cache_write_5m", p_in * 1.25))
        p_cw1 = float(price.get("cache_write_1h", p_cw))
        n = lambda k: int(usage.get(k) or 0)  # noqa: E731
        c1h, c5m = n("cache_creation_1h_input_tokens"), n("cache_creation_5m_input_tokens")
        if not (c1h or c5m):  # only the flat total: all of it is 5m
            c5m = n("cache_creation_input_tokens")
        return (n("input_tokens") * p_in + n("cache_read_input_tokens") * p_cr
                + c5m * p_cw + c1h * p_cw1 + n("output_tokens") * p_out) / 1_000_000
    except Exception:
        return None
    return None


def last_assistant_model(path) -> Optional[str]:
    """`message.model` of the last assistant record in the transcript tail; None when unreadable/absent/synthetic."""
    try:
        p = Path(path)
        size = p.stat().st_size
        with p.open("rb") as fh:
            fh.seek(max(0, size - TAIL_BYTES))
            tail = fh.read().decode("utf-8", errors="replace")
    except Exception:
        return None
    for line in reversed(tail.splitlines()):
        if '"model"' not in line:
            continue
        try:
            rec = json.loads(line)
        except ValueError:
            continue
        msg = rec.get("message") if isinstance(rec, dict) else None
        model = msg.get("model") if isinstance(msg, dict) else None
        if isinstance(model, str) and model and not model.startswith("<") and (rec.get("type") in (None, "assistant")):
            return model[:80]
    return None


def usage_split(path) -> Optional[dict]:
    """Token split of the last assistant `usage` record: input_tokens, cache_read_input_tokens,
    cache_creation_input_tokens, output_tokens and context_tokens, where
    context_tokens = input_tokens + cache_read_input_tokens + cache_creation_input_tokens
    (output excluded); plus cache_creation_1h_input_tokens / cache_creation_5m_input_tokens from
    usage.cache_creation (a flat-only record counts as all 5m). None when unreadable or no usage record exists."""
    try:
        p = Path(path)
        size = p.stat().st_size
        with p.open("rb") as fh:
            fh.seek(max(0, size - TAIL_BYTES))
            tail = fh.read().decode("utf-8", errors="replace")
    except Exception:
        return None
    for line in reversed(tail.splitlines()):
        if '"usage"' not in line:
            continue
        try:
            rec = json.loads(line)
        except ValueError:
            continue  # the first, cut line of the tail
        usage = (rec.get("message") or {}).get("usage") if isinstance(rec, dict) else None
        if isinstance(usage, dict):
            try:
                out = {k: int(usage.get(k) or 0) for k in USAGE_KEYS}
            except (TypeError, ValueError):
                return None
            cc = usage.get("cache_creation") if isinstance(usage.get("cache_creation"), dict) else {}
            try:
                out["cache_creation_1h_input_tokens"] = int(cc.get("ephemeral_1h_input_tokens") or 0)
                out["cache_creation_5m_input_tokens"] = int(cc.get("ephemeral_5m_input_tokens") or 0)
            except (TypeError, ValueError):
                return None
            if not (out["cache_creation_1h_input_tokens"] or out["cache_creation_5m_input_tokens"]):
                out["cache_creation_5m_input_tokens"] = out["cache_creation_input_tokens"]
            out["context_tokens"] = (out["input_tokens"] + out["cache_read_input_tokens"]
                                     + out["cache_creation_input_tokens"])
            return out
    return None


def subagents_dir(transcript_path) -> Optional[Path]:
    try:
        p = Path(transcript_path)
        return p.with_suffix("") / "subagents"
    except Exception:
        return None


def resolve_subagent(transcript_path, to: str) -> Optional[tuple[str, Path]]:
    """(agentId, transcript) of the subagent of this session that `to` names, else None.
    `to` may be the agentId (with or without the `agent-` prefix) or the name in its meta file."""
    d = subagents_dir(transcript_path)
    target = str(to or "").strip()
    if d is None or not target or not d.is_dir():
        return None
    bare = target.split(" [", 1)[0].strip()
    aid = bare[len("agent-"):] if bare.startswith("agent-") else bare
    direct = d / f"agent-{aid}.jsonl"
    if os.sep not in aid and "/" not in aid and direct.is_file():
        return aid, direct
    for meta in d.glob("agent-*.meta.json"):
        try:
            m = json.loads(meta.read_text(encoding="utf-8"))
        except Exception:
            continue
        if isinstance(m, dict) and bare and bare == str(m.get("name") or ""):
            agent = meta.name[len("agent-"):-len(".meta.json")]
            jsonl = d / f"agent-{agent}.jsonl"
            if jsonl.is_file():
                return agent, jsonl
    return None
