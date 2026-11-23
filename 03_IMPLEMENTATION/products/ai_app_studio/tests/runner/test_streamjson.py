"""stream-json allow-list (DESIGN 5.2, P6e): per-event-type field allow-list, thinking dropped, tool results reduced."""
from __future__ import annotations

import asyncio
import hashlib
import json
import os
import sys
from pathlib import Path

import pytest
from aas_runner import claude as rc
from aas_runner import streamjson as sj
from mcp.client.session import ClientSession
from mcp.client.stdio import StdioServerParameters, stdio_client

SECRET = "sk-ant-api03-" + "A1b2C3d4E5f6G7h8I9j0" * 2


def feed(m: sj.StreamMapper, o: object) -> list[tuple[str, dict]]:
    return m.feed(o if isinstance(o, str) else json.dumps(o))


def test_thinking_is_dropped_and_counted() -> None:
    m = sj.StreamMapper()
    out = feed(m, {"type": "assistant", "message": {"content": [
        {"type": "thinking", "thinking": "private reasoning " + SECRET, "signature": "zzz"},
        {"type": "redacted_thinking", "data": "abc"},
        {"type": "text", "text": "hello"}]}})
    assert [k for k, _ in out] == ["agent.text"]
    assert "private" not in json.dumps(out) and SECRET not in json.dumps(out)
    assert m.thinking_blocks == 2


def test_tool_use_maps_to_step_and_call_with_callid_and_target() -> None:
    m = sj.StreamMapper()
    out = feed(m, {"type": "assistant", "message": {"content": [
        {"type": "tool_use", "id": "toolu_1", "name": "mcp__ai-app-studio__code_write_file",
         "input": {"projectId": "prj_0123456789abcdef", "path": "src/App.tsx", "content": "x", "baseRevision": 1}}]}})
    assert [k for k, _ in out] == ["step.started", "tool.call"]
    step, call = out[0][1], out[1][1]
    assert "code_write_file" in step["title"] and "src/App.tsx" in step["title"]
    assert call["callId"] == "toolu_1" and call["target"] == "src/App.tsx" and call["tool"] == "code_write_file"


def test_tool_result_is_size_sha256_and_capped_excerpt_not_body() -> None:
    m = sj.StreamMapper()
    body = "line\n" * 5000 + SECRET
    out = feed(m, {"type": "user", "message": {"content": [
        {"type": "tool_result", "tool_use_id": "toolu_1", "is_error": True, "content": body}]}})
    ((kind, p),) = out
    assert kind == "tool.result"
    assert p["callId"] == "toolu_1" and p["isError"] is True and p["size"] == len(body.encode())
    assert p["sha256"] == hashlib.sha256(body.encode()).hexdigest()
    assert len(p["excerpt"]) <= 300
    assert set(p) <= {"callId", "tool", "isError", "size", "sha256", "excerpt"}


def test_tool_result_list_content_redacts_secret_in_excerpt() -> None:
    m = sj.StreamMapper()
    out = feed(m, {"type": "user", "message": {"content": [
        {"type": "tool_result", "tool_use_id": "t2", "content": [{"type": "text", "text": "key " + SECRET}]}]}})
    assert SECRET not in json.dumps(out)


def test_result_maps_to_cost_update_and_final_fields() -> None:
    m = sj.StreamMapper()
    out = feed(m, {"type": "result", "subtype": "success", "num_turns": 3, "total_cost_usd": 0.0123,
                   "duration_ms": 4500, "result": "full secret text " + SECRET,
                   "usage": {"input_tokens": 10, "output_tokens": 20, "cache_read_input_tokens": 99}})
    assert [k for k, _ in out] == ["cost.update"]
    assert out[0][1] == {"inputTokens": 10, "outputTokens": 20, "costUsd": 0.0123}
    assert m.final == {"status": "success", "turns": 3, "costUsd": 0.0123, "durationMs": 4500}
    assert SECRET not in json.dumps(out) and "full secret" not in json.dumps(out)


def test_unknown_types_and_fields_dropped_and_counted_garbage_does_not_raise() -> None:
    m = sj.StreamMapper()
    assert feed(m, {"type": "weird", "x": 1}) == []
    assert feed(m, "not json at all") == []
    assert feed(m, "[1,2,3]") == []
    assert feed(m, "") == []
    assert m.dropped == 3
    out = feed(m, {"type": "assistant", "message": {"content": [{"type": "image", "source": "x"},
                                                               {"type": "text", "text": "ok"}]}})
    assert [k for k, _ in out] == ["agent.text"] and m.dropped == 4


def _delta(t: str) -> dict:
    return {"type": "stream_event", "event": {"type": "content_block_delta", "index": 0,
                                              "delta": {"type": "text_delta", "text": t}}}


def test_stream_event_text_delta_is_line_buffered_and_redacted() -> None:
    m = sj.StreamMapper()
    assert feed(m, _delta("par")) == []
    assert feed(m, _delta("tial ")) == []
    out = feed(m, _delta("line " + SECRET + "\nnext"))
    assert [k for k, _ in out] == ["agent.typing"]
    assert SECRET not in json.dumps(out)
    assert out[0][1]["text"].startswith("partial line ")
    assert [k for k, _ in m.flush()] == ["agent.typing"]  # the unterminated rest is flushed (redacted)


def test_init_check_accepts_only_the_studio_server_and_catalogue_tools() -> None:
    good = {"type": "system", "subtype": "init", "mcp_servers": [{"name": "ai-app-studio", "status": "connected"}],
            "tools": ["mcp__ai-app-studio__code_write_file"]}
    sj.check_init(good)
    for bad in (
        {**good, "mcp_servers": [*good["mcp_servers"], {"name": "canary", "status": "connected"}]},
        {**good, "mcp_servers": []},
        {**good, "tools": ["mcp__ai-app-studio__code_write_file", "Bash"]},
        {**good, "tools": ["mcp__ai-app-studio__not_in_catalogue"]},
        {**good, "tools": ["mcp__canary__x"]},
        {k: v for k, v in good.items() if k != "mcp_servers"},
    ):
        with pytest.raises(sj.InitViolation):
            sj.check_init(bad)


def test_catalogue_is_a_subset_of_the_mcp_server_tools(tmp_path: Path) -> None:
    root = Path(__file__).resolve().parents[2]

    async def names() -> set[str]:
        env = {**os.environ, "AAS_DATA_DIR": str(tmp_path / "d")}
        p = StdioServerParameters(command=sys.executable, args=[str(root / "apps" / "mcp-app" / "server.py")], env=env)
        async with stdio_client(p) as (r, w), ClientSession(r, w) as s:
            await s.initialize()
            return {t.name for t in (await s.list_tools()).tools}

    assert set(rc.CATALOGUE_TOOLS) <= asyncio.run(names())
