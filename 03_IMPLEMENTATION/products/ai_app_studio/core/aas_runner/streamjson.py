"""stream-json -> activity payloads with a per-event-type field allow-list (DESIGN 5.2; P6e, P5e/N3).

Nothing here touches the database: ``StreamMapper.feed(line)`` returns ``[(kind, payload)]`` that the runner records
through ``studio_core.activity.record`` (which sanitises and caps once more). Text is sanitised and value-redacted
here too, so a mapped payload never holds a secret even before it is recorded. Thinking blocks are dropped and only
counted; tool results are reduced to size + sha256 + a capped excerpt; unknown types/fields are dropped and counted.
"""
from __future__ import annotations

import hashlib
import json
import time
from typing import Any

from studio_core.activity import clean_text

SERVER = "ai-app-studio"
TOOL_PREFIX = f"mcp__{SERVER}__"
# every MCP tool the built-in runner may call; a subset of the tools published by apps/mcp-app/server.py (tested)
CATALOGUE_TOOLS: tuple[str, ...] = (
    "project_list", "project_inspect", "code_list_files", "code_read_file", "code_write_file", "code_delete_file",
    "activity_list",
    "plan_submit", "app_build", "preview_capture", "device_start", "device_install", "device_tap",
    "device_screenshot", "app_export",
)
EXCERPT_CAP = 300
TEXT_CAP = 4000
TYPING_STALE_SECONDS = 2.0
TYPING_TOKEN_CAP = 512  # an unfinished token stays buffered across a stale flush up to this many characters
_TARGET_KEYS = ("path", "file", "file_path", "target")


class InitViolation(Exception):
    """The CLI's raw system/init does not match what the studio configured (P5e/N3): the run must be aborted."""


def _server_name(s: Any) -> str | None:
    if isinstance(s, str):
        return s
    if isinstance(s, dict) and isinstance(s.get("name"), str):
        return s["name"]
    return None


def check_init(obj: dict[str, Any]) -> None:
    servers = obj.get("mcp_servers")
    if not isinstance(servers, list):
        raise InitViolation("init has no mcp_servers list")
    names = [_server_name(s) for s in servers]
    if names != [SERVER]:
        raise InitViolation(f"mcp_servers must be exactly [{SERVER!r}], got {names!r}")
    tools = obj.get("tools")
    if not isinstance(tools, list):
        raise InitViolation("init has no tools list")
    allowed = {TOOL_PREFIX + t for t in CATALOGUE_TOOLS}
    bad = [t for t in tools if t not in allowed]
    if bad:
        raise InitViolation(f"tools outside the catalogue: {[str(b)[:60] for b in bad][:5]}")


def _short(tool: Any) -> str:
    t = str(tool)
    return t[len(TOOL_PREFIX):] if t.startswith(TOOL_PREFIX) else t


def _text(value: Any, cap: int) -> str:
    return clean_text(value, cap)[0]


class StreamMapper:
    def __init__(self) -> None:
        self.dropped = 0
        self.thinking_blocks = 0
        self.final: dict[str, Any] | None = None
        self.model = ""
        self.init: dict[str, Any] | None = None
        self._tools: dict[str, str] = {}
        self._typing = ""
        self._typing_at = 0.0

    # ------------------------------------------------------------------ entry
    def feed(self, line: str | bytes) -> list[tuple[str, dict[str, Any]]]:
        raw = line.decode("utf-8", "replace") if isinstance(line, bytes) else line
        if not raw.strip():
            return []
        try:
            obj = json.loads(raw)
        except ValueError:
            self.dropped += 1
            return []
        if not isinstance(obj, dict):
            self.dropped += 1
            return []
        t = obj.get("type")
        if t == "system":
            return self._system(obj)
        if t == "assistant":
            return self._assistant(obj)
        if t == "user":
            return self._user(obj)
        if t == "result":
            return self._result(obj)
        if t == "stream_event":
            return self._stream_event(obj)
        self.dropped += 1
        return []

    # ------------------------------------------------------------------ per type
    def _system(self, obj: dict[str, Any]) -> list[tuple[str, dict[str, Any]]]:
        if obj.get("subtype") == "init":
            self.init = obj
            self.model = _text(obj.get("model", ""), 80)
        elif obj.get("subtype") in ("hook_started", "hook_progress", "hook_response"):
            pass  # the owner's own hooks (profile full): expected, never mapped or recorded, not a "dropped" warning
        else:
            self.dropped += 1
        return []

    @staticmethod
    def _blocks(obj: dict[str, Any]) -> list[Any]:
        msg = obj.get("message")
        content = msg.get("content") if isinstance(msg, dict) else None
        if isinstance(content, str):
            return [{"type": "text", "text": content}]
        return content if isinstance(content, list) else []

    def _assistant(self, obj: dict[str, Any]) -> list[tuple[str, dict[str, Any]]]:
        out: list[tuple[str, dict[str, Any]]] = []
        step_done = False
        for b in self._blocks(obj):
            bt = b.get("type") if isinstance(b, dict) else None
            if bt == "text":
                out.append(("agent.text", {"text": _text(b.get("text", ""), TEXT_CAP)}))
            elif bt in ("thinking", "redacted_thinking"):
                self.thinking_blocks += 1  # count only: the content is never read
            elif bt == "tool_use":
                name = _short(b.get("name", ""))
                inp = b.get("input") if isinstance(b.get("input"), dict) else {}
                target = ""
                for k in _TARGET_KEYS:
                    if isinstance(inp.get(k), str):
                        target = _text(inp[k], 500)
                        break
                cid = _text(b.get("id", ""), 80)
                if cid:
                    self._tools[cid] = name
                if not step_done:
                    out.append(("step.started", {"stepId": cid[:40], "title": _text(f"{name} {target}".strip(), 200)}))
                    step_done = True
                shown = {k: v for k, v in inp.items() if k != "content"}  # file bodies go through file.diff instead
                if isinstance(inp.get("content"), str):
                    shown["contentBytes"] = len(inp["content"].encode("utf-8"))
                out.append(("tool.call", {"tool": name, "callId": cid, "target": target,
                                           "input": _text(shown, 2000)}))
            else:
                self.dropped += 1
        return out

    def _user(self, obj: dict[str, Any]) -> list[tuple[str, dict[str, Any]]]:
        out: list[tuple[str, dict[str, Any]]] = []
        for b in self._blocks(obj):
            if not isinstance(b, dict) or b.get("type") != "tool_result":
                self.dropped += 1
                continue
            c = b.get("content", "")
            if isinstance(c, list):
                body = "\n".join(str(x.get("text", "")) for x in c if isinstance(x, dict) and x.get("type") == "text")
            else:
                body = c if isinstance(c, str) else json.dumps(c, default=str)
            raw = body.encode("utf-8", "replace")
            cid = _text(b.get("tool_use_id", ""), 80)
            p: dict[str, Any] = {"callId": cid, "isError": bool(b.get("is_error")), "size": len(raw),
                                 "sha256": hashlib.sha256(raw).hexdigest(), "excerpt": _text(body, EXCERPT_CAP)}
            if cid in self._tools:
                p["tool"] = self._tools[cid]
            out.append(("tool.result", p))
        return out

    def _result(self, obj: dict[str, Any]) -> list[tuple[str, dict[str, Any]]]:
        usage = obj.get("usage") if isinstance(obj.get("usage"), dict) else {}

        def num(v: Any, typ: type) -> Any:
            return typ(v) if isinstance(v, (int, float)) and not isinstance(v, bool) else typ(0)

        status = "error" if obj.get("is_error") is True and obj.get("subtype") in (None, "success") \
            else _text(obj.get("subtype", "unknown"), 40)
        self.final = {"status": status, "turns": num(obj.get("num_turns"), int),
                      "costUsd": num(obj.get("total_cost_usd"), float), "durationMs": num(obj.get("duration_ms"), int)}
        return [("cost.update", {"inputTokens": num(usage.get("input_tokens"), int),
                                 "outputTokens": num(usage.get("output_tokens"), int),
                                 "costUsd": self.final["costUsd"]})]

    def _stream_event(self, obj: dict[str, Any]) -> list[tuple[str, dict[str, Any]]]:
        ev = obj.get("event")
        if not isinstance(ev, dict) or ev.get("type") != "content_block_delta":
            return []  # message_start / block start / stop / message_delta: expected noise, not counted
        d = ev.get("delta")
        if not isinstance(d, dict) or d.get("type") != "text_delta" or not isinstance(d.get("text"), str):
            return []  # input_json_delta etc.: file bodies are shown via file.diff, never streamed raw
        self._typing += d["text"]
        self._typing_at = self._typing_at or time.monotonic()
        if "\n" not in self._typing:
            return []
        head, _, self._typing = self._typing.rpartition("\n")
        self._typing_at = time.monotonic() if self._typing else 0.0
        return [("agent.typing", {"text": _text(head, 1000)})]

    # ------------------------------------------------------------------ typing buffer
    def flush(self) -> list[tuple[str, dict[str, Any]]]:
        rest, self._typing, self._typing_at = self._typing, "", 0.0
        return [("agent.typing", {"text": _text(rest, 1000)})] if rest.strip() else []

    def flush_stale(self, max_age: float = TYPING_STALE_SECONDS) -> list[tuple[str, dict[str, Any]]]:
        """A long pause in the middle of a line: show what is complete, but NEVER split a token. Only the text up to
        the last whitespace is flushed (redacted as one piece); the trailing token stays buffered so a secret that
        arrives in several deltas is redacted whole later. A token longer than TYPING_TOKEN_CAP is flushed (redacted)
        instead of growing the buffer without bound."""
        if not (self._typing and self._typing_at and time.monotonic() - self._typing_at >= max_age):
            return []
        cut = max(self._typing.rfind(c) for c in (" ", "\t", "\r", "\n"))
        if cut < 0:
            return self.flush() if len(self._typing) > TYPING_TOKEN_CAP else []
        head, rest = self._typing[:cut + 1], self._typing[cut + 1:]
        if len(rest) > TYPING_TOKEN_CAP:
            return self.flush()
        self._typing = rest
        self._typing_at = time.monotonic() if rest else 0.0
        return [("agent.typing", {"text": _text(head, 1000)})] if head.strip() else []
