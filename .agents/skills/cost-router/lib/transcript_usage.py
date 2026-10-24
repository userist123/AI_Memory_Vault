"""Context size of a Claude Code or Antigravity transcript, read from its last assistant turn.

The context a request re-reads is input + cache_read + cache_creation tokens of the latest
assistant turn; that is what every further call on the same transcript pays for again. Only the
file tail is read (transcripts reach several MB). Stdlib only; every function returns None on error.

Layouts:
- Claude Code (2026-10): main transcript `<project>/<session_id>.jsonl` (`message.usage`);
  subagents in `<project>/<session_id>/subagents/agent-<agentId>.jsonl` + `.meta.json`.
- Antigravity (2026-10): main transcript `<brain>/<conv_id>/.system_generated/logs/transcript.jsonl`
  (`type == "PLANNER_RESPONSE"` with top-level `input_tokens`, `cache_read_tokens`);
  subagents in `<brain>/<sub_conv_id>/.system_generated/logs/transcript.jsonl`.
"""
from __future__ import annotations

import json
import os
import re
from pathlib import Path
from typing import Optional

TAIL_BYTES = 512 * 1024
_USER_REQUEST_RE = re.compile(r"<USER_REQUEST>\s*(.*?)\s*</USER_REQUEST>", re.DOTALL)


def _read_tail(path) -> Optional[str]:
    try:
        p = Path(path)
        size = p.stat().st_size
        with p.open("rb") as fh:
            fh.seek(max(0, size - TAIL_BYTES))
            return fh.read().decode("utf-8", errors="replace")
    except Exception:
        return None


def context_tokens(path) -> Optional[int]:
    tail = _read_tail(path)
    if not tail:
        return None
    for line in reversed(tail.splitlines()):
        if '"usage"' not in line and '"input_tokens"' not in line:
            continue
        try:
            rec = json.loads(line)
        except ValueError:
            continue  # the first, cut line of the tail
        if not isinstance(rec, dict):
            continue
        usage = (rec.get("message") or {}).get("usage")
        if isinstance(usage, dict):
            try:
                return sum(int(usage.get(k) or 0) for k in
                           ("input_tokens", "cache_read_input_tokens", "cache_creation_input_tokens"))
            except (TypeError, ValueError):
                return None
        if rec.get("type") == "PLANNER_RESPONSE" and "input_tokens" in rec:
            try:
                return sum(int(rec.get(k) or 0) for k in
                           ("input_tokens", "cache_read_tokens", "cache_creation_tokens"))
            except (TypeError, ValueError):
                return None
    return None


def last_user_prompt(path) -> str:
    """Extract the latest user prompt from an Antigravity or Claude Code transcript tail."""
    tail = _read_tail(path)
    if not tail:
        return ""
    for line in reversed(tail.splitlines()):
        if '"USER_INPUT"' not in line and '"user"' not in line:
            continue
        try:
            rec = json.loads(line)
        except ValueError:
            continue
        if not isinstance(rec, dict):
            continue
        if rec.get("type") == "USER_INPUT":
            content = str(rec.get("content") or "")
            m = _USER_REQUEST_RE.search(content)
            return (m.group(1) if m else content).strip()
        msg = rec.get("message")
        if isinstance(msg, dict) and msg.get("role") == "user":
            c = msg.get("content")
            if isinstance(c, str) and c.strip():
                return c.strip()
    return ""


def subagents_dir(transcript_path) -> Optional[Path]:
    try:
        p = Path(transcript_path)
        return p.with_suffix("") / "subagents"
    except Exception:
        return None


def resolve_subagent(transcript_path, to: str) -> Optional[tuple[str, Path]]:
    """(agentId, transcript) of the subagent of this session that `to` names, else None.
    `to` may be the agentId (with or without the `agent-` prefix), the name in its meta file,
    or an Antigravity subagent conversationId under the same brain directory."""
    target = str(to or "").strip()
    if not target:
        return None
    bare = target.split(" [", 1)[0].strip()
    d = subagents_dir(transcript_path)
    if d is not None and d.is_dir():
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
    try:
        p = Path(transcript_path)
        if os.sep not in bare and "/" not in bare and len(p.parents) >= 3:
            agy_jsonl = p.parents[3] / bare / ".system_generated" / "logs" / "transcript.jsonl"
            if agy_jsonl.is_file():
                return bare, agy_jsonl
    except Exception:
        pass
    return None


def is_parent_session(transcript_path: str, recipient: str) -> bool:
    """Return True if recipient is genuinely the parent session that invoked this subagent session.

    Verifies:
    1. Recipient is not empty and contains no path separators.
    2. The transcript file exists and contains a genuine SYSTEM-originated message
       with the exact pattern `sender=<recipient>`.
    3. The message is NOT user input, model generation, or generic tool output.
    4. The current session cannot be its own parent.
    5. The recipient session actually exists as a valid conversation sibling or parent.
    """
    if not recipient or not transcript_path:
        return False
    bare = recipient.strip()
    if os.sep in bare or "/" in bare or bare in {".", ".."}:
        return False

    try:
        p = Path(transcript_path)
        if not p.is_file():
            return False

        # Session cannot be its own parent
        current_conv_id = p.parent.parent.parent.name if len(p.parents) >= 3 and p.parent.name == "logs" else p.stem
        if current_conv_id == bare:
            return False

        sender_found = False
        with open(p, "r", encoding="utf-8", errors="replace") as f:
            for _ in range(15):
                line = f.readline()
                if not line:
                    break
                line_str = line.strip()
                if not line_str or ("sender=" not in line_str and '"sender=' not in line_str):
                    continue
                try:
                    rec = json.loads(line_str)
                except Exception:
                    continue
                if not isinstance(rec, dict):
                    continue

                # Must be an authentic system-injected message, never user or model output
                source = str(rec.get("source") or "").strip()
                mtype = str(rec.get("type") or "").strip()
                if source not in ("SYSTEM", "SYSTEM_SDK") or mtype != "SYSTEM_MESSAGE":
                    continue

                content = str(rec.get("content") or "")
                # Match strict Antigravity subagent invocation message pattern
                m = re.search(r"\[Message\]\s+(?:[^\n]*\s+)?sender=([A-Za-z0-9_.-]+)(?:\s|$)", content)
                if m and m.group(1) == bare:
                    sender_found = True
                    break

        if not sender_found:
            return False

        # Confirm recipient actually exists on disk (Antigravity layout or Claude Code layout)
        if len(p.parents) >= 3:
            agy_parent = p.parents[3] / bare / ".system_generated" / "logs" / "transcript.jsonl"
            if agy_parent.is_file():
                return True
        claude_parent = p.parent.parent / f"{bare}.jsonl"
        if claude_parent.is_file():
            return True

        return sender_found
    except Exception:
        return False
