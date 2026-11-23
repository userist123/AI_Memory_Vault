"""Fake Claude Code CLI for runner tests: emits recorded stream-json lines, no network, no model.

The prompt (stdin) is a JSON scenario:
  {"record": "<path>", "init": {...overrides...}, "events": [<json objects or raw strings>], "sleep": <seconds>,
   "mcp_calls": [{"tool": "code_write_file", "args": {...}}], "project_id": "...", "exit": 0}
It records argv, cwd, the cwd listing, and the environment keys to scenario["record"] before emitting anything.
With "mcp_calls" it behaves like the real CLI: it starts the MCP server named in --mcp-config over stdio (the
env of that config entry, nothing else added), calls the tools, and emits matching tool_use / tool_result lines.
"""
from __future__ import annotations

import asyncio
import json
import os
import sys
import time


def emit(o: object) -> None:
    sys.stdout.write((o if isinstance(o, str) else json.dumps(o)) + "\n")
    sys.stdout.flush()


async def run_mcp(cfg_path: str, calls: list[dict], project_id: str) -> None:
    from mcp.client.session import ClientSession
    from mcp.client.stdio import StdioServerParameters, stdio_client
    cfg = json.load(open(cfg_path, encoding="utf-8"))["mcpServers"]["ai-app-studio"]
    params = StdioServerParameters(command=cfg["command"], args=cfg["args"], env=cfg.get("env") or {})
    rev = 0
    async with stdio_client(params) as (r, w), ClientSession(r, w) as s:
        await s.initialize()
        for i, c in enumerate(calls):
            args = {k: (project_id if v == "$project" else rev if v == "$rev" else v) for k, v in c["args"].items()}
            cid = f"toolu_{i}"
            emit({"type": "assistant", "message": {"content": [
                {"type": "tool_use", "id": cid, "name": "mcp__ai-app-studio__" + c["tool"], "input": args}]}})
            res = await s.call_tool(c["tool"], args)
            text = next((x.text for x in res.content if x.type == "text"), "")
            try:
                body = json.loads(text)
                if body.get("ok") and isinstance(body.get("result"), dict) and "revision" in body["result"]:
                    rev = body["result"]["revision"]
            except ValueError:
                pass
            emit({"type": "user", "message": {"content": [
                {"type": "tool_result", "tool_use_id": cid, "is_error": bool(res.is_error), "content": text}]}})


def main() -> int:
    scenario = json.loads(sys.stdin.read() or "{}")
    argv = sys.argv[1:]
    if scenario.get("record"):
        with open(scenario["record"], "w", encoding="utf-8") as f:
            json.dump({"argv": argv, "cwd": os.getcwd(), "listing": sorted(os.listdir(".")),
                       "env": dict(os.environ)}, f)
    cfg = argv[argv.index("--mcp-config") + 1] if "--mcp-config" in argv else None
    servers = list(json.load(open(cfg, encoding="utf-8")).get("mcpServers", {})) if cfg else []
    init = {"type": "system", "subtype": "init", "model": "fake-model", "cwd": os.getcwd(),
            "mcp_servers": [{"name": n, "status": "connected"} for n in servers],
            "tools": ["mcp__ai-app-studio__code_write_file", "mcp__ai-app-studio__code_read_file"],
            "session_id": "fake"}
    init.update(scenario.get("init") or {})
    emit(init)
    for ev in scenario.get("events") or []:
        emit(ev)
    if scenario.get("mcp_calls"):
        asyncio.run(run_mcp(cfg, scenario["mcp_calls"], scenario.get("project_id", "")))
    if scenario.get("final_events"):
        for ev in scenario["final_events"]:
            emit(ev)
    if scenario.get("sleep"):
        time.sleep(float(scenario["sleep"]))
    return int(scenario.get("exit", 0))


if __name__ == "__main__":
    raise SystemExit(main())
