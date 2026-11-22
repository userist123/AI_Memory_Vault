"""cost-router best-effort guard for the dfir-analyst (no network / no writes) and test-author (tests only) roles."""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
HOOK = ROOT / ".claude" / "skills" / "cost-router" / "hook_enforce_route.py"


def _call(tmp_path, agent_type, tool, tool_input):
    env = dict(os.environ, HOME=str(tmp_path), USERPROFILE=str(tmp_path))
    for k in ("COST_ROUTER_OFF", "COST_ROUTER_ENFORCE"):
        env.pop(k, None)
    payload = {"session_id": "rg-1", "tool_name": tool, "tool_input": tool_input}
    if agent_type:
        payload.update(agent_type=agent_type, agent_id="a1")
    r = subprocess.run([sys.executable, str(HOOK)], input=json.dumps(payload), capture_output=True, text=True,
                       timeout=60, env=env, encoding="utf-8")
    assert r.returncode == 0, r.stderr
    if not r.stdout.strip():
        return None
    out = json.loads(r.stdout)["hookSpecificOutput"]
    return out.get("permissionDecision")


DFIR_DENIED = [
    "curl https://example.com", "wget http://x/y", "Invoke-WebRequest http://x", "iwr http://x", "irm http://x",
    "Invoke-RestMethod http://x", "ssh host", "scp a host:b", "ftp host", "nc host 80", "python -m http.server",
    "pip install requests", "python -m pip install requests", "npm install", "git push origin main", "git fetch",
    "git clone https://x/y", "echo hi > out.txt", "echo hi >> out.txt", "Get-Content a | Out-File b",
    "Set-Content b hi", "Add-Content b hi", "cat a | tee b", "cp a b", "mv a b", "rm a", "del a", "Remove-Item a",
    "New-Item x", "cd C:/x && curl http://y", "ls; wget http://y",
]
DFIR_ALLOWED = [
    "ls -la", "cat evidence/a.evtx.json", "grep -r 4624 logs", "git status", "git log --oneline",
    "python -c \"print(1)\"", "sha256sum a.bin", "Get-FileHash a.bin", "grep -c 'a>b' file",
    "git diff 2>&1", "echo 2>/dev/null",
]


@pytest.mark.parametrize("cmd", DFIR_DENIED)
def test_dfir_bash_network_and_writes_are_denied(tmp_path, cmd):
    assert _call(tmp_path, "dfir-analyst", "Bash", {"command": cmd}) == "deny"


@pytest.mark.parametrize("cmd", DFIR_ALLOWED)
def test_dfir_read_only_bash_is_allowed(tmp_path, cmd):
    assert _call(tmp_path, "dfir-analyst", "Bash", {"command": cmd}) is None


def test_other_agents_and_main_session_bash_is_untouched_by_the_dfir_guard(tmp_path):
    assert _call(tmp_path, "vault-worker", "Bash", {"command": "curl http://x"}) is None
    assert _call(tmp_path, "test-author", "Bash", {"command": "git push"}) is None
    assert _call(tmp_path, None, "Bash", {"command": "curl http://x"}) is None


TEST_OK = ["20_TESTS/test_x.py", "pkg/tests/helper.py", "pkg/test/a.py", "a/b/foo_test.py", "src/Foo.Tests.cs",
           "src/FooTests.cs", "C:/repo/20_TESTS/conftest.py", "C:" + chr(92) + "repo" + chr(92) + "20_TESTS" + chr(92) + "conftest.py", "test_y.py"]
TEST_DENIED = ["src/app.py", "lib/check.py", "README.md", "C:" + chr(92) + "repo" + chr(92) + ".claude" + chr(92) + "x" + chr(92) + "SKILL.md", "latest/a.py",
               "contest.py", "20_TESTS_x/a.py"]


@pytest.mark.parametrize("tool,key", [("Write", "file_path"), ("Edit", "file_path"), ("NotebookEdit", "notebook_path")])
@pytest.mark.parametrize("path", TEST_DENIED)
def test_test_author_cannot_write_outside_test_paths(tmp_path, tool, key, path):
    assert _call(tmp_path, "test-author", tool, {key: path}) == "deny"


@pytest.mark.parametrize("path", TEST_OK)
def test_test_author_may_write_test_paths(tmp_path, path):
    assert _call(tmp_path, "test-author", "Write", {"file_path": path}) is None


def test_other_agents_write_is_untouched_by_the_test_author_guard(tmp_path):
    assert _call(tmp_path, "vault-worker", "Write", {"file_path": "src/app.py"}) is None


def test_guard_fails_open_on_a_malformed_payload(tmp_path):
    assert _call(tmp_path, "dfir-analyst", "Bash", {"command": 5}) is None
    assert _call(tmp_path, "test-author", "Write", {}) is None
