"""Ask the vault-memory MCP server the work questions taken from the open coordination tasks.

This is a real MCP client talking to the real server over stdio, against the real vault and the
real per-user directory: every question is one line in the usage log that
memory_usage_report.py reads. It is NOT a Claude Code session: it is the MCP SDK acting as the
client, and it says so in the client name it sends.

    python 30_SCRIPTS/evaluation/run_memory_worklist.py [--limit 5] [--out FILE.json]

The questions are in 07_EVALUATION/memory_usage/work_queries.json; each carries the coordination
file and the verbatim passage it comes from, and the passage is checked before the question is asked.
What is written out is what the server returned (ids, titles, paths, lifecycle), not the notes' text.
"""
from __future__ import annotations

import argparse
import asyncio
import json
import os
import sys
from pathlib import Path
from typing import Any, Dict, List

REPO = Path(__file__).resolve().parents[2]
QUERIES = REPO / "07_EVALUATION" / "memory_usage" / "work_queries.json"
SERVER = REPO / "03_IMPLEMENTATION" / "packages" / "interfaces" / "memory_mcp_server.py"
CLIENT_NAME = "claude-sonnet-mcp-sdk-driver"


def load_queries() -> List[Dict[str, Any]]:
    queries = json.loads(QUERIES.read_text(encoding="utf-8"))["queries"]
    for q in queries:
        source = (REPO / q["source_file"]).read_text(encoding="utf-8")
        if q["source_quote"] not in source:
            raise SystemExit(f"question {q['id']}: the passage is not in {q['source_file']}")
    if len({q["question"] for q in queries}) != len(queries):
        raise SystemExit("the questions must all be different")
    return queries


async def run(limit: int, get_top: int) -> Dict[str, Any]:
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client
    from mcp.types import Implementation

    env = {k: v for k, v in os.environ.items() if k != "MEMORY_CONTROLLER_HMAC_SECRET"}
    params = StdioServerParameters(command=sys.executable, args=[str(SERVER)], env=env, cwd=str(REPO))
    results = []
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write, client_info=Implementation(name=CLIENT_NAME, version="1")) as session:
            await session.initialize()
            for q in load_queries():
                reply = await session.call_tool("memory_search", {"query": q["question"], "limit": limit})
                if reply.isError:
                    raise SystemExit(f"question {q['id']} failed: {reply.content}")
                payload = reply.structuredContent.get("result", reply.structuredContent)
                rows = [{k: r[k] for k in ("id", "title", "path", "type", "lifecycle", "verification", "score")}
                        for r in payload["query_results"]]
                results.append({"id": q["id"], "source_file": q["source_file"], "source_quote": q["source_quote"],
                                "question": q["question"], "returned": rows})
            gets = []
            for entry in results[:get_top]:
                readable = [r for r in entry["returned"] if r["lifecycle"] in ("ACTIVE", "REVIEW")]
                if not readable:
                    continue
                reply = await session.call_tool("memory_get", {"note_id": readable[0]["id"]})
                if not reply.isError:
                    note = reply.structuredContent.get("result", reply.structuredContent)
                    gets.append({"question_id": entry["id"], "id": note["id"], "title": note["title"],
                                 "lifecycle": note["lifecycle"], "unverified": note["unverified"],
                                 "content_chars": len(note["content"])})
    return {"client": CLIENT_NAME, "limit": limit, "results": results, "gets": gets}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--limit", type=int, default=5)
    parser.add_argument("--get-top", type=int, default=3, help="also memory_get the first readable result of the first N questions")
    parser.add_argument("--out", default=str(REPO / "07_EVALUATION" / "memory_usage" / "work_session_results.json"))
    args = parser.parse_args(argv)
    payload = asyncio.run(run(args.limit, args.get_top))
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    Path(args.out).write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(f"QUESTIONS={len(payload['results'])} GETS={len(payload['gets'])} OUT={args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
