"""cost-router context economy: the repository map, its SessionStart hook and the global rules/deny install."""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / ".claude" / "skills" / "cost-router"
MAP = SKILL / "lib" / "repo_map.py"
HOOK = SKILL / "hook_repo_map.py"
INSTALL = SKILL / "install.py"


def _run(args, home: Path, stdin: str = "{}", cwd=None, env_extra=None):
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home))
    env.pop("CLAUDE_PROJECT_DIR", None)
    env.update(env_extra or {})
    return subprocess.run([sys.executable, *map(str, args)], input=stdin, capture_output=True, text=True,
                          timeout=60, env=env, cwd=cwd, encoding="utf-8")


def _git(repo: Path, *args):
    subprocess.run(["git", "-C", str(repo), "-c", "user.email=t@t", "-c", "user.name=t", *args],
                   check=True, capture_output=True)


def _small_repo(tmp_path: Path) -> Path:
    repo = tmp_path / "proj"
    (repo / "src" / "app").mkdir(parents=True)
    (repo / "big").mkdir()
    (repo / "README.md").write_text("# proj\n", encoding="utf-8")
    (repo / "pyproject.toml").write_text("[project]\nname='p'\n", encoding="utf-8")
    (repo / "src" / "README.md").write_text("---\nid: x\n---\n# Application sources\n", encoding="utf-8")
    (repo / "src" / "app" / "main.py").write_text("print(1)\n", encoding="utf-8")
    for i in range(310):
        (repo / "big" / f"f{i}.json").write_text("{}", encoding="utf-8")
    _git(repo, "init", "-q")
    _git(repo, "add", "-A")
    _git(repo, "commit", "-qm", "init")
    return repo


def test_map_of_this_repository_is_small_and_flags_heavy_directories(tmp_path):
    r = _run([MAP, ROOT, "--no-cache"], tmp_path)
    assert r.returncode == 0, r.stderr
    text = r.stdout
    assert len(text) <= 4001, len(text)  # ~1k tokens instead of exploring 17k files
    assert text.startswith("# Repo map: ") and "tracked files" in text
    assert "⚠ .agents/" in text and "Tests: pytest" in text
    assert "never list or read a ⚠ directory wholesale" in text


def test_map_describes_directories_and_is_cached_by_head(tmp_path):
    repo = _small_repo(tmp_path)
    home = tmp_path / "home"
    text = _run([MAP, repo], home).stdout
    assert "⚠ big/ 310 files" in text and "(.json 310)" in text
    assert "- src/ 2 files" in text and "Application sources" in text, "README frontmatter is skipped"
    assert "Root files: README.md, pyproject.toml" in text
    cached = list((home / ".claude" / "cache" / "repo-map").glob("*.md"))
    assert len(cached) == 1 and cached[0].read_text(encoding="utf-8") == text.rstrip("\n")
    (repo / "src" / "new.py").write_text("", encoding="utf-8")
    _git(repo, "add", "-A")
    _git(repo, "commit", "-qm", "two")
    text2 = _run([MAP, repo], home).stdout
    assert "- src/ 3 files" in text2, "a new commit rebuilds the map"
    assert len(list((home / ".claude" / "cache" / "repo-map").glob("*.md"))) == 1, "stale entries are dropped"


def test_map_is_silent_outside_a_git_repository(tmp_path):
    plain = tmp_path / "plain"
    plain.mkdir()
    r = _run([MAP, plain], tmp_path)
    assert r.returncode == 0 and r.stdout == ""
    r = _run([HOOK], tmp_path, env_extra={"CLAUDE_PROJECT_DIR": str(plain)})
    assert r.returncode == 0 and r.stdout == ""


def test_hook_injects_the_map_as_session_context(tmp_path):
    repo = _small_repo(tmp_path)
    r = _run([HOOK], tmp_path / "home", env_extra={"CLAUDE_PROJECT_DIR": str(repo)})
    assert r.returncode == 0, r.stderr
    out = json.loads(r.stdout)["hookSpecificOutput"]
    assert out["hookEventName"] == "SessionStart" and "# Repo map: proj" in out["additionalContext"]
    # cwd from the hook input works too, and garbage input never blocks
    r = _run([HOOK], tmp_path / "home", stdin=json.dumps({"cwd": str(repo)}))
    assert "# Repo map: proj" in r.stdout
    assert _run([HOOK], tmp_path / "home", stdin="not json", cwd=repo).returncode == 0


def test_project_hook_is_silent_when_the_user_install_is_registered(tmp_path):
    repo = _small_repo(tmp_path)
    home = tmp_path / "home"
    assert _run([INSTALL, "--home", home], home).returncode == 0
    r = _run([HOOK], home, env_extra={"CLAUDE_PROJECT_DIR": str(repo)})
    assert r.returncode == 0 and r.stdout == "", "one map per session, from the user copy"
    user_hook = home / ".claude" / "skills" / "cost-router" / "hook_repo_map.py"
    r = _run([user_hook], home, env_extra={"CLAUDE_PROJECT_DIR": str(repo)})
    assert "# Repo map: proj" in r.stdout, "the installed copy works without this repository"


def test_install_adds_rules_and_deny_once_and_uninstall_removes_only_them(tmp_path):
    home = tmp_path / "home"
    claude = home / ".claude"
    claude.mkdir(parents=True)
    (claude / "CLAUDE.md").write_text("# My own rules\nkeep me\n", encoding="utf-8")
    own = ["Read(./secrets/**)", "Read(**/__pycache__/**)"]  # the second one also ships with the installer
    (claude / "settings.json").write_text(json.dumps({"permissions": {"deny": own}}), encoding="utf-8")
    for _ in range(2):
        r = _run([INSTALL, "--home", home], home)
        assert r.returncode == 0, r.stderr
    md = (claude / "CLAUDE.md").read_text(encoding="utf-8")
    assert md.startswith("# My own rules\nkeep me\n") and md.count("cost-router:context-rules:start") == 1
    settings = json.loads((claude / "settings.json").read_text(encoding="utf-8"))
    deny = settings["permissions"]["deny"]
    assert deny[:2] == own and deny.count("Read(**/__pycache__/**)") == 1 and "Read(**/.mypy_cache/**)" in deny
    assert not any("node_modules" in r or "venv" in r or "dist" in r for r in deny), "dependency/build trees stay readable"
    starts = settings["hooks"]["SessionStart"]
    assert len(starts) == 1 and "cost-router/hook_repo_map.py" in json.dumps(starts)
    assert starts[0]["matcher"] == "startup|clear|compact"
    r = _run([INSTALL, "--home", home, "--uninstall"], home)
    assert r.returncode == 0, r.stderr
    assert (claude / "CLAUDE.md").read_text(encoding="utf-8") == "# My own rules\nkeep me\n"
    settings = json.loads((claude / "settings.json").read_text(encoding="utf-8"))
    assert settings["permissions"]["deny"] == own, "rules the user already had survive the uninstall"
    assert "SessionStart" not in settings.get("hooks", {})


def test_no_context_and_session_start_leave_rules_and_deny_alone(tmp_path):
    for flag in ("--no-context", "--session-start"):
        home = tmp_path / flag.strip("-")
        assert _run([INSTALL, "--home", home, flag], home).returncode == 0
        assert not (home / ".claude" / "CLAUDE.md").exists()
        settings = home / ".claude" / "settings.json"
        assert not settings.exists() or "permissions" not in json.loads(settings.read_text(encoding="utf-8"))


def test_project_settings_register_the_repo_map_hook():
    settings = json.loads((ROOT / ".claude" / "settings.json").read_text(encoding="utf-8"))
    entries = [e for e in settings["hooks"]["SessionStart"] if "hook_repo_map.py" in json.dumps(e)]
    assert len(entries) == 1 and entries[0]["matcher"] == "startup|clear|compact"
    cmd = entries[0]["hooks"][0]["command"]
    assert cmd.rstrip().endswith("exit 0") and "exit 2" not in cmd


def test_hook_survives_a_cp1252_stdout(tmp_path):
    repo = _small_repo(tmp_path)
    (repo / "src" / "README.md").write_text("# Sursele aplicației ș ț ă\n", encoding="utf-8")
    _git(repo, "commit", "-qam", "ro")
    r = _run([HOOK], tmp_path / "home", env_extra={"CLAUDE_PROJECT_DIR": str(repo), "PYTHONIOENCODING": "cp1252"})
    assert r.returncode == 0, r.stderr
    ctx = json.loads(r.stdout)["hookSpecificOutput"]["additionalContext"]
    assert "⚠ big/" in ctx and "aplicației" in ctx
    r = _run([MAP, repo, "--no-cache"], tmp_path / "home", env_extra={"PYTHONIOENCODING": "cp1252"})
    assert r.returncode == 0 and "big/" in r.stdout


def test_staged_changes_and_first_commit_refresh_the_cached_map(tmp_path):
    home = tmp_path / "home"
    repo = tmp_path / "fresh"
    repo.mkdir()
    _git(repo, "init", "-q")
    (repo / "a.py").write_text("", encoding="utf-8")
    _git(repo, "add", "a.py")
    assert "1 tracked files" in _run([MAP, repo], home).stdout  # no commit yet
    (repo / "b.py").write_text("", encoding="utf-8")
    _git(repo, "add", "b.py")
    assert "2 tracked files" in _run([MAP, repo], home).stdout, "a staged file is a new listing"
    _run([MAP, repo, "--max-chars", "1500"], home)
    assert len(list((home / ".claude" / "cache" / "repo-map").glob("*.md"))) == 2, "sizes cache separately"


def test_rules_block_keeps_crlf_and_leaves_a_damaged_block_alone(tmp_path):
    home = tmp_path / "home"
    md = home / ".claude" / "CLAUDE.md"
    md.parent.mkdir(parents=True)
    md.write_bytes(b"a\r\nb\r\n")
    assert _run([INSTALL, "--home", home, "--no-hook"], home).returncode == 0
    assert b"\r\n<!-- cost-router:context-rules:start -->\r\n" in md.read_bytes()
    assert _run([INSTALL, "--home", home, "--uninstall"], home).returncode == 0
    assert md.read_bytes() == b"a\r\nb\r\n"
    damaged = "mine\n<!-- cost-router:context-rules:start -->\nhalf a block, end marker deleted\nmore of mine\n"
    md.write_text(damaged, encoding="utf-8")
    r = _run([INSTALL, "--home", home, "--no-hook"], home)
    assert r.returncode == 0 and "damaged" in r.stdout
    assert md.read_text(encoding="utf-8") == damaged
