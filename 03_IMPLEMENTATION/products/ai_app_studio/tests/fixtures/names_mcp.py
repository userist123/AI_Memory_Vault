"""Minimal stdio MCP server for the real-CLI probes: publishes the tool names given on argv; a call returns
``CALLED-<name>``. No dependencies (raw JSON-RPC lines), so it starts fast under any interpreter."""
from __future__ import annotations

import json
import sys


def send(obj: dict) -> None:
    sys.stdout.write(json.dumps(obj) + "\n")
    sys.stdout.flush()


def main() -> None:
    names = sys.argv[1:]
    for line in sys.stdin:
        try:
            msg = json.loads(line)
        except ValueError:
            continue
        mid, method = msg.get("id"), msg.get("method")
        if mid is None:
            continue  # notification
        if method == "initialize":
            version = (msg.get("params") or {}).get("protocolVersion", "2025-06-18")
            send({"jsonrpc": "2.0", "id": mid, "result": {"protocolVersion": version, "capabilities": {"tools": {}},
                                                          "serverInfo": {"name": "names", "version": "1"}}})
        elif method == "tools/list":
            send({"jsonrpc": "2.0", "id": mid, "result": {"tools": [
                {"name": n, "description": n, "inputSchema": {"type": "object", "properties": {}}} for n in names]}})
        elif method == "tools/call":
            name = (msg.get("params") or {}).get("name", "")
            send({"jsonrpc": "2.0", "id": mid, "result": {"content": [{"type": "text", "text": f"CALLED-{name}"}]}})
        else:
            send({"jsonrpc": "2.0", "id": mid, "error": {"code": -32601, "message": "method not found"}})


if __name__ == "__main__":
    main()
