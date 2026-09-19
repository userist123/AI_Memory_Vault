"""The vault-memory MCP server, over a real stdio connection.

A subprocess runs `memory_mcp_server.py` against a temporary vault and a temporary per-user
directory; the test speaks MCP to it with the official client. Nothing is mocked: the same
path a Claude Code, Antigravity or Gemini CLI session takes.
"""
from __future__ import annotations

import asyncio
import importlib.util
import json
import os
import sys
from pathlib import Path

import pytest
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
SERVER = REPO / "03_IMPLEMENTATION" / "packages" / "interfaces" / "memory_mcp_server.py"
SECRET = "test-secret-for-the-mcp-server-" + "x" * 20

_spec = importlib.util.spec_from_file_location("memory_vault_fixture", REPO / "20_TESTS" / "memory_vault_fixture.py")
fx = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(fx)

from interfaces import vault_runtime  # noqa: E402


@pytest.fixture
def setup(tmp_path):
    vault = fx.make_vault(tmp_path)
    state = tmp_path / "state"
    env = {k: v for k, v in os.environ.items() if k != vault_runtime.SECRET_ENV}
    env.update({vault_runtime.HOME_ENV: str(state), vault_runtime.SECRET_ENV: SECRET,
                "MEMORY_VAULT_ROOT": str(vault), "PYTHONIOENCODING": "utf-8"})
    return vault, state, env


def _session(env):
    params = StdioServerParameters(command=sys.executable, args=[str(SERVER)], env=env, cwd=str(REPO))
    return stdio_client(params)


def contains_any(text, needles):
    return [n for n in needles if n in text]


async def _call(session, tool, **arguments):
    result = await session.call_tool(tool, arguments)
    assert not result.isError, result.content
    return result.structuredContent["result"] if "result" in (result.structuredContent or {}) else result.structuredContent


def test_the_server_exposes_exactly_the_three_tools_and_no_attest():
    async def go(env):
        async with _session(env) as (read, write):
            async with ClientSession(read, write) as session:
                await session.initialize()
                return {t.name for t in (await session.list_tools()).tools}
    env = {**os.environ, vault_runtime.SECRET_ENV: SECRET}
    names = asyncio.run(go(env))
    assert names == {"memory_search", "memory_get", "memory_propose"}
    assert not any("attest" in n or "slot" in n or "verify" in n for n in names)


def test_search_propose_get_over_stdio_and_the_usage_log(setup):
    vault, state, env = setup
    query = "bugetul grafului valoarea implicita expansiune"
    title = "Propunere din sesiunea de test MCP"

    async def go():
        async with _session(env) as (read, write):
            async with ClientSession(read, write) as session:
                info = await session.initialize()
                found = await _call(session, "memory_search", query=query, limit=3)
                proposed = await _call(session, "memory_propose", title=title,
                                       body="Aceasta propunere trebuie gasita de cautare in aceeasi sesiune.",
                                       type="knowledge", provenance={"source_type": "ai", "source_ref": "test"})
                again = await _call(session, "memory_search", query="propunere sesiunea de test MCP gasita in aceeasi sesiune", limit=5)
                note = await _call(session, "memory_get", note_id=proposed["id"])
                return info.serverInfo.name, found, proposed, again, note

    server_name, found, proposed, again, note = asyncio.run(go())

    assert server_name == "vault-memory"
    assert found["count"] >= 1 and "bugetul grafului" in [r["title"] for r in found["query_results"]]

    # the candidate: REVIEW, unverified, in the content tree, on disk
    assert proposed["lifecycle"] == "REVIEW" and proposed["verification"] == "unverified"
    assert proposed["path"].startswith("01_ARCHITECTURE/knowledge/") and (vault / proposed["path"]).exists()
    # found by search in the same session, and readable
    assert proposed["id"] in [r["id"] for r in again["query_results"]]
    assert note["lifecycle"] == "REVIEW" and note["unverified"] is True

    # the usage log: one line per call, hashes not texts
    rows = vault_runtime.read_usage_log(state / "usage.jsonl")
    assert [r["tool"] for r in rows] == ["memory_search", "memory_propose", "memory_search", "memory_get"]
    assert all(r["outcome"] == "ok" and r["client"] for r in rows)
    assert rows[0]["query_sha256"] == vault_runtime.query_digest(query)
    assert rows[0]["n_results"] == found["count"] and rows[0]["ids"] == [r["id"] for r in found["query_results"]]
    log_text = (state / "usage.jsonl").read_text(encoding="utf-8")
    assert contains_any(log_text, (query, title, "Aceasta propunere", SECRET)) == []
    assert all(isinstance(r["latency_ms"], (int, float)) for r in rows)


def test_a_bad_proposal_is_an_error_result_and_is_logged(setup):
    vault, state, env = setup

    async def go():
        async with _session(env) as (read, write):
            async with ClientSession(read, write) as session:
                await session.initialize()
                return await session.call_tool("memory_propose", {"title": "t", "body": "b",
                                                                    "provenance": {"source_type": "official"}})

    result = asyncio.run(go())
    assert result.isError
    rows = vault_runtime.read_usage_log(state / "usage.jsonl")
    assert rows and rows[-1]["tool"] == "memory_propose" and rows[-1]["outcome"] == "error"
    assert not list((vault / "01_ARCHITECTURE" / "knowledge").glob("t_*.md"))


def test_negative_control_the_server_without_a_secret_names_the_fix_and_writes_nothing(tmp_path):
    vault = fx.make_vault(tmp_path)
    env = {k: v for k, v in os.environ.items() if k != vault_runtime.SECRET_ENV}
    env.update({vault_runtime.HOME_ENV: str(tmp_path / "empty"), "MEMORY_VAULT_ROOT": str(vault)})

    async def go():
        async with _session(env) as (read, write):
            async with ClientSession(read, write) as session:
                await session.initialize()
                return await session.call_tool("memory_search", {"query": "orice"})

    result = asyncio.run(go())
    assert result.isError
    text = json.dumps([c.model_dump() for c in result.content])
    assert "--init-secret" in text
    assert list((vault / "01_ARCHITECTURE" / "knowledge").glob("*.md")).__len__() == len(fx.SEED_NOTES)


def test_negative_control_a_plaintext_query_in_the_log_would_be_caught():
    """The 'no query text in the log' check must be able to fail."""
    leaky = json.dumps({"tool": "memory_search", "query": "text in clear"})
    assert contains_any(leaky, ("text in clear",)) == ["text in clear"]
    assert contains_any(json.dumps({"query_sha256": vault_runtime.query_digest("text in clear")}), ("text in clear",)) == []
