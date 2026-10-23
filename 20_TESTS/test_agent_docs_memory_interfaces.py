"""Every memory interface the agent instructions cite must exist.

CLAUDE.md told agents to call `http://localhost:8000/memory/search` and `/memory/propose`. Nothing
in the repository served those routes, so no agent ever reached the production search. Same
idea as test_readme_references.py, applied to the instruction files: a cited module, command flag,
MCP tool, server registration or path that does not exist fails the suite.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
PACKAGES = REPO / "03_IMPLEMENTATION" / "packages"
sys.path.insert(0, str(PACKAGES))

INSTRUCTION_FILES = [name for name in ("CLAUDE.md", "AGENTS.md", "GEMINI.md") if (REPO / name).exists()]
EXTERNAL_MODULES = {"pytest", "pip", "venv", "http.server"}
UNTRACKED_BY_DESIGN = ("06_INBOX/",)
SERVER_SOURCE = PACKAGES / "interfaces" / "memory_mcp_server.py"

_MODULE_CMD = re.compile(r"python3?\s+-m\s+([A-Za-z_][\w.]*)([^`\n]*)")
_FLAG = re.compile(r"(--[a-z][a-z0-9-]*)")
_TOOL = re.compile(r"\b((?:memory|vault)_[a-z_]+)\s*\(|`((?:memory|vault)_[a-z_]+)`")
_REST = re.compile(r"localhost:8000/memory\S*")
_ROOTS = (
    r"\d\d_[A-Z_]+|\.github|docs|scripts|security|telemetry"
    r"|\.agents|\.claude-plugin|\.claude|\.codex|\.gemini"
)
# Zero characters after the slash is a bare directory (`09_COORDINATION/`): it must exist too.
_PATH = re.compile(rf"`((?:{_ROOTS})/[^`\s<>*]*)`")
# A bare retired directory may be NAMED only on a line that says it is retired (AGENTS.md:23,52).
_RETIRED_DIRS = ("99_SYSTEM/", "09_COORDINATION/", "00_CORE/")
_RETIRED_MARKERS = ("retired", "retirat")
_NOT_EXISTING_MARKERS = ("nu există", "does not exist", "not exist")


def tool_names() -> set[str]:
    source = SERVER_SOURCE.read_text(encoding="utf-8")
    names: set[str] = set()
    for match in re.finditer(r"TOOL_NAMES\s*=\s*\(([^)]*)\)", source):
        names |= set(re.findall(r'"((?:memory|vault)_[a-z_]+)"', match.group(1)))
    return names


def module_sources(dotted: str) -> str | None:
    """Source of the module and of what a compatibility shim delegates to; None if it does not exist."""
    rel = Path(*dotted.split("."))
    texts = []
    for base in (REPO, PACKAGES):
        for candidate in (base / rel.with_suffix(".py"), base / rel / "__main__.py"):
            if candidate.exists():
                text = candidate.read_text(encoding="utf-8")
                texts.append(text)
                for pkg, mod in re.findall(r"from\s+(\w+)\.(\w+)\s+import\s+\*", text):
                    delegate = PACKAGES / pkg / f"{mod}.py"
                    if delegate.exists():
                        texts.append(delegate.read_text(encoding="utf-8"))
    return "\n".join(texts) if texts else None


def problems_in(text: str, repo: Path = REPO) -> list[str]:
    """Everything in `text` that names a memory interface that is not there."""
    found: list[str] = []
    known_tools = tool_names()
    for line in text.splitlines():
        for dotted, rest in _MODULE_CMD.findall(line):
            if dotted in EXTERNAL_MODULES:
                continue
            source = module_sources(dotted)
            if source is None:
                found.append(f"module {dotted} does not exist")
                continue
            for flag in _FLAG.findall(rest):
                if flag not in source:
                    found.append(f"{dotted} has no {flag}")
        for a, b in _TOOL.findall(line):
            name = a or b
            if name not in known_tools:
                found.append(f"MCP tool {name} does not exist")
        for url in _REST.findall(line):
            if not any(marker in line for marker in _NOT_EXISTING_MARKERS):
                found.append(f"{url} is cited as if it existed")
        for path in _PATH.findall(line):
            if path.startswith(_RETIRED_DIRS) and any(m in line.lower() for m in _RETIRED_MARKERS):
                continue
            if not path.startswith(UNTRACKED_BY_DESIGN) and not (repo / path.rstrip("/")).exists():
                found.append(f"path {path} does not exist")
    return found


@pytest.mark.parametrize("name", INSTRUCTION_FILES)
def test_every_memory_interface_cited_exists(name):
    assert problems_in((REPO / name).read_text(encoding="utf-8")) == []


# Agent profiles are instructions too: the router hands them to agents (`profile_ref` in
# 04_CONFIG/agent_router.json). All 21 pointed at `99_SYSTEM/` files deleted in dbcfbc502.
AGENT_PROFILES = sorted(p.relative_to(REPO).as_posix() for p in (REPO / ".agents" / "agents").glob("*/agent.md"))


def test_there_are_agent_profiles_to_check():
    assert len(AGENT_PROFILES) >= 21


@pytest.mark.parametrize("name", AGENT_PROFILES)
def test_every_path_an_agent_profile_cites_exists(name):
    assert problems_in((REPO / name).read_text(encoding="utf-8")) == []


def test_the_restored_policy_files_carry_their_provenance():
    for name in ("Agent_Capability_Registry.md", "Council_Context_Budget.md"):
        text = (REPO / "00_GOVERNANCE" / "protocols" / name).read_text(encoding="utf-8")
        assert "dbcfbc502^" in text and "Provenance" in text, name


@pytest.mark.parametrize("name", ["CLAUDE.md", "AGENTS.md"])
def test_the_instructions_point_at_the_real_interfaces(name):
    text = (REPO / name).read_text(encoding="utf-8")
    for needed in ("memory_search", "memory_propose", "recall_cli", "--init-secret", ".mcp.json"):
        assert needed in text, f"{name} does not mention {needed}"


def test_the_mcp_server_is_registered_and_points_at_a_real_file():
    config = json.loads((REPO / ".mcp.json").read_text(encoding="utf-8"))
    entry = config["mcpServers"]["vault-memory"]
    assert entry["command"] == "python"
    assert (REPO / entry["args"][0]).is_file()
    assert "vault-memory" in SERVER_SOURCE.read_text(encoding="utf-8")


def test_the_mcp_dependency_is_pinned_in_both_requirement_files():
    for name in ("requirements.txt", "requirements-memory-v6.txt"):
        lines = [l.strip().lstrip("\ufeff") for l in (REPO / name).read_text(encoding="utf-8").splitlines()]
        assert any(re.fullmatch(r"mcp==\d+\.\d+\.\d+", l) for l in lines), name


# ---- negative controls: the check must be able to fail ---------------------------------------

def test_negative_control_a_missing_module_is_reported():
    assert "module cognitive_core.no_such_cli does not exist" in problems_in("Run `python -m cognitive_core.no_such_cli --query x`.")


def test_negative_control_a_flag_the_cli_does_not_have_is_reported():
    assert "cognitive_core.recall_cli has no --make-coffee" in problems_in("`python -m cognitive_core.recall_cli --make-coffee`")


def test_negative_control_an_unknown_mcp_tool_is_reported():
    assert problems_in("call `memory_teleport(x)` first") == ["MCP tool memory_teleport does not exist"]
    assert problems_in("call `vault_teleport(x)` first") == ["MCP tool vault_teleport does not exist"]


def test_negative_control_the_fictional_rest_route_is_reported_unless_marked_absent():
    assert problems_in("Use `http://localhost:8000/memory/search?query=x`.") != []
    assert problems_in("`http://localhost:8000/memory/search` nu există în acest depozit.") == []


def test_negative_control_a_missing_path_is_reported():
    assert problems_in("see `30_SCRIPTS/evaluation/no_such_report.py`") == ["path 30_SCRIPTS/evaluation/no_such_report.py does not exist"]


def test_the_check_would_have_caught_the_original_claude_md():
    original = ("Use the existing Vault memory interface when available:\n"
                "`http://localhost:8000/memory/search?query=subiectul_cautat`\n"
                "`http://localhost:8000/memory/propose`\n")
    assert len(problems_in(original)) == 2


def test_bare_nonexistent_directory_is_reported():
    text = "use `99_NOPE/` and `docs/` and `09_COORDINATION/` here\nthe old `99_SYSTEM/` is retired\n"
    out = problems_in(text)
    assert "path 99_NOPE/ does not exist" in out
    assert "path 09_COORDINATION/ does not exist" in out
    assert "path 99_SYSTEM/ does not exist" not in out
    assert not any("docs/" in o for o in out)
