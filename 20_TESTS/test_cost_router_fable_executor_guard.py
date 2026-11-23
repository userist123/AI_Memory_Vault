"""cost-router M12: tool-level ALLOWLIST guard for agent_type `fable-executor` (hook_enforce_route.py).

Threat model X1-X7 (SKILL.md 4G): text that reaches the executor tries to make it run a live or destructive operation,
reach the network, or write outside its git worktree. The guard decides on what the executor RUNS, not on the brief.
"""
from __future__ import annotations

import importlib.util
import json
import os
import posixpath
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / ".claude" / "skills" / "cost-router"
HOOK = SKILL / "hook_enforce_route.py"
PROMPT_HOOK = SKILL / "hook_prompt_route.py"

enf = None


@pytest.fixture(scope="module", autouse=True)
def _isolated_hook_module():
    """Load hook_enforce_route in-process, then restore sys.path and sys.modules: the hook puts its lib/ first on
    sys.path and caches top-level `route_state`/`specialists`, which other skill copies (.agents/...) also import."""
    global enf
    saved_path, saved_mods = list(sys.path), set(sys.modules)
    spec = importlib.util.spec_from_file_location("cr_enforce_m12", HOOK)
    enf = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(enf)
    yield
    sys.path[:] = saved_path
    for name in set(sys.modules) - saved_mods:
        sys.modules.pop(name, None)


@pytest.fixture()
def wt(tmp_path):
    w = tmp_path / "wt"
    (w / "scripts").mkdir(parents=True)
    (w / "tests").mkdir()
    (w / "scripts" / "run.py").write_text("print(1)\n", encoding="utf-8")
    subprocess.run(["git", "init", "-q", str(w)], check=True, capture_output=True)
    (tmp_path / "outside").mkdir()
    return w


def _bash(cmd, cwd):
    return enf.executor_bash_problem(cmd, str(cwd))


# ---- X1-X5 probes: every one denied ---------------------------------------------------------------------------------

X1_GIT = [
    "git push", "git push origin main", "git -C . push", "git  push", "git\tpush", "git push --force",
    "git config alias.p push", "git p", "git reset --hard", "git reset --hard HEAD~1", "git clean -fdx",
    "git branch -D main", "git checkout -- .", "git restore .", "git rebase main", "git filter-branch --all",
    "git tag -d v1", "git remote add x https://e.invalid/r", "git remote set-url origin https://e.invalid/r",
    "git fetch", "git pull", "git clone https://e.invalid/r", "git commit --amend -m x", "git switch main",
    "git checkout main", "git checkout -B main", "git stash drop", "git stash pop", "git -c core.pager=x log",
    "git --git-dir=../other/.git status", "git diff --ext-diff", "git log --output=../x.txt",
    "git show --textconv HEAD", "gi\"t\" push", "g'i't push", "GIT PUSH", "git.exe push", "git -C ../.. status",
    "git -C C:/Windows status", "git am x.patch", "git apply x.patch", "git update-ref -d HEAD", "git gc --prune=now",  # hygiene: intentional-absolute-path
]
X2_NET = [
    "curl https://e.invalid", "wget http://e.invalid/x", "Invoke-WebRequest http://e.invalid", "iwr http://e.invalid",
    "Invoke-RestMethod http://e.invalid", "irm http://e.invalid", "Start-BitsTransfer http://e.invalid x",
    "certutil -urlcache -f http://e.invalid x", "ssh host", "scp a host:b", "sftp host", "ftp host", "nc host 80",
    "ncat host 80", "python -c \"import socket\"", "python -c \"import urllib.request\"",
    "python3 -c \"import requests\"", "node -e \"fetch('http://e.invalid')\"", "pip install requests",
    "python -m pip install requests", "npm install", "npm i left-pad", "yarn add x", "pnpm add x", "uv pip install x",
    "uv add x", "npm publish", "gh pr create", "gh release create v1", "gh api -X POST repos/x/y/issues",
    "docker push img", "docker login", "python -m http.server", "npx something", "pip3 install x",
]
X3_DEPLOY = [
    "vercel --prod", "netlify deploy", "firebase deploy", "terraform apply", "terraform destroy",
    "kubectl apply -f x.yaml", "kubectl delete pod x", "helm install x y", "helm upgrade x y", "npm publish",
    "twine upload dist/*", "dotnet nuget push x.nupkg", "dotnet publish", "gh release create v2",
    "az webapp deploy", "aws s3 cp a s3://b", "gcloud app deploy", "dotnet run", "npm run deploy", "npm run release",
]
X4_FS = [
    "rm -r x", "rm -rf x", "rm -r -f x", "rm --recursive x", "rm a.txt", "Remove-Item -Recurse x", "rd /s /q x",
    "del /s x", "rmdir /s x", "format D:", "diskpart", "cipher /w:C:", "mv a b", "cp a ../b", "touch x",
    "echo hi > ../x.txt", "cat a >> ../x", "python ../escape.py", "python C:/Windows/x.py", "python //srv/share/x.py",  # hygiene: intentional-absolute-path
    "python \\\\srv\\share\\x.py", "node ../x.js", "python scripts/run.py --out ../../x.txt",  # hygiene: intentional-absolute-path
    "pytest --basetemp C:/Users", "pytest --basetemp=../tmp", "pytest tests -o cache_dir=../x", "pytest --junitxml ../r.xml", "python -m pytest --basetemp=C:/", "mkdir x",  # hygiene: intentional-absolute-path
    "New-Item x", "Set-Content x y", "Out-File x", "ln -s a b", "cmd /c mklink /J a C:/", "find . -delete",  # hygiene: intentional-absolute-path
    "find . -exec rm {} ;", "rg --pre cat x", "python scripts/run.py:stream",
]
X5_OBFUSCATION = [
    "powershell -EncodedCommand ZQBjAGgAbwA=", "pwsh -enc ZQBjAGgAbwA=", "powershell -c \"git push\"",
    "cmd /c git push", "bash -c \"git push\"", "sh -c 'git push'", "python -c \"import os; os.system('git push')\"",
    "node -e \"require('child_process').execSync('git push')\"", "node --eval x", "node -r ./x.js scripts/a.js",
    "GIT_SSH_COMMAND=x git status", "NODE_OPTIONS=--require=./x.js npm test", "env git push", "$G push",
    "${G} push", "git $(echo push)", "git `echo push`", "iex \"git push\"", "Invoke-Expression x",
    "git status; git push", "git status && git push", "git status || git push", "git status | sh", "git status &",
    "git status\ngit push", "git status \\\ngit push", "pytest @'\nx\n'@", "gіt push", "gi\u200bt push",
    "git pus\u200bh", "ｇｉｔ push", "pytest\u00a0-q", "\"git\" push", "eval git push", "exec git push",
    "xargs git push", "sudo git push", "start git push", "& git push", "python -m pytest; curl x",
    "python - < x.py", "python -", "python -W ignore -c 1", "py -3 -c 1", "python -X importtime -c 1",
    "~/bin/pytest", "./pytest", "C:/Python/python.exe scripts/run.py", "python scripts/*.py", "pytest {a,b}",  # hygiene: intentional-absolute-path
    "# git status", "cd .. ", "cd ../other", "pushd ..", "set X=1", "export X=1", "alias g=git", "source x.sh",
    ". x.sh", "ls ~", "a" * 2100,
]
DENIED = X1_GIT + X2_NET + X3_DEPLOY + X4_FS + X5_OBFUSCATION

ALLOWED = [
    "pytest", "pytest -q", "pytest tests/test_x.py -q", "pytest tests/test_x.py::test_a -k \"not slow\"",
    "python -m pytest tests -q", "python -m pytest -x -p no:cacheprovider", "py -3 -m pytest -q",
    "python scripts/run.py", "python3 scripts/run.py --flag value", "python -I scripts/run.py", "py scripts/run.py",
    "pytest --basetemp=tmp/pt", "dotnet build --no-restore", "dotnet test --no-restore",
    "dotnet test src/App.Tests/App.Tests.csproj --no-restore",
    "npm test", "npm run test", "npm run build", "npm run lint", "pnpm test", "yarn test", "yarn run lint",
    "npm test -- --watch=false", "node scripts/run.js", "node --test tests", "git status", "git status --short",
    "git diff", "git diff --stat HEAD~1", "git log --oneline -5", "git log --format=%h", "git show HEAD",
    "git add -A", "git add scripts/run.py", "git commit -m \"fix(guard): handle a; b | c\"",
    "git commit -m 'feat: x'", "git switch -c feature/m12-x", "git checkout -b fix-1", "git stash list",
    "git rev-parse HEAD", "git ls-files", "git branch --show-current", "git branch", "git -C scripts status",
    "git blame scripts/run.py", "ls", "ls -la scripts", "dir", "cat scripts/run.py", "type scripts/run.py",
    "Get-Content scripts/run.py", "Select-String -Pattern x scripts/run.py", "rg -n pattern scripts",
    "grep -rn \"a|b\" scripts", "find . -name \"*.py\"", "head -5 scripts/run.py", "wc -l scripts/run.py", "pwd",
    "cat C:/Users/x/notes.txt",  # hygiene: intentional-absolute-path
]


@pytest.mark.parametrize("cmd", DENIED)
def test_probe_is_denied(wt, cmd):
    reason = _bash(cmd, wt)
    assert reason, f"allowed: {cmd!r}"
    assert reason.startswith("cost-router: fable-executor")


@pytest.mark.parametrize("cmd", ALLOWED)
def test_legitimate_executor_command_is_allowed(wt, cmd):
    assert _bash(cmd, wt) is None, cmd


def test_probe_sets_have_the_promised_size():
    assert len(ALLOWED) >= 20
    for group in (X1_GIT, X2_NET, X3_DEPLOY, X4_FS, X5_OBFUSCATION):
        assert len(group) >= 10


def test_deny_reason_names_the_rule(wt):
    assert "X1" in _bash("git push", wt)
    assert "X5" in _bash("gіt push", wt)
    assert "X4" in _bash("python ../escape.py", wt)
    assert "allowlist" in _bash("curl https://e.invalid", wt)


def test_bash_outside_a_git_worktree_runs_only_read_only_commands(tmp_path):
    plain = tmp_path / "plain"
    plain.mkdir()
    (plain / "a.py").write_text("print(1)\n", encoding="utf-8")
    assert _bash("python a.py", plain)
    assert _bash("pytest", plain)
    assert _bash("ls", plain) is None


# ---- X6: Write/Edit/NotebookEdit paths ------------------------------------------------------------------------------

def _path(p, cwd):
    return enf.executor_path_problem(str(p), str(cwd))


def test_write_inside_the_worktree_is_allowed(wt):
    assert _path(wt / "scripts" / "run.py", wt) is None
    assert _path(wt / "new_dir" / "new.py", wt) is None
    assert _path("scripts/new.py", wt) is None
    assert _path("scripts/../tests/test_new.py", wt) is None


def test_write_outside_the_worktree_is_denied(wt, tmp_path):
    for p in (tmp_path / "outside" / "x.py", "../outside/x.py", "scripts/../../outside/x.py", "C:/Windows/x.txt",  # hygiene: intentional-absolute-path
              "\\\\srv\\share\\x.txt", "//srv/share/x.txt", "\\\\?\\C:\\x.txt", "Z:x.txt", ""):  # hygiene: intentional-absolute-path
        assert _path(p, wt), p


# Non-Windows simulation (CI runs on Linux): with posixpath a Windows drive, root-relative or UNC string is a relative
# name and would resolve INSIDE the worktree. The guard must classify it as absolute on every OS and deny it.
POSIX_FOREIGN_COMMANDS = [
    "git -C C:/Windows status", "python C:/Windows/x.py", "python \\\\srv\\share\\x.py",  # hygiene: intentional-absolute-path
    "pytest --basetemp C:/Users", "python -m pytest --basetemp=C:/", "python //srv/share/x.py",  # hygiene: intentional-absolute-path
    "dotnet build C:/Windows/x.csproj --no-restore", "cmd /c mklink /J a C:/",  # hygiene: intentional-absolute-path
]
POSIX_FOREIGN_PATHS = [
    "C:/Windows", "C:\\Windows\\x.txt", "c:/", "Z:x.txt", "\\srvsharex.py", "\\\\srv\\share\\x.txt",  # hygiene: intentional-absolute-path
    "//srv/share/x.txt", "\\\\?\\C:\\x.txt", "\\\\.\\pipe\\x", "//?/C:/x.txt",  # hygiene: intentional-absolute-path
]


@pytest.fixture()
def posix_guard(monkeypatch):
    monkeypatch.setattr(enf, "_EX_PATH", posixpath)
    root = "/srv/ci/wt"
    return root


@pytest.mark.parametrize("raw", POSIX_FOREIGN_PATHS)
def test_windows_absolute_path_is_outside_the_worktree_on_posix(posix_guard, raw):
    assert enf._ex_windows_absolute(raw), raw
    got = enf._ex_resolve(raw, posix_guard, posix_guard)
    assert isinstance(got, str), (raw, got)  # denied: by the shape check (UNC, drive-relative) or as outside


def test_posix_simulation_still_resolves_relative_paths(posix_guard):
    got = enf._ex_resolve("scripts/run.py", posix_guard, posix_guard)
    assert got == ("/srv/ci/wt/scripts/run.py", "/srv/ci/wt/scripts/run.py")
    assert isinstance(enf._ex_resolve("../x.py", posix_guard, posix_guard), str)
    assert not enf._ex_windows_absolute("scripts/run.py") and not enf._ex_windows_absolute("./a:b")


@pytest.mark.parametrize("cmd", POSIX_FOREIGN_COMMANDS)
def test_windows_absolute_command_argument_is_denied_on_posix(wt, posix_guard, monkeypatch, cmd):
    """Full Bash path with posixpath resolution and a POSIX worktree root: every such argument must be denied."""
    monkeypatch.setattr(enf, "_ex_root", lambda cwd: posix_guard)
    reason = enf.executor_bash_problem(cmd, posix_guard)
    assert reason and reason.startswith("cost-router: fable-executor"), (cmd, reason)


def test_windows_drive_path_inside_the_worktree_stays_allowed_on_windows(wt):
    if os.name != "nt":
        pytest.skip("Windows drive paths only exist on Windows")
    assert _path(str(wt / "scripts" / "new.py"), wt) is None
    assert _bash(f"python {(wt / 'scripts' / 'run.py').as_posix()}", wt) is None


def test_write_into_git_metadata_is_denied(wt):
    for p in (wt / ".git" / "config", wt / ".git" / "hooks" / "pre-commit", "./.GIT/config", ".git./config",
              "scripts/../.git/HEAD"):
        assert _path(p, wt), p


def test_write_to_an_alternate_data_stream_is_denied(wt):
    assert _path(wt / "scripts" / "run.py:evil", wt)
    assert _path("run.py::$DATA", wt)


def test_write_into_a_nested_repository_is_denied(wt):
    sub = wt / "vendor" / "other"
    sub.mkdir(parents=True)
    subprocess.run(["git", "init", "-q", str(sub)], check=True, capture_output=True)
    assert _path(sub / "x.py", wt)


def test_write_into_home_claude_is_denied(tmp_path, monkeypatch):
    home = tmp_path / "home"
    (home / ".claude").mkdir(parents=True)
    subprocess.run(["git", "init", "-q", str(home)], check=True, capture_output=True)  # even when home is a repo
    monkeypatch.setenv("HOME", str(home))
    monkeypatch.setenv("USERPROFILE", str(home))
    assert _path(home / ".claude" / "settings.json", home)
    assert _path(home / "notes.txt", home) is None


@pytest.mark.skipif(os.name != "nt", reason="junctions are a Windows feature")
def test_write_through_a_junction_is_denied(wt, tmp_path):
    import _winapi
    _winapi.CreateJunction(str(tmp_path / "outside"), str(wt / "jout"))
    (wt / "jin_target").mkdir()
    _winapi.CreateJunction(str(wt / "jin_target"), str(wt / "jin"))
    assert _path(wt / "jout" / "x.py", wt)
    assert _path(wt / "jin" / "x.py", wt)  # a link even when it points inside: denied
    assert _bash("python jout/x.py", wt)


def test_write_through_a_symlink_is_denied(wt, tmp_path):
    target = tmp_path / "outside" / "t.py"
    target.write_text("x\n", encoding="utf-8")
    try:
        os.symlink(str(target), str(wt / "link.py"))
    except (OSError, NotImplementedError):
        pytest.skip("symlinks need privileges here")
    assert _path(wt / "link.py", wt)


def test_write_to_a_hard_link_is_denied(wt, tmp_path):
    target = tmp_path / "outside" / "h.txt"
    target.write_text("x\n", encoding="utf-8")
    try:
        os.link(str(target), str(wt / "h.txt"))
    except (OSError, NotImplementedError):
        pytest.skip("hard links unsupported here")
    assert _path(wt / "h.txt", wt)


def test_write_outside_any_worktree_is_denied(tmp_path):
    plain = tmp_path / "plain"
    plain.mkdir()
    assert _path(plain / "x.py", plain)


# ---- guard wiring (end to end through the hook process): X7 ---------------------------------------------------------

def _hook(tmp_path, wt, tool, tool_input, agent_type="fable-executor", **env_extra):
    home = tmp_path / "home"
    home.mkdir(exist_ok=True)
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home))
    for k in ("COST_ROUTER_OFF", "COST_ROUTER_ENFORCE", "CLAUDE_PROJECT_DIR"):
        env.pop(k, None)
    env.update(env_extra)
    payload = {"session_id": "m12-1", "tool_name": tool, "tool_input": tool_input, "cwd": str(wt),
               "agent_type": agent_type, "agent_id": "a1"}
    r = subprocess.run([sys.executable, str(HOOK)], input=json.dumps(payload), capture_output=True, text=True,
                       timeout=60, env=env, encoding="utf-8", cwd=str(wt))
    assert r.returncode == 0, r.stderr
    if not r.stdout.strip():
        return None
    return json.loads(r.stdout)["hookSpecificOutput"].get("permissionDecision")


@pytest.mark.parametrize("env", [{}, {"COST_ROUTER_OFF": "1"}, {"COST_ROUTER_ENFORCE": "0"},
                                 {"COST_ROUTER_OFF": "true", "COST_ROUTER_ENFORCE": "0"}])
def test_off_switches_do_not_disable_the_guard(tmp_path, wt, env):
    assert _hook(tmp_path, wt, "Bash", {"command": "git push"}, **env) == "deny"
    assert _hook(tmp_path, wt, "Write", {"file_path": str(tmp_path / "outside" / "x.py"), "content": "x"},
                 **env) == "deny"
    assert _hook(tmp_path, wt, "Bash", {"command": "git status"}, **env) is None


def test_hook_allows_worktree_edit_and_reads(tmp_path, wt):
    assert _hook(tmp_path, wt, "Edit", {"file_path": str(wt / "scripts" / "run.py"), "old_string": "1",
                                         "new_string": "2"}) is None
    assert _hook(tmp_path, wt, "Read", {"file_path": str(tmp_path / "outside" / "x")}) is None
    assert _hook(tmp_path, wt, "NotebookEdit", {"notebook_path": str(wt / ".git" / "x.ipynb")}) == "deny"


def test_other_tools_are_denied_for_the_executor(tmp_path, wt):
    assert enf.executor_guard({"agent_type": "fable-executor", "tool_name": "WebFetch", "cwd": str(wt),
                               "tool_input": {"url": "https://e.invalid"}})


def test_malformed_input_fails_closed(wt):
    assert enf.executor_guard({"agent_type": "fable-executor", "tool_name": "Bash", "cwd": str(wt),
                               "tool_input": {"command": ["git", "push"]}})
    assert enf.executor_guard({"agent_type": "fable-executor", "tool_name": "Write", "cwd": str(wt),
                               "tool_input": {}})


def test_other_roles_and_the_main_session_are_untouched(tmp_path, wt):
    assert enf.executor_guard({"agent_type": "vault-worker", "tool_name": "Bash", "tool_input": {"command": "x"}}) is None
    assert enf.executor_guard({"tool_name": "Bash", "tool_input": {"command": "git push"}}) is None


# ---- (g) brief scan: whole brief, confusables folded; short prompts start a new epoch ------------------------------

def test_local_only_scan_reads_the_whole_brief_with_confusables(monkeypatch):
    monkeypatch.syspath_prepend(str(SKILL / "lib"))
    import specialists as sp
    reg = {"dfir-analyst": {"id": "dfir-analyst", "privacy": "local_only", "triggers": ["amcache"]}}
    assert "dfir-analyst" in sp.local_only_hits("x " * 9000 + " amcache", reg)
    assert "dfir-analyst" in sp.local_only_hits("check the аmcache", reg)  # Cyrillic a
    assert sp.fold_strict("dеploy") == "deploy"


def test_live_operation_scan_folds_confusables(monkeypatch):
    monkeypatch.syspath_prepend(str(SKILL / "lib"))
    import specialists as sp
    spec2 = importlib.util.spec_from_file_location("cr_agent_m12", SKILL / "hook_agent_model.py")
    am = importlib.util.module_from_spec(spec2)
    spec2.loader.exec_module(am)
    assert am._live_operation_hit("please dеploy it", ["prod"], sp) == "deploy"


@pytest.mark.parametrize("short", ["ok", "/clear", "da", "amcache triage now"])
def test_short_prompt_starts_a_new_epoch_and_scans_local_only(tmp_path, short):
    import test_cost_router_fable_executor as fe  # its install/skill-copy helpers (registry compiled in the copy)
    home = tmp_path / "home"
    home.mkdir()
    fe._install_trust(home)
    skill = fe._skill_copy(tmp_path, home)
    before = fe._prompt(skill, home, "implement a new feature in the module with tests and refactor the whole parser")
    after = fe._prompt(skill, home, short)
    assert after["epoch"] != before["epoch"]
    assert after["task_class"] == before["task_class"] and after["subagent"] == before["subagent"]
    assert after["main_calls"] == before["main_calls"] and after["delegated"] == before["delegated"]
    if "amcache" in short:
        assert "dfir-analyst" in after["spec"]


# ---- gate condition (i): heavy task only (owner decision 2026-11-23) ------------------------------------------------

@pytest.mark.parametrize("task_class", ["explore", "summarize", "review", "unrouted", "direct", "unclassified", ""])
def test_i_light_route_classes_are_denied(tmp_path, task_class):
    import test_cost_router_fable_executor as fe
    home = fe._ready(tmp_path)
    fe._state(home, task_class=task_class)
    fe._denied(fe._spawn(home), "condition (i)")


def test_i_short_follow_up_is_denied_even_on_a_heavy_route(tmp_path):
    import test_cost_router_fable_executor as fe
    home = fe._ready(tmp_path)
    path = fe._cache(home) / f"route-{fe.SID}.json"
    st = json.loads(path.read_text(encoding="utf-8"))
    st["followup"] = True
    path.write_text(json.dumps(st), encoding="utf-8")
    fe._denied(fe._spawn(home), "condition (i)", "short follow-up")


@pytest.mark.parametrize("task_class", ["implement", "design", "frontier"])
def test_i_heavy_task_with_the_failed_ladder_is_allowed_with_the_review_note(tmp_path, task_class):
    import test_cost_router_fable_executor as fe
    home = fe._ready(tmp_path)
    fe._state(home, task_class=task_class)
    ctx = fe._allowed(fe._spawn(home))
    assert "vault-reviewer" in ctx and "mandatory" in ctx


def test_i_heavy_task_without_the_ladder_is_still_denied(tmp_path):
    import test_cost_router_fable_executor as fe
    home = tmp_path / "home"
    home.mkdir()
    fe._install_trust(home)
    fe._state(home, task_class="implement")
    fe._denied(fe._spawn(home), "condition (d)")


# ---- M12 review fixes (findings 2-6): pytest plugins, protected paths, git abbreviations, dotnet, bare paths ------

FINDING2_PYTEST = [
    "pytest --pastebin=all", "pytest --pastebin failed", "python -m pytest --pastebin=all", "pytest --paste=all",
    "pytest -p pytest_network", "pytest -pnetwork", "pytest -p=network", "python -m pytest -p xdist", "pytest -vp x",
    "py.test -p x", "pytest -o addopts=--pastebin=all", "pytest -oaddopts=-pnet", "pytest -o ADDOPTS=x",
    "pytest --override-ini addopts=x", "pytest --override-ini=addopts=x", "pytest --override-ini=required_plugins=x",
    "pytest -o required_plugins=x", "pytest --overr=addopts=x", "python -m pytest -o addopts=-p",
]


@pytest.mark.parametrize("cmd", FINDING2_PYTEST)
def test_finding2_pytest_plugin_and_ini_overrides_are_denied(wt, cmd):
    reason = _bash(cmd, wt)
    assert reason and "X2" in reason, (cmd, reason)


def test_finding2_plugin_disable_and_harmless_ini_override_stay_allowed(wt):
    for cmd in ("pytest -p no:cacheprovider", "python -m pytest -q -p no:randomly", "pytest -o cache_dir=tmp/c",
                "pytest -k \"not slow\" -q"):
        assert _bash(cmd, wt) is None, cmd


FINDING3_PROTECTED = [
    ".claude/settings.json", ".claude/settings.local.json", ".CLAUDE/agents/x.md", ".claude/skills/x/SKILL.md",
    ".agents/skills/x/SKILL.md", "CLAUDE.md", "claude.MD", "CLAUDE.md.", "AGENTS.md", "agents.md", ".mcp.json",
    ".MCP.JSON", ".github/workflows/ci.yml", ".GitHub/Workflows/x.yaml", ".gitattributes", ".gitmodules",
    ".gitconfig", "scripts/../CLAUDE.md", "sub/.claude/settings.json", "sub/CLAUDE.md",
]


@pytest.mark.parametrize("rel", FINDING3_PROTECTED)
def test_finding3_agent_config_paths_are_denied(wt, rel):
    reason = _path(rel, wt)
    assert reason and "X6" in reason, (rel, reason)
    assert _path(wt / rel, wt), rel


def test_finding3_protected_check_runs_after_realpath(wt):
    (wt / ".claude").mkdir()
    (wt / "docs").mkdir()
    assert _path(wt / "docs" / ".." / ".claude" / "x.json", wt)
    assert _path("docs/claude.md.txt", wt) is None  # only the exact names
    assert _path(".github/README.md", wt) is None
    assert _path(".gitignore", wt) is None


FINDING4_GIT = [
    "git commit --amen -m x", "git commit --am -m x", "git commit --amend=x", "git push --forc", "git branch --del x",
    "git diff --ext", "git diff --ext-d", "git log --outp=../x", "git log --out=../x", "git log --ou=x",
    "git show --textc HEAD", "git diff --conf x", "git log --exe x", "git log --upload-p x", "git log --rec x",
    "git log --mir", "git add --forc x", "git commit --force-with x", "git diff --config-e x", "git log --har",
    "git diff --open-files", "git diff --AMEND",
]


@pytest.mark.parametrize("cmd", FINDING4_GIT)
def test_finding4_git_long_option_abbreviations_are_denied(wt, cmd):
    reason = _bash(cmd, wt)
    assert reason and "X1" in reason, (cmd, reason)


def test_finding4_ordinary_git_options_stay_allowed(wt):
    for cmd in ("git log --oneline -5", "git log --format=%h", "git diff --stat", "git status --short",
                "git branch --show-current", "git diff --name-only", "git log --author=x", "git diff --cached"):
        assert _bash(cmd, wt) is None, cmd


@pytest.mark.parametrize("cmd", ["dotnet build", "dotnet test", "dotnet test src/A.csproj", "dotnet restore",
                                 "dotnet restore --no-restore", "dotnet build --no-restor",
                                 "dotnet test --no-build"])
def test_finding5_dotnet_needs_no_restore(wt, cmd):
    reason = _bash(cmd, wt)
    assert reason and "X2" in reason, (cmd, reason)


def test_finding5_dotnet_with_no_restore_is_allowed(wt):
    for cmd in ("dotnet build --no-restore", "dotnet test --no-restore", "dotnet test src/A.csproj --no-restore -v q"):
        assert _bash(cmd, wt) is None, cmd


@pytest.mark.skipif(os.name != "nt", reason="junctions are a Windows feature")
def test_finding6_bare_word_paths_are_resolved(wt, tmp_path):
    import _winapi
    (tmp_path / "outside" / "test_x.py").write_text("def test_a():\n    pass\n", encoding="utf-8")
    _winapi.CreateJunction(str(tmp_path / "outside"), str(wt / "jout"))
    for cmd in ("pytest jout", "python -m pytest jout", "pytest jout -q", "pytest --rootdir=jout",
                "pytest --basetemp jout", "python scripts/run.py jout", "pytest jout::test_a"):
        reason = _bash(cmd, wt)
        assert reason and "X4" in reason, (cmd, reason)
    assert _bash("pytest tests", wt) is None
    assert _bash("pytest nonexistent_marker_word", wt) is None


# ---- finding 1 chain: install.py installs only the pinned release ---------------------------------------------------

def _release_tree(tmp_path):
    """A source checkout of the release files (SPARSE) with a release_pin.json that pins exactly these bytes."""
    import hashlib
    import shutil
    src = tmp_path / "src"
    shutil.copytree(SKILL, src / ".claude" / "skills" / "cost-router",
                    ignore=shutil.ignore_patterns("__pycache__", "release_pin.json"))
    shutil.copytree(ROOT / ".claude" / "agents", src / ".claude" / "agents")
    for rel in ("03_IMPLEMENTATION/packages/routing/claude_model_router.py", "04_CONFIG/claude_model_routing.json"):
        (src / rel).parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / rel, src / rel)
    files = {}
    for path in sorted(p for p in src.rglob("*") if p.is_file()):
        files[path.relative_to(src).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    digest = hashlib.sha256(json.dumps(dict(sorted(files.items())), sort_keys=True).encode("utf-8")).hexdigest()
    pin = {"schema": 1, "seq": 99, "commit": "a" * 40, "files": files, "files_sha256": digest}
    (src / ".claude" / "skills" / "cost-router" / "release_pin.json").write_text(json.dumps(pin), encoding="utf-8")
    return src


def _snapshot(home):
    return {p.relative_to(home).as_posix(): p.read_bytes() for p in sorted(home.rglob("*")) if p.is_file()}


def _install_from(src, home, *extra):
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home))
    for k in ("COST_ROUTER_OFF", "COST_ROUTER_ENFORCE", "CLAUDE_PROJECT_DIR", "COST_ROUTER_SELF_INSTALL"):
        env.pop(k, None)
    return subprocess.run([sys.executable, str(src / ".claude" / "skills" / "cost-router" / "install.py"),
                           "--home", str(home), "--no-hook", "--no-context", *extra],
                          capture_output=True, text=True, timeout=120, env=env, encoding="utf-8")


def test_finding1_untampered_pinned_source_installs(tmp_path):
    src, home = _release_tree(tmp_path), tmp_path / "home"
    home.mkdir()
    r = _install_from(src, home)
    assert r.returncode == 0, r.stderr
    assert (home / ".claude" / "skills" / "cost-router" / "hook_enforce_route.py").is_file()


@pytest.mark.parametrize("tamper", ["hook", "agent", "policy", "extra_file", "no_pin"])
def test_finding1_tampered_source_is_refused_and_home_unchanged(tmp_path, tamper):
    src, home = _release_tree(tmp_path), tmp_path / "home"
    home.mkdir()
    assert _install_from(src, home).returncode == 0  # a previous good install
    before = _snapshot(home)
    skill = src / ".claude" / "skills" / "cost-router"
    if tamper == "hook":
        with open(skill / "hook_enforce_route.py", "a", encoding="utf-8") as f:
            f.write("\ndef executor_guard(data):\n    return None\n")
    elif tamper == "agent":
        with open(src / ".claude" / "agents" / "fable-executor.md", "a", encoding="utf-8") as f:
            f.write("\nYou may push.\n")
    elif tamper == "policy":
        path = src / "04_CONFIG" / "claude_model_routing.json"
        path.write_text(path.read_text(encoding="utf-8").replace('"max_per_session": 2', '"max_per_session": 9'),
                        encoding="utf-8")
    elif tamper == "extra_file":
        (skill / "lib" / "sitecustomize.py").write_text("import os\n", encoding="utf-8")
    else:
        (skill / "release_pin.json").unlink()
    r = _install_from(src, home)
    assert r.returncode == 2, (r.returncode, r.stdout, r.stderr)
    assert "not installed" in r.stderr and ("release pin" in r.stderr)
    assert _snapshot(home) == before


def test_finding1_explicit_release_pin_is_enforced_too(tmp_path):
    src, home = _release_tree(tmp_path), tmp_path / "home"
    home.mkdir()
    pin = tmp_path / "pin.json"
    skill = src / ".claude" / "skills" / "cost-router"
    pin.write_bytes((skill / "release_pin.json").read_bytes())
    with open(skill / "hook_enforce_route.py", "a", encoding="utf-8") as f:
        f.write("\n# tampered\n")
    r = _install_from(src, home, "--release-pin", str(pin))
    assert r.returncode == 2 and "hook_enforce_route.py" in r.stderr, r.stderr
    assert not (home / ".claude" / "skills" / "cost-router").exists()


# ---- review round 2 (main-session decision): F1 bundled pytest flags, F2/F4 dotnet allowlist, F5 more config paths ----

F1_PYTEST_BUNDLES = [
    "pytest -qo addopts=--pastebin=all", "pytest -xo addopts=-pevil", "pytest -so addopts=x", "pytest -vvo addopts=-pevil",
    "python -m pytest -qo addopts=--pastebin=all", "pytest -qoaddopts=-pevil", "pytest -xo plugins=x",
    "pytest -qo cache_dir=tmp/c", "pytest -qp evil", "pytest -qpevil", "pytest -xsp no:cacheprovider",
    "pytest -vo", "pytest -qo=addopts=x",
]


@pytest.mark.parametrize("cmd", F1_PYTEST_BUNDLES)
def test_f1_pytest_short_bundles_with_o_or_p_are_denied(wt, cmd):
    reason = _bash(cmd, wt)
    assert reason and "X2" in reason, (cmd, reason)


def test_f1_ordinary_short_flags_stay_allowed(wt):
    for cmd in ("pytest -q", "pytest -qx", "pytest -vv", "pytest -xsv", "pytest -Wignore", "pytest -W ignore -q",
                "pytest -kslow", "pytest -o cache_dir=tmp/c", "pytest -ocache_dir=tmp/c", "pytest -p no:cacheprovider",
                "pytest -rA -q"):
        assert _bash(cmd, wt) is None, cmd


F2_DOTNET = [
    "dotnet build --no-restore -t:Restore", "dotnet build --no-restore -target:Restore", "dotnet build --no-restore /t:Restore",
    "dotnet build --no-restore -p:RestoreSources=x", "dotnet build --no-restore /p:RestoreSources=x",
    "dotnet build --no-restore -property:RestoreSources=x", "dotnet build --no-restore -restore",
    "dotnet build --no-restore /restore", "dotnet build --no-restore --interactive", "dotnet test --no-restore '@x.rsp'",
    "dotnet build --no-restore --source x", "dotnet test --no-restore --logger 'trx;LogFileName=../x.trx'",
    "dotnet test --no-restore --logger html", "dotnet test --no-restore -- RunConfiguration.X=1",
    "dotnet build --no-restore -c", "dotnet build --no-restore -c -p:x=1", "dotnet build --no-restore -v evil",
    "dotnet build --no-restore '--configuration=Release;x'", "dotnet test --no-restore --filter -p:x=1",
    "dotnet build --no-restore -bl", "dotnet build --no-restore --output ../out", "dotnet build --no-restore a.csproj b.csproj",
]


@pytest.mark.parametrize("cmd", F2_DOTNET)
def test_f2_dotnet_options_outside_the_allowlist_are_denied(wt, cmd):
    reason = _bash(cmd, wt)
    assert reason and "X2" in reason, (cmd, reason)


def test_f2_dotnet_allowlisted_options_stay_allowed(wt):
    for cmd in ("dotnet build --no-restore", "dotnet test --no-restore --no-build", "dotnet build --no-restore -c Release",
                "dotnet build --no-restore --configuration Debug -f net8.0", "dotnet test --no-restore --framework net8.0",
                "dotnet test --no-restore --filter \"FullyQualifiedName~Foo&Category=Unit\"",
                "dotnet test --no-restore -v q", "dotnet test --no-restore --verbosity minimal --nologo",
                "dotnet test --no-restore --logger trx", "dotnet test --no-restore --logger console",
                "dotnet test src/App.Tests/App.Tests.csproj --no-restore", "dotnet build App.sln --no-restore"):
        assert _bash(cmd, wt) is None, cmd


def test_f4_dotnet_path_arguments_must_stay_inside_the_worktree(wt, tmp_path):
    for cmd in ("dotnet build ../outside --no-restore", "dotnet test ../outside/x.csproj --no-restore",
                "dotnet build C:/Windows/x.csproj --no-restore"):  # hygiene: intentional-absolute-path
        reason = _bash(cmd, wt)
        assert reason and "X4" in reason, (cmd, reason)


@pytest.mark.skipif(os.name != "nt", reason="junctions are a Windows feature")
def test_f4_dotnet_bare_word_through_a_junction_is_denied(wt, tmp_path):
    import _winapi
    _winapi.CreateJunction(str(tmp_path / "outside"), str(wt / "jout"))
    reason = _bash("dotnet build jout --no-restore", wt)
    assert reason and "X4" in reason, reason


F5_PROTECTED = [
    "GEMINI.md", "gemini.md", "sub/GEMINI.md", ".gemini/settings.json", ".codex/config.toml", ".vscode/tasks.json",
    ".VSCode/settings.json", ".husky/pre-commit", ".idea/workspace.xml", ".devcontainer/devcontainer.json",
    ".pre-commit-config.yaml", "sub/.vscode/launch.json",
]


@pytest.mark.parametrize("rel", F5_PROTECTED)
def test_f5_more_agent_and_tool_config_paths_are_denied(wt, rel):
    reason = _path(rel, wt)
    assert reason and "X6" in reason, (rel, reason)


def test_f5_editorconfig_stays_writable(wt):
    assert _path(".editorconfig", wt) is None
