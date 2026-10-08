"""Every AI client reaches the same server with a valid principal; the real registry is sound.

* MCP over real stdio: vault_* tools answer with the envelope; an MCP client cannot become owner.
* The per-client configurations (.mcp.json, .codex, .agents, .gemini) all start the same server
  with a principal the policy accepts through MCP.
* The real 04_CONFIG registry passes its CI check, and the routes everyone depends on exist.
* The PUBLIC export writes nothing a web AI may not see.
"""
from __future__ import annotations

import asyncio
import json
import os
import re
import subprocess
import sys
import tomllib
from pathlib import Path

import pytest
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

REPO = Path(__file__).resolve().parents[1]
PACKAGES = REPO / "03_IMPLEMENTATION" / "packages"
sys.path.insert(0, str(PACKAGES))
SERVER = PACKAGES / "interfaces" / "memory_mcp_server.py"

from vault_access.audit import AuditLog  # noqa: E402
from vault_access.core import VaultAccess  # noqa: E402
from vault_access.policy import AccessPolicy  # noqa: E402

POLICY = AccessPolicy.load(REPO / "04_CONFIG" / "access_policy.yaml")


@pytest.fixture(scope="module")
def state_home(tmp_path_factory):
    return tmp_path_factory.mktemp("vault_state")


_ACCESS = {}


def real(principal, interface, state_home):
    key = (principal, interface)
    if key not in _ACCESS:
        router = next(iter(_ACCESS.values())).router if _ACCESS else None
        _ACCESS[key] = VaultAccess(principal, interface, audit=AuditLog(enabled=False), router=router)
    return _ACCESS[key]


# ── client configurations ────────────────────────────────────────────────────────────────
def _server_args():
    configs = {
        ".mcp.json": json.loads((REPO / ".mcp.json").read_text(encoding="utf-8"))["mcpServers"]["vault-memory"],
        ".agents/mcp_config.json": json.loads((REPO / ".agents/mcp_config.json").read_text(encoding="utf-8"))["mcpServers"]["vault-memory"],
        ".gemini/settings.json": json.loads((REPO / ".gemini/settings.json").read_text(encoding="utf-8"))["mcpServers"]["vault-memory"],
        ".codex/config.toml": tomllib.loads((REPO / ".codex/config.toml").read_text(encoding="utf-8"))["mcp_servers"]["vault-memory"],
    }
    return configs


@pytest.mark.parametrize("name", [".mcp.json", ".agents/mcp_config.json", ".gemini/settings.json", ".codex/config.toml"])
def test_every_client_starts_the_same_server_with_a_valid_principal(name):
    entry = _server_args()[name]
    assert entry["command"] == "python"
    args = entry["args"]
    assert (REPO / args[0]).resolve() == SERVER.resolve()
    principal = args[args.index("--principal") + 1]
    assert principal in POLICY.principals
    assert POLICY.principal(principal, "mcp").name == principal          # accepted through MCP
    assert POLICY.effective_clearance(POLICY.principals[principal]) == "INTERNAL"


def test_principals_are_distinct_per_client():
    principals = [e["args"][e["args"].index("--principal") + 1] for e in _server_args().values()]
    assert len(set(principals)) == len(principals)


def test_instruction_files_point_every_runtime_at_agents_md():
    assert len((REPO / "AGENTS.md").read_bytes()) < 32 * 1024          # Codex project_doc_max_bytes
    gemini = (REPO / "GEMINI.md").read_text(encoding="utf-8")
    assert "AGENTS.md" in gemini and "vault_resolve" in gemini
    settings = json.loads((REPO / ".gemini/settings.json").read_text(encoding="utf-8"))
    assert "AGENTS.md" in settings["context"]["fileName"]
    agents = (REPO / "AGENTS.md").read_text(encoding="utf-8")
    for needle in ("vault_resolve", "vault_read", "cite_as", "export_public_vault.py", ".codex/config.toml"):
        assert needle in agents


# ── the real registry ────────────────────────────────────────────────────────────────────
def test_the_real_registry_passes_its_ci_check():
    out = subprocess.run([sys.executable, str(REPO / "30_SCRIPTS/routing/build_route_manifest.py"), "--check"],
                         cwd=REPO, capture_output=True, text=True, timeout=600)
    assert out.returncode == 0, out.stdout + out.stderr
    assert "route registry: OK" in out.stdout


CORE_ROUTES = [
    "vault://governance/vault_state",
    "vault://governance/rules/rules",
    "vault://governance/protocols/no_fabrication_policy",
    "vault://procedures/git_backup_restore_rollback",
    "vault://procedures/connecting_every_ai_to_the_vault",
    "vault://knowledge/xau_kinetic_clean_architecture",
    "vault://projects/loganalyzer_mvp",
    "vault://skills.governance/vault-navigation",
]


@pytest.mark.parametrize("uri", CORE_ROUTES)
def test_core_routes_exist_and_are_readable_by_a_coding_agent(uri, state_home):
    env = real("cloud_cli.codex", "mcp", state_home).read(uri, line_start=1, line_end=3)
    assert env["ok"], env


@pytest.mark.parametrize("query,uri", [
    ("VAULT_STATE", "vault://governance/vault_state"),
    ("No_Fabrication_Policy", "vault://governance/protocols/no_fabrication_policy"),
    ("procedura backup git", "vault://procedures/git_backup_restore_rollback"),
])
def test_file_names_and_plain_requests_resolve_directly(query, uri, state_home):
    env = real("cloud_cli.claude_code", "mcp", state_home).resolve(query)
    assert env["code"] == "OK" and env["route"]["uri"] == uri


def test_a_name_shared_by_two_files_is_ambiguous_with_both_offered(state_home):
    # Git_Backup_Restore_Rollback.md exists in procedures AND as an Obsidian artifact copy
    env = real("cloud_cli.claude_code", "mcp", state_home).resolve("Git_Backup_Restore_Rollback")
    assert env["code"] == "AMBIGUOUS"
    assert "vault://procedures/git_backup_restore_rollback" in {c["uri"] for c in env["candidates"]}


def test_quarantine_and_archive_are_never_served_to_agents(state_home):
    a = real("cloud_cli.claude_code", "mcp", state_home)
    visible = {d["domain"] for d in a.list("*")["domains"]}
    assert not visible & {"inbox", "archive", "knowledge.external_skills", "private"}
    assert "governance" in visible and "skills" in visible and "domain_packs.07_dotnet_wpf_desktop" in visible


# ── MCP over stdio ───────────────────────────────────────────────────────────────────────
def _session(env, principal):
    params = StdioServerParameters(command=sys.executable, args=[str(SERVER), "--principal", principal],
                                   env=env, cwd=str(REPO))
    return stdio_client(params)


def test_vault_tools_over_real_stdio(tmp_path, state_home):
    env = {k: v for k, v in os.environ.items() if k != "MEMORY_CONTROLLER_HMAC_SECRET"}
    env.update({"AI_MEMORY_VAULT_HOME": str(tmp_path / "state"), "PYTHONIOENCODING": "utf-8"})
    # warm metadata cache, so the server does not re-parse 4000 files on the first resolve
    router = real("cloud_cli.claude_code", "mcp", state_home).router
    router.cache_path = tmp_path / "state" / "route_meta_cache.json"
    router.load_all_meta()
    router._cache_dirty = True
    router._save_cache()

    async def go():
        async with _session(env, "cloud_cli.claude_code") as (read, write):
            async with ClientSession(read, write) as session:
                await session.initialize()

                async def call(tool, **args):
                    res = await session.call_tool(tool, args)
                    assert not res.isError, res.content
                    sc = res.structuredContent or {}
                    return sc.get("result", sc)
                return (await call("vault_resolve", query="VAULT_STATE"),
                        await call("vault_read", uri="vault://governance/vault_state", line_start=1, line_end=4),
                        await call("vault_read", uri="vault://inbox/readme"),
                        await call("vault_search", query="procedura backup git"))
    resolved, read, denied, searched = asyncio.run(go())
    assert resolved["route"]["uri"] == "vault://governance/vault_state"
    assert read["ok"] and read["cite_as"].startswith("vault://governance/vault_state sha256:")
    assert read["evidence"][0]["text"].startswith("# VAULT STATE")
    assert denied["code"] == "NOT_FOUND"
    # no HMAC secret in this environment: search degrades to routing metadata, it does not fail
    assert searched["ok"] and searched["mode"] == "routing_metadata_fallback"
    audit = (tmp_path / "state" / "vault_access_audit.jsonl").read_text(encoding="utf-8")
    assert "procedura backup git" not in audit and audit.count("\n") >= 4


def test_an_mcp_client_cannot_claim_to_be_the_owner(tmp_path):
    env = {**os.environ, "AI_MEMORY_VAULT_HOME": str(tmp_path / "state"), "PYTHONIOENCODING": "utf-8"}

    async def go():
        async with _session(env, "owner") as (read, write):
            async with ClientSession(read, write) as session:
                await session.initialize()
                return await session.call_tool("vault_read", {"uri": "vault://inbox/readme"})
    result = asyncio.run(go())
    assert result.isError
    assert "cannot be asserted through mcp" in json.dumps([c.model_dump() for c in result.content])


# ── PUBLIC export for web AIs ────────────────────────────────────────────────────────────
def test_public_export_contains_only_public_domains(tmp_path):
    sys.path.insert(0, str(REPO / "30_SCRIPTS" / "routing"))
    from export_public_vault import export
    result = export(tmp_path / "export", domains=["domain_packs.07_dotnet_wpf_desktop", "governance"])
    files = json.loads((tmp_path / "export" / "MANIFEST.json").read_text(encoding="utf-8"))["files"]
    assert result["files"] == len(files) > 0
    assert all(f["uri"].startswith("vault://domain_packs.07_dotnet_wpf_desktop/") for f in files)
    assert (tmp_path / "export" / "llms.txt").read_text(encoding="utf-8").startswith("# AI Memory Vault")
    with pytest.raises(SystemExit):
        export(REPO / "dist_should_not_exist")


def test_the_cli_refuses_owner_without_an_interactive_terminal():
    out = subprocess.run([sys.executable, "-m", "cognitive_core.vault_cli", "--principal", "owner", "ls", "inbox"],
                         cwd=REPO, capture_output=True, text=True, stdin=subprocess.DEVNULL, timeout=120)
    assert out.returncode == 3 and "interactive terminal" in out.stdout
    out = subprocess.run([sys.executable, "-m", "cognitive_core.vault_cli", "--principal", "local_llm.ollama",
                          "ls", "inbox"], cwd=REPO, capture_output=True, text=True, stdin=subprocess.DEVNULL, timeout=120)
    assert out.returncode == 3 and "cannot be asserted through cli" in out.stdout
