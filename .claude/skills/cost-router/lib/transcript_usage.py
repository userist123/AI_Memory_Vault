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


def context_tokens(path) -> Optional[int]:
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
                return sum(int(usage.get(k) or 0) for k in
                           ("input_tokens", "cache_read_input_tokens", "cache_creation_input_tokens"))
            except (TypeError, ValueError):
                return None
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
