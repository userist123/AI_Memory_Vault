"""cost-router M8: lib/check.py acceptance receipts and the check-runner agent.

Temporary HOME everywhere; the real ~/.claude is never touched. Receipts are informational: they never grant authority.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / ".claude" / "skills" / "cost-router"
CHECK = SKILL / "lib" / "check.py"
AGENT = ROOT / ".claude" / "agents" / "check-runner.md"
sys.path.insert(0, str(SKILL / "lib"))
sys.path.insert(0, str(Path(__file__).resolve().parent))

import check  # noqa: E402
import registry_compile as rc  # noqa: E402


def _run(home: Path, *args, cwd=None):
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home))
    env.pop("CLAUDE_SESSION_ID", None)
    return subprocess.run([sys.executable, "-I", str(CHECK), *args], capture_output=True, text=True,
                          encoding="utf-8", timeout=120, env=env, cwd=str(cwd or home))


def _receipts(home: Path):
    d = home / ".claude" / "cache" / "cost-router" / "checks"
    return sorted(d.glob("*.json")) if d.is_dir() else []


@pytest.fixture
def home(tmp_path):
    h = tmp_path / "home"
    h.mkdir()
    return h


def test_pass_and_fail_lines_and_receipt_fields(home):
    r = _run(home, "run", "--label", "unit", "--", sys.executable, "-c", "print('ok')")
    assert r.returncode == 0 and r.stdout.strip() == "check unit: exit=0 pass"
    r = _run(home, "run", "--label", "unit2", "--", sys.executable, "-c", "import sys; print('boom'); sys.exit(3)")
    assert r.returncode == 3 and r.stdout.strip() == "check unit2: exit=3 fail"
    files = _receipts(home)
    assert len(files) == 2
    rec = check_last(home, "unit2")
    assert rec["exit"] == 3 and rec["label"] == "unit2" and "boom" in rec["tail"]
    assert len(rec["cmd_sha256"]) == 64 and rec["duration_s"] >= 0
    assert check.command_sha256([sys.executable, "-c", "x"]) == check.command_sha256([sys.executable, "-c", "x"])
    assert files[0].name.split("-")[0].isdigit()  # <pid>-<epoch>.json when no session is given


def check_last(home: Path, label: str):
    r = _run(home, "last", "--label", label)
    assert r.returncode == 0, r.stdout
    return json.loads(r.stdout)


def test_tail_is_last_40_lines(home):
    code = "print('\\n'.join(f'line{i}' for i in range(100)))"
    _run(home, "run", "--label", "long", "--", sys.executable, "-c", code)
    rec = check_last(home, "long")
    assert len(rec["tail"]) == 40 and rec["tail"][-1] == "line99" and rec["tail"][0] == "line60"


def test_pytest_summary_is_parsed(home, tmp_path):
    t = tmp_path / "proj"
    t.mkdir()
    (t / "test_x.py").write_text("def test_a():\n    assert True\n\ndef test_b():\n    assert False\n\n"
                                 "def test_c():\n    assert True\n", encoding="utf-8")
    r = _run(home, "run", "--label", "py", "--", sys.executable, "-m", "pytest", "-q", "-p", "no:cacheprovider",
             str(t / "test_x.py"))
    assert r.returncode == 1
    assert r.stdout.strip() == "check py: exit=1 fail (pytest 2 passed, 1 failed)", r.stdout
    (t / "test_x.py").write_text("def test_a():\n    assert True\n", encoding="utf-8")
    r = _run(home, "run", "--label", "py", "--", sys.executable, "-m", "pytest", "-q", "-p", "no:cacheprovider",
             str(t / "test_x.py"))
    assert r.returncode == 0 and r.stdout.strip() == "check py: exit=0 pass (pytest 1 passed)", r.stdout


def test_parse_summary_variants():
    assert check.parse_summary(["=== 2 failed, 8 passed, 1 skipped in 3.20s ==="]) == "pytest 8 passed, 2 failed, 1 skipped"
    assert check.parse_summary(["3 errors in 0.5s"]) == "pytest 3 error"
    assert check.parse_summary(["Passed!  - Failed:     0, Passed:    10, Skipped:     1, Total:    11, Duration: 2 s"]) \
        == "dotnet 10 passed, 0 failed, 1 skipped, total 11"
    assert check.parse_summary(["Failed!  - Failed:     2, Passed:     5, Skipped:     0, Total:     7"]) \
        == "dotnet 5 passed, 2 failed, 0 skipped, total 7"
    assert check.parse_summary(["nothing here", "12 passed is a nice thing to say"]) == ""


def test_dotnet_style_output_through_run(home):
    code = "print('Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 s')"
    r = _run(home, "run", "--label", "dn", "--", sys.executable, "-c", code)
    assert r.stdout.strip() == "check dn: exit=0 pass (dotnet 4 passed, 0 failed, 0 skipped, total 4)"


def test_no_shell_and_missing_command(home, tmp_path):
    sentinel = tmp_path / "shell-ran.txt"
    r = _run(home, "run", "--label", "meta", "--", sys.executable, "-c", "import sys; print(sys.argv[1])",
             f"x; touch {sentinel} && echo > {sentinel}")
    assert r.returncode == 0 and not sentinel.exists()
    r = _run(home, "run", "--label", "gone", "--", "definitely-not-a-command-xyz")
    assert r.returncode == 2 and "not found" in r.stdout.lower()  # F4: refused up front, no receipt


def test_usage_errors_and_last_without_receipt(home):
    assert _run(home, "run", "--label", "bad label", "--", sys.executable, "-c", "1").returncode == 2
    assert _run(home, "run", "--label", "x").returncode == 2
    r = _run(home, "last")
    assert r.returncode == 1 and "no receipt" in r.stdout


def test_last_filters_by_label_and_session(home):
    _run(home, "run", "--session", "sA", "--label", "a", "--", sys.executable, "-c", "1")
    _run(home, "run", "--session", "sB", "--label", "b", "--", sys.executable, "-c", "import sys; sys.exit(1)")
    assert check_last(home, "a")["session"] == "sA"
    r = _run(home, "last", "--session", "sB")
    assert json.loads(r.stdout)["label"] == "b"
    assert any(p.name.startswith("sA-") for p in _receipts(home))


def test_last_failing_is_superseded_by_a_later_pass(home, monkeypatch):
    monkeypatch.setenv("HOME", str(home))
    monkeypatch.setenv("USERPROFILE", str(home))
    _run(home, "run", "--session", "s9", "--label", "unit", "--", sys.executable, "-c", "import sys; sys.exit(2)")
    assert check.last_failing("s9")["exit"] == 2
    assert check.last_failing("other") is None and check.last_failing("") is None
    import time
    time.sleep(0.01)
    _run(home, "run", "--session", "s9", "--label", "unit", "--", sys.executable, "-c", "1")
    assert check.last_failing("s9") is None


# ---- escalation note cites the last failing receipt ---------------------------------------------------------------

def test_escalation_note_cites_the_failing_receipt_informationally(tmp_path):
    import test_cost_router_escalation_matrix as em
    home, proj = tmp_path / "ehome", tmp_path / "eproj"
    home.mkdir()
    proj.mkdir()
    import hashlib
    pins = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted((ROOT / ".claude" / "agents").glob("*.md"))}
    (home / ".claude" / "skills" / "cost-router").mkdir(parents=True)
    (home / ".claude" / "skills" / "cost-router" / "agents.pinned.json").write_text(json.dumps(pins), encoding="utf-8")
    env = (home, proj)
    em.prompt(env, em.BRIEFS["review-high"] + " #escalate", "sonnet")
    out = em.spawn(env, "sonnet", "review-high", "vault-reviewer")
    assert out["permissionDecision"] == "allow" and "escalated sonnet -> opus" in em.note(out)
    assert "after failed check" not in em.note(out)
    _run(home, "run", "--session", "s1", "--label", "unit", "--", sys.executable, "-c", "import sys; sys.exit(3)")
    em.prompt(env, em.BRIEFS["review-high"] + " #escalate", "sonnet")
    out = em.spawn(env, "sonnet", "review-high", "vault-reviewer")
    assert out["permissionDecision"] == "allow" and out["updatedInput"]["model"] == "opus"  # same decision as before
    assert "after failed check unit exit=3" in em.note(out)
    # a receipt of ANOTHER session is not cited, and a denied (not escalated) spawn is unchanged
    em.prompt(env, em.BRIEFS["review-high"], "sonnet", sid="s2")
    out = em.spawn(env, "sonnet", "review-high", "vault-reviewer", sid="s2")
    assert out["permissionDecision"] == "deny" and "after failed check" not in json.dumps(out)


# ---- the agent ------------------------------------------------------------------------------------------------------

def _frontmatter(path: Path) -> dict:
    text = path.read_text(encoding="utf-8")
    assert text.startswith("---\n")
    block = text.split("\n---", 1)[0].splitlines()[1:]
    return {ln.split(":", 1)[0].strip(): ln.split(":", 1)[1].strip() for ln in block if ":" in ln}


def test_check_runner_agent_is_cheap_read_mostly_and_not_privileged():
    fm = _frontmatter(AGENT)
    assert fm["name"] == "check-runner" and fm["model"] == "haiku"
    assert [t.strip() for t in fm["tools"].split(",")] == ["Read", "Grep", "Glob", "Bash"]
    assert "Edit" not in fm["tools"] and "Write" not in fm["tools"]
    body = AGENT.read_text(encoding="utf-8")
    assert "check.py" in body and "never edits" in fm["description"].lower()
    assert "never interprets causes" in fm["description"].lower()
    install = (SKILL / "install.py").read_text(encoding="utf-8")
    priv = [ln for ln in install.splitlines() if ln.startswith("PRIVILEGED_AGENTS")][0]
    assert "check-runner" not in priv
    assert fm["model"] not in ("opus", "fable")
    assert "fable" not in AGENT.read_text(encoding="utf-8").lower()


def test_sidecar_is_accepted_by_the_compiler_and_matches_the_agent():
    sc = json.loads((SKILL / "roles" / "check-runner.json").read_text(encoding="utf-8"))
    assert sc["authority"] == {"write": "none", "execute": True, "network": "none", "spawn": False}
    assert sc["tier"]["default"] == "haiku" and sc["tier"]["max"] != "fable"
    reg, data = rc.compile_registry(SKILL, ROOT / ".claude" / "skills", ROOT / ".claude" / "agents")
    entry = {e["id"]: e for e in reg["entries"]}["check-runner"]
    assert entry["kind"] == "agent" and entry["subagent_type"] == "check-runner"
    hashes = json.loads(rc.compile_full(SKILL, ROOT / ".claude" / "skills", ROOT / ".claude" / "agents").hashes)
    assert entry["tier"][0] == "haiku" and hashes["entries"]["check-runner"]["artifact_sha256"]
    assert len(data) <= 16384


# ---- .cmd/.bat shims: cmd.exe re-parses the command line (reviewer probe ph) -------------------------------------

CMD_METACHARS = ["&", "|", "<", ">", "^", "%", '"', "\n"]


@pytest.mark.skipif(os.name != "nt", reason=".cmd shims only matter on Windows")
@pytest.mark.parametrize("ch", CMD_METACHARS, ids=["amp", "pipe", "lt", "gt", "caret", "pct", "quote", "newline"])
def test_cmd_shim_with_a_cmd_metacharacter_argument_is_refused(home, tmp_path, ch):
    shim = tmp_path / "shim.cmd"
    shim.write_text("@echo off"+chr(13)+chr(10)+"echo ARGS=%*"+chr(13)+chr(10), encoding="ascii")
    sentinel = tmp_path / "PWNED.txt"
    r = _run(home, "run", "--label", "b", "--", str(shim), f'x{ch} echo hacked > "{sentinel}"')
    assert r.returncode == 2 and "cmd" in r.stdout.lower() and "refus" in r.stdout.lower()
    assert not sentinel.exists()
    assert _receipts(home) == []  # nothing ran, nothing recorded


@pytest.mark.skipif(os.name != "nt", reason=".cmd shims only matter on Windows")
def test_cmd_shim_with_plain_arguments_still_runs(home, tmp_path):
    shim = tmp_path / "shim.cmd"
    shim.write_text("@echo off\r\nexit /b 0\r\n", encoding="ascii")
    r = _run(home, "run", "--label", "b", "--", str(shim), "plain", "--flag=value", "path/with-dash_1.txt")
    assert r.returncode == 0 and "exit=0 pass" in r.stdout


def test_metacharacters_are_fine_for_a_real_executable(home):
    r = _run(home, "run", "--label", "ok", "--", sys.executable, "-c", "print('a & b | c ^ % \"')")
    assert r.returncode == 0 and "exit=0 pass" in r.stdout


def test_shim_refusal_helper_is_pure(tmp_path):
    assert check.shim_refusal([sys.executable, "-c", "x&y"]) is None
    shim = tmp_path / "t.bat"
    shim.write_text("@echo off"+chr(13)+chr(10), encoding="ascii")
    assert check.shim_refusal([str(shim), "plain"]) is None
    assert "metacharacter" in check.shim_refusal([str(shim), "a|b"])
    assert check.shim_refusal([str(shim)]) is None


# ---- M10 residuals: session id shape, cwd executable resolution, receipt size cap ---------------------------------

@pytest.mark.parametrize("sid", ["../x", "a.b", "..", "a/b", "a b", "x" * 65, "s\u00e9"],
                         ids=["traversal", "dot", "dotdot", "slash", "space", "too-long", "non-ascii"])
def test_bad_session_is_a_usage_error_for_run_and_last(home, sid):
    r = _run(home, "run", "--session", sid, "--label", "s", "--", sys.executable, "-c", "1")
    assert r.returncode == 2 and "session" in r.stdout.lower()
    assert _receipts(home) == []  # nothing ran, nothing recorded
    r = _run(home, "last", "--session", sid)
    assert r.returncode == 2 and "session" in r.stdout.lower()


def test_session_of_64_safe_chars_is_accepted(home):
    sid = "A-z_9" * 12 + "abcd"
    assert len(sid) == 64
    r = _run(home, "run", "--session", sid, "--label", "s", "--", sys.executable, "-c", "1")
    assert r.returncode == 0 and _receipts(home)[0].name.startswith(sid + "-")
    assert check.last_failing("../" + sid) is None and check.last_failing("a.b") is None


def _env_without_curdir_switch():
    env = dict(os.environ)
    env.pop("NoDefaultCurrentDirectoryInExePath", None)
    return env


@pytest.mark.skipif(os.name != "nt", reason="Windows searches the current directory for a bare command name")
@pytest.mark.parametrize("cwd_on_path", [False, True], ids=["cwd-not-on-path", "cwd-on-path"])
def test_bare_name_that_resolves_only_in_cwd_is_refused(home, tmp_path, cwd_on_path):
    work = tmp_path / "work"
    work.mkdir()
    sentinel = tmp_path / "PLANTED.txt"
    (work / "zzcrplanted.cmd").write_text(f"@echo off\r\necho x> \"{sentinel}\"\r\n", encoding="ascii")
    env = _env_without_curdir_switch()
    env.update(HOME=str(home), USERPROFILE=str(home))
    env.pop("CLAUDE_SESSION_ID", None)
    if cwd_on_path:
        env["PATH"] = str(work) + os.pathsep + env.get("PATH", "")
    r = subprocess.run([sys.executable, "-I", str(CHECK), "run", "--label", "p", "--", "zzcrplanted"],
                       capture_output=True, text=True, encoding="utf-8", timeout=120, env=env, cwd=str(work))
    assert r.returncode == 2 and "refus" in r.stdout.lower() and "current directory" in r.stdout.lower()
    assert not sentinel.exists()
    assert _receipts(home) == []


@pytest.mark.skipif(os.name != "nt", reason="Windows PATHEXT resolution")
def test_bare_name_on_path_still_resolves_outside_cwd(home, tmp_path):
    work = tmp_path / "work"
    work.mkdir()
    r = _run(home, "run", "--label", "c", "--", "cmd", "/c", "exit", "0", cwd=work)
    assert r.returncode == 0 and "exit=0 pass" in r.stdout
    exe = check.resolve_exe("cmd")
    assert exe and os.path.isabs(exe) and os.path.dirname(exe).lower() != str(Path.cwd()).lower()


def _plant_receipt(home: Path, name: str, receipt: dict, pad: int = 0):
    d = home / ".claude" / "cache" / "cost-router" / "checks"
    d.mkdir(parents=True, exist_ok=True)
    if pad:
        receipt = dict(receipt, tail=["x" * 1000] * pad)
    (d / name).write_text(json.dumps(receipt), encoding="utf-8")
    return d / name


def test_receipt_over_256_kb_is_ignored_by_last_and_last_failing(home, monkeypatch):
    monkeypatch.setenv("HOME", str(home))
    monkeypatch.setenv("USERPROFILE", str(home))
    big = {"schema": 1, "label": "big", "session": "s7", "epoch": 2_000_000_000, "t": 2_000_000_000.0, "exit": 5}
    p = _plant_receipt(home, "s7-2000000000.json", big, pad=300)
    assert p.stat().st_size > 256 * 1024
    r = _run(home, "last", "--label", "big")
    assert r.returncode == 1 and "no receipt" in r.stdout
    assert check.last_failing("s7") is None
    small = _plant_receipt(home, "s7-2000000001.json", dict(big, label="small", t=2_000_000_001.0), pad=10)
    assert small.stat().st_size < 256 * 1024
    assert check.last_failing("s7")["label"] == "small"


def test_unresolvable_executable_is_refused_with_exit_2_and_no_receipt(home):
    r = _run(home, "run", "--label", "nf", "--", "zzcr-no-such-command-xyz", "--version")
    assert r.returncode == 2 and "not found" in r.stdout.lower()
    assert _receipts(home) == []


def test_child_environment_sets_no_default_cwd_exe_path_on_windows(monkeypatch):
    seen = {}

    class _P:
        returncode, stdout = 0, b"ok\n"

    def fake_run(argv, **kw):
        seen["env"] = kw.get("env")
        return _P()

    monkeypatch.setattr(check.subprocess, "run", fake_run)
    check.run([sys.executable, "-c", "pass"], "envt")
    if os.name == "nt":
        assert seen["env"] is not None and seen["env"].get("NoDefaultCurrentDirectoryInExePath") == "1"
