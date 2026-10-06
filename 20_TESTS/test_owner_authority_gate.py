import json
import os
import subprocess
import sys
from pathlib import Path

HOOK = Path(".claude/hooks/owner_authority_gate.py")

def run_hook(payload, env=None):
    merged = os.environ.copy()
    if env:
        merged.update(env)
    return subprocess.run([sys.executable, str(HOOK)],
                          input=json.dumps(payload), text=True,
                          capture_output=True, env=merged, check=False)

def test_mutation_denied_without_owner_gate():
    r = run_hook({"tool_name": "Bash", "tool_input": {"command": "git push"}})
    data = json.loads(r.stdout)
    assert data["hookSpecificOutput"]["permissionDecision"] == "deny"

def test_read_only_tool_allowed():
    r = run_hook({"tool_name": "Read", "tool_input": {"file_path": "README.md"}})
    assert r.stdout == ""

def test_false_owner_gate_denies():
    gate = "python -c \"import json,sys; print(json.dumps({'approved': False}))\""
    r = run_hook({"tool_name": "Bash", "tool_input": {"command": "git push"}},
                  {"MEMORY_VAULT_OWNER_GATE_COMMAND": gate})
    data = json.loads(r.stdout)
    assert data["hookSpecificOutput"]["permissionDecision"] == "deny"

def test_explicit_owner_gate_can_approve():
    gate = "python -c \"import json,sys; e=json.load(sys.stdin); print(json.dumps({'approved': e['tool_name']=='Bash'}))\""
    r = run_hook({"tool_name": "Bash", "tool_input": {"command": "git push"}},
                  {"MEMORY_VAULT_OWNER_GATE_COMMAND": gate})
    assert r.stdout == ""


def test_read_only_vault_mcp_tools_are_allowed():
    for name in ("vault_read", "vault_resolve", "memory_search", "memory_get"):
        r = run_hook({"tool_name": f"mcp__vault-memory__{name}", "tool_input": {}})
        assert r.stdout == "", name


def test_memory_propose_and_lookalike_tools_need_the_owner_gate():
    for name in ("mcp__vault-memory__memory_propose", "mcp__evil__vault_read",
                 "mcp__vault-memory__vault_read_and_write", "Write", "Edit"):
        r = run_hook({"tool_name": name, "tool_input": {}})
        assert json.loads(r.stdout)["hookSpecificOutput"]["permissionDecision"] == "deny", name


def test_the_hook_is_registered_for_every_tool():
    settings = json.loads(Path(".claude/settings.json").read_text(encoding="utf-8"))
    entry = settings["hooks"]["PreToolUse"][0]
    assert entry["matcher"] == ".*"
    assert entry["hooks"][0]["command"].endswith("owner_authority_gate.py")
