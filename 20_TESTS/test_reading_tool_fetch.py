"""Reading tool (MCP server `fetch`): configuration, guard behaviour and a live stdio handshake.

The server runs isolated through uv (`python -m uv tool run mcp-server-fetch==<pin>`), because
mcp-server-fetch needs mcp 2.x while the repository pins mcp==1.30.0. The handshake runs only
where uv is installed (requirements-reading-tools.txt); CI does not install it, so there it is skipped.
"""
import importlib.util
import json
import re
import subprocess
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
GUARD_DIR = REPO / ".claude" / "skills" / "agent-integrity-guard"


def _mcp():
    return json.loads((REPO / ".mcp.json").read_text(encoding="utf-8"))["mcpServers"]


def test_fetch_server_is_declared_for_claude_code():
    entry = _mcp()["fetch"]
    assert entry["command"] == "python"
    assert entry["args"][:4] == ["-m", "uv", "tool", "run"]
    assert re.fullmatch(r"mcp-server-fetch==\d{4}\.\d{1,2}\.\d{1,2}", entry["args"][4])
    assert len(entry["args"]) == 5


def test_vault_memory_entry_is_untouched():
    entry = _mcp()["vault-memory"]
    assert entry["args"][0] == "03_IMPLEMENTATION/packages/interfaces/memory_mcp_server.py"
    assert entry["args"][entry["args"].index("--principal") + 1] == "cloud_cli.claude_code"


def test_dependency_is_pinned():
    lines = [l.strip() for l in (REPO / "requirements-reading-tools.txt").read_text(encoding="utf-8").splitlines()
             if l.strip() and not l.lstrip().startswith("#")]
    assert len(lines) == 1 and re.fullmatch(r"uv==\d+\.\d+\.\d+", lines[0])
    # the server must not share the repository interpreter: it would upgrade mcp past the pin
    assert "mcp-server-fetch" not in (REPO / "requirements.txt").read_text(encoding="utf-8")


def test_skill_and_procedure_exist_and_point_at_each_other():
    skill = (REPO / ".claude/skills/surse-web/SKILL.md").read_text(encoding="utf-8")
    head = skill.split("---")[1]
    assert "name: surse-web" in head and "description:" in head
    assert "Reading_Web_Sources_With_Fetch.md" in skill
    proc = (REPO / "10_DOCUMENTATION/procedures/Reading_Web_Sources_With_Fetch.md").read_text(encoding="utf-8")
    assert "lifecycle: REVIEW" in proc and "verification: unverified" in proc
    assert ".claude/skills/surse-web/SKILL.md" in proc


def test_guard_still_asks_before_every_fetch_call():
    allow = json.loads((GUARD_DIR / "allowlist.json").read_text(encoding="utf-8"))
    assert not any("mcp__fetch__fetch".startswith(p) for p in allow.get("mcp_allow_prefixes", []))
    sys.path.insert(0, str(GUARD_DIR))
    try:
        import guard as g
    finally:
        sys.path.pop(0)
    assert g.decide_mcp("mcp__fetch__fetch", allow).action == g.ASK


@pytest.mark.skipif(importlib.util.find_spec("uv") is None,
                    reason="uv not installed (requirements-reading-tools.txt)")
def test_server_starts_and_lists_the_fetch_tool():
    msgs = [
        {"jsonrpc": "2.0", "id": 1, "method": "initialize",
         "params": {"protocolVersion": "2024-11-05", "capabilities": {},
                    "clientInfo": {"name": "vault-test", "version": "0"}}},
        {"jsonrpc": "2.0", "method": "notifications/initialized"},
        {"jsonrpc": "2.0", "id": 2, "method": "tools/list"},
    ]
    proc = subprocess.Popen([sys.executable] + _mcp()["fetch"]["args"], stdin=subprocess.PIPE,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8")
    try:
        for m in msgs:
            proc.stdin.write(json.dumps(m) + "\n")
            proc.stdin.flush()
        tools = None
        for _ in range(20):
            line = proc.stdout.readline()
            if not line:
                break
            obj = json.loads(line)
            if obj.get("id") == 2:
                tools = [t["name"] for t in obj["result"]["tools"]]
                break
    finally:
        proc.kill()
        proc.wait(timeout=10)
    assert tools is not None and "fetch" in tools
