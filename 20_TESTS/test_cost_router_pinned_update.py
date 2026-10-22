"""HIGH-2: the cost-router self-update re-syncs to a PINNED, hash-verified release (never to `main`).

A local bare repository (file:// URL, allowAnySHA1InWant like GitHub) stands in for the remote; no network.
Probe list P1..P11 is in 00_GOVERNANCE/coordination/tasks/todo-claude-cost-router-pinned-update.md.
"""
from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / ".claude" / "skills" / "cost-router"
INSTALL = SKILL / "install.py"
FILES = (".claude/skills/cost-router", ".claude/agents",
         "03_IMPLEMENTATION/packages/routing/claude_model_router.py", "04_CONFIG/claude_model_routing.json")
PIN_PATH = ".claude/skills/cost-router/release_pin.json"

pytestmark = pytest.mark.skipif(shutil.which("git") is None, reason="git not installed")


@pytest.fixture(autouse=True)
def _hermetic(monkeypatch):
    for k in ("COST_ROUTER_NO_SELF_INSTALL", "COST_ROUTER_OFF"):
        monkeypatch.delenv(k, raising=False)
    monkeypatch.setenv("COST_ROUTER_SELF_INSTALL", "1")


def _rp_module():
    spec = importlib.util.spec_from_file_location("release_pin_under_test", SKILL / "lib" / "release_pin.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _g(repo: Path, *args: str, data: bytes | None = None) -> str:
    return subprocess.run(["git", "-C", str(repo), "-c", "user.name=t", "-c", "user.email=t@t", *args],
                          check=True, capture_output=True, input=data).stdout.decode().strip()


def _env(home: Path) -> dict:
    return dict(os.environ, HOME=str(home), USERPROFILE=str(home))


def _installer(home: Path, *extra: str, check: bool = True):
    r = subprocess.run([sys.executable, str(INSTALL), "--home", str(home), *extra], env=_env(home),
                       capture_output=True, text=True, timeout=90)
    if check:
        assert r.returncode == 0, r.stdout + r.stderr
    return r


def _hook(home: Path):
    path = home / ".claude" / "skills" / "cost-router" / "hook_self_update.py"
    spec = importlib.util.spec_from_file_location(f"pinned_hook_{abs(hash(str(home)))}", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _snapshot(home: Path) -> dict:
    out = {}
    for sub in ("skills/cost-router", "agents"):
        base = home / ".claude" / sub
        for p in sorted(base.rglob("*")):
            if p.is_file():
                out[p.relative_to(home).as_posix()] = p.read_bytes()
    return out


def _make_pin(files: dict, commit: str, seq: int = 1) -> dict:
    digest = hashlib.sha256(json.dumps(dict(sorted(files.items())), sort_keys=True).encode("utf-8")).hexdigest()
    return {"schema": 1, "seq": seq, "commit": commit, "files": files, "files_sha256": digest}


def _write_pin(path: Path, pin: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(pin, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def _set_installed_pin(env, pin: dict) -> None:
    """Swap the installed pin for `pin` and keep the install a pinned release (VERSION line 1 == pin commit)."""
    _write_pin(env.installed_pin, pin)
    version = env.installed_pin.parent / "VERSION"
    lines = version.read_text(encoding="utf-8").splitlines()
    version.write_text("\n".join([pin["commit"], *lines[1:]]) + "\n", encoding="utf-8")


class Env:
    """work repo (the 'author'), bare remote, pin of the first commit, home with the pinned install."""

    def __init__(self, tmp_path: Path):
        self.tmp = tmp_path
        self.work = tmp_path / "work"
        for rel in FILES:
            src, dst = ROOT / rel, self.work / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            if src.is_dir():
                shutil.copytree(src, dst, ignore=shutil.ignore_patterns("__pycache__"))
            else:
                shutil.copy2(src, dst)
        subprocess.run(["git", "init", "-q", "-b", "main", str(self.work)], check=True)
        _g(self.work, "config", "core.autocrlf", "false")
        self.rp = _rp_module()
        self.bare = tmp_path / "remote.git"
        self.home = tmp_path / "home"
        self.pinfile = tmp_path / "pin.json"

    def commit_all(self, msg: str = "v1") -> str:
        _g(self.work, "add", "-A")
        _g(self.work, "commit", "-q", "-m", msg)
        return _g(self.work, "rev-parse", "HEAD")

    def publish(self) -> None:
        if not self.bare.exists():
            subprocess.run(["git", "init", "-q", "--bare", str(self.bare)], check=True)
            _g(self.bare, "config", "uploadpack.allowAnySHA1InWant", "true")
            _g(self.bare, "config", "uploadpack.allowFilter", "true")
        _g(self.work, "push", "-q", "-f", str(self.bare), "HEAD:refs/heads/main")

    def pin(self, commit: str, seq: int = 1) -> dict:
        pin = self.rp.generate(self.work, commit, seq)
        _write_pin(self.pinfile, pin)
        return pin

    def install(self, *extra: str) -> None:
        if "--source-ref" not in extra:  # a pinned release install records the pin's commit as its VERSION
            extra += ("--source-ref", json.loads(self.pinfile.read_text(encoding="utf-8"))["commit"])
        _installer(self.home, "--release-pin", str(self.pinfile), *extra)

    @property
    def installed_pin(self) -> Path:
        return self.home / ".claude" / "skills" / "cost-router" / "release_pin.json"

    def update(self, now: float = 1_000_000.0, mod=None) -> str:
        mod = mod or _hook(self.home)
        return mod.update(self.home, now=now, url=self.bare.as_uri())


@pytest.fixture()
def env(tmp_path):
    e = Env(tmp_path)
    c1 = e.commit_all()
    e.publish()
    e.c1 = c1
    e.pin_dict = e.pin(c1)
    e.install()
    return e


# ---- happy path --------------------------------------------------------------------------------------------------

def test_happy_path_installs_the_pinned_bytes_and_keeps_the_trusted_pin(env):
    skill = env.home / ".claude" / "skills" / "cost-router"
    (skill / "SKILL.md").write_text("locally drifted\n", encoding="utf-8")
    assert env.update() == "updated"
    for rel, sha in env.pin_dict["files"].items():
        if rel.startswith(".claude/skills/cost-router/chat/"):
            continue
        if rel.startswith(".claude/skills/cost-router/"):
            dst = skill / rel[len(".claude/skills/cost-router/"):]
        elif rel.startswith(".claude/agents/"):
            dst = env.home / ".claude" / "agents" / rel[len(".claude/agents/"):]
        elif rel.endswith("claude_model_router.py"):
            dst = skill / "lib" / "claude_model_router.py"
        else:
            dst = skill / "policy.json"
        assert hashlib.sha256(dst.read_bytes()).hexdigest() == sha, rel
    assert env.installed_pin.read_bytes() == env.pinfile.read_bytes()
    assert (skill / "VERSION").read_text(encoding="utf-8").splitlines()[0] == env.c1
    assert _installer(env.home, "--verify", check=False).returncode == 0


def test_generator_cli_writes_a_pin_that_loads(env, tmp_path):
    out = tmp_path / "cli-pin.json"
    r = subprocess.run([sys.executable, str(SKILL / "lib" / "release_pin.py"), "generate", "--repo", str(env.work),
                        "--commit", env.c1, "--seq", "3", "--out", str(out)], capture_output=True, text=True)
    assert r.returncode == 0, r.stderr
    pin = env.rp.load_pin(out)
    assert pin["seq"] == 3 and pin["commit"] == env.c1 and PIN_PATH not in pin["files"]


# ---- P1 tampered remote ------------------------------------------------------------------------------------------

def test_p1_rewritten_remote_still_yields_the_pinned_bytes(env):
    skill_md = env.work / ".claude/skills/cost-router/SKILL.md"
    skill_md.write_text("EVIL\n", encoding="utf-8")
    _g(env.work, "commit", "-q", "-a", "--amend", "-m", "rewritten")
    env.publish()  # force-push: main no longer contains the pinned commit
    assert _g(env.work, "rev-parse", "HEAD") != env.c1
    assert env.update() == "updated"
    installed = env.home / ".claude" / "skills" / "cost-router" / "SKILL.md"
    assert b"EVIL" not in installed.read_bytes()
    assert installed.read_bytes() == (ROOT / ".claude/skills/cost-router/SKILL.md").read_bytes()


def test_p1_wrong_file_hash_in_the_trusted_pin_is_verify_failed_and_changes_nothing(env):
    pin = copy.deepcopy(env.pin_dict)
    pin["files"][".claude/skills/cost-router/SKILL.md"] = "0" * 64
    _write_pin(env.installed_pin, _make_pin(pin["files"], pin["commit"], pin["seq"]))
    before = _snapshot(env.home)
    assert env.update() == "verify-failed"
    assert _snapshot(env.home) == before


def test_extra_file_in_the_fetched_tree_is_verify_failed(env):
    (env.work / ".claude/skills/cost-router/lib/extra.py").write_text("print(1)\n", encoding="utf-8")
    c2 = env.commit_all("extra")
    env.publish()
    _set_installed_pin(env, _make_pin(env.pin_dict["files"], c2))  # pins c2 but lists only c1's files
    before = _snapshot(env.home)
    assert env.update() == "verify-failed"
    assert _snapshot(env.home) == before


# ---- P2 pinned sha absent ----------------------------------------------------------------------------------------

def test_p2_pinned_sha_absent_from_the_remote_is_fetch_failed(env):
    _set_installed_pin(env, _make_pin(env.pin_dict["files"], "1" * 40))
    before = _snapshot(env.home)
    assert env.update() == "fetch-failed"
    assert _snapshot(env.home) == before


def test_unreachable_remote_is_fetch_failed(env, tmp_path):
    before = _snapshot(env.home)
    assert _hook(env.home).update(env.home, now=1.0, url=(tmp_path / "nope").as_uri()) == "fetch-failed"
    assert _snapshot(env.home) == before


# ---- P3 manifest swapped together with files ---------------------------------------------------------------------

def test_p3_a_pin_inside_the_fetched_tree_is_ignored(tmp_path):
    e = Env(tmp_path)
    evil = _make_pin({".claude/skills/cost-router/SKILL.md": "f" * 64}, "2" * 40, seq=99)
    _write_pin(e.work / PIN_PATH, evil)
    c1 = e.commit_all()
    e.publish()
    e.pin(c1)
    e.install()
    assert e.update() == "updated"
    assert e.installed_pin.read_bytes() == e.pinfile.read_bytes()
    assert json.loads(e.installed_pin.read_text(encoding="utf-8"))["seq"] == 1


# ---- P4 partial write --------------------------------------------------------------------------------------------

def test_p4_truncated_blob_reads_fail_verification(env, monkeypatch):
    mod = _hook(env.home)
    real = mod._rp._read_blob
    monkeypatch.setattr(mod._rp, "_read_blob", lambda *a, **k: real(*a, **k)[:-1])
    before = _snapshot(env.home)
    assert env.update(mod=mod) == "verify-failed"
    assert _snapshot(env.home) == before


# ---- P5 bad manifest keys ----------------------------------------------------------------------------------------

@pytest.mark.parametrize("key", ["../x", "/abs", "C:/x", "a\\b", "", ".", "README.md", PIN_PATH,
                                 ".claude/skills/cost-router/../../x", ".claude/skills/cost-router//x",
                                 ".claude/skills/cost-router/./x", ".claude/skills/cost-router/a\x00b",
                                 ".claude/skills-evil/x", "04_CONFIG/claude_model_routing.json/../../x"])
def test_p5_bad_manifest_keys_are_rejected(tmp_path, key):
    rp = _rp_module()
    path = tmp_path / "pin.json"
    _write_pin(path, _make_pin({key: "a" * 64}, "a" * 40))
    with pytest.raises(rp.PinError):
        rp.load_pin(path)


def test_p5_control_a_clean_pin_loads(tmp_path):
    rp = _rp_module()
    path = tmp_path / "pin.json"
    _write_pin(path, _make_pin({".claude/skills/cost-router/lib/route.py": "a" * 64,
                                "04_CONFIG/claude_model_routing.json": "b" * 64}, "a" * 40, seq=4))
    assert rp.load_pin(path)["seq"] == 4


# ---- P6 symlink --------------------------------------------------------------------------------------------------

def test_p6_symlink_blob_is_refused(env):
    oid = _g(env.work, "hash-object", "-w", "--stdin", data=b"../../../outside")
    _g(env.work, "update-index", "--add", "--cacheinfo", f"120000,{oid},.claude/skills/cost-router/lib/link")
    _g(env.work, "commit", "-q", "-m", "symlink")  # not commit_all: add -A would drop the index-only symlink
    c2 = _g(env.work, "rev-parse", "HEAD")
    env.publish()
    with pytest.raises(env.rp.PinError):
        env.rp.generate(env.work, c2, 2)
    _set_installed_pin(env, _make_pin(env.pin_dict["files"], c2))
    before = _snapshot(env.home)
    assert env.update() == "verify-failed"
    assert _snapshot(env.home) == before
    with pytest.raises(env.rp.PinError):
        env.rp.fetch_verified(env.bare.as_uri(), env.rp.load_pin(env.installed_pin), env.tmp / "dest", 30)


# ---- P7 downgrade ------------------------------------------------------------------------------------------------

def test_p7_downgrade_is_refused_without_force(tmp_path):
    e = Env(tmp_path)
    c1 = e.commit_all()
    e.publish()
    e.pin(c1, seq=2)
    e.install()
    low = tmp_path / "low.json"
    _write_pin(low, e.rp.generate(e.work, c1, 1))
    before = _snapshot(e.home)
    r = _installer(e.home, "--release-pin", str(low), check=False)
    assert r.returncode == 1 and "not installed" in r.stderr
    assert _snapshot(e.home) == before
    assert _installer(e.home, "--release-pin", str(low), "--force", check=False).returncode == 0
    assert json.loads(e.installed_pin.read_text(encoding="utf-8"))["seq"] == 1
    _installer(e.home, "--release-pin", str(e.pinfile))  # upgrade back is fine
    assert json.loads(e.installed_pin.read_text(encoding="utf-8"))["seq"] == 2


def test_release_pin_flag_validates_the_pin(env, tmp_path):
    bad = tmp_path / "bad.json"
    bad.write_text("{not json", encoding="utf-8")
    before = _snapshot(env.home)
    assert _installer(env.home, "--release-pin", str(bad), check=False).returncode == 1
    assert _snapshot(env.home) == before


# ---- P8 fetched code is never executed; install comes from the verified staging ----------------------------------

def test_p8_fetched_installer_is_never_executed_and_install_uses_the_verified_staging(tmp_path, monkeypatch):
    e = Env(tmp_path)
    sentinel = tmp_path / "SENTINEL"
    inst = e.work / ".claude/skills/cost-router/install.py"
    inst.write_text(f"open({str(sentinel)!r}, 'w').write('pwned')\n" + inst.read_text(encoding="utf-8"),
                    encoding="utf-8")
    hook = e.work / ".claude/skills/cost-router/hook_self_update.py"
    hook.write_text(f"open({str(sentinel)!r}, 'w').write('pwned')\n" + hook.read_text(encoding="utf-8"),
                    encoding="utf-8")
    c1 = e.commit_all()
    e.publish()
    pin = e.pin(c1)
    e.install()  # installs from the genuine worktree installer, pin only
    mod = _hook(e.home)
    seen = []
    real_run = subprocess.run

    def spy(cmd, *a, **k):
        if isinstance(cmd, list) and len(cmd) > 2 and str(cmd[1]).endswith("install.py"):
            root = Path(cmd[cmd.index("--source-root") + 1])
            handed = Path(cmd[cmd.index("--release-pin") + 1])
            seen.append({"cmd": cmd, "pin_bytes": handed.read_bytes(), "tmp": Path(root).parent,
                         "files": {rel: hashlib.sha256((root / rel).read_bytes()).hexdigest()
                                               for rel in pin["files"]}})
        return real_run(cmd, *a, **k)

    monkeypatch.setattr(mod.subprocess, "run", spy)
    assert e.update(mod=mod) == "updated"
    assert not sentinel.exists()
    assert len(seen) == 1
    cmd = seen[0]["cmd"]
    assert Path(cmd[1]) == e.home / ".claude" / "skills" / "cost-router" / "install.py"
    handed = Path(cmd[cmd.index("--release-pin") + 1])
    assert handed != e.installed_pin and handed.parent.parent == seen[0]["tmp"], "hook hands over its own pin copy"
    assert seen[0]["pin_bytes"] == e.installed_pin.read_bytes()
    assert cmd[cmd.index("--source-ref") + 1] == c1 and "--self-update" in cmd
    assert seen[0]["files"] == pin["files"], "the installer was fed exactly the verified bytes"
    assert not sentinel.exists()
    # the pinned (poisoned) bytes were installed as data, but nothing ran them
    assert (e.home / ".claude/skills/cost-router/install.py").read_bytes() == inst.read_bytes()


# ---- P9 / P10 ----------------------------------------------------------------------------------------------------

def test_p9_no_pin_installed_means_no_git_at_all(tmp_path, monkeypatch):
    home = tmp_path / "home"
    _installer(home)  # a plain install carries no pin
    mod = _hook(home)

    def boom(*a, **k):
        raise AssertionError("git/subprocess used without a trusted pin")

    monkeypatch.setattr(mod, "_git", boom)
    monkeypatch.setattr(mod.subprocess, "run", boom)
    assert mod.update(home, now=5.0, url="file:///nowhere") == "no-pin"


def _variant_bad_json(p):
    return "{not json"


def _variant(mutate):
    def make(pin):
        pin = copy.deepcopy(pin)
        mutate(pin)
        return json.dumps(pin)
    return make


@pytest.mark.parametrize("mutate", [
    lambda p: p.update(schema=2),
    lambda p: p.update(commit=p["commit"][:12]),
    lambda p: p.update(commit=p["commit"].upper()),
    lambda p: p.update(files_sha256="0" * 64),
    lambda p: p.update(seq=0),
    lambda p: p.update(seq=True),
    lambda p: p.update(files={}),
    lambda p: p.update(extra=1),
    lambda p: p["files"].update({".claude/skills/cost-router/x": "xyz"}),
], ids=["schema2", "short-commit", "upper-commit", "bad-digest", "seq0", "seq-bool", "empty-files", "unknown-key",
        "bad-sha-value"])
def test_p10_malformed_pins_are_pin_invalid(env, mutate, monkeypatch):
    env.installed_pin.write_text(_variant(mutate)(env.pin_dict), encoding="utf-8")
    mod = _hook(env.home)
    monkeypatch.setattr(mod.subprocess, "run", lambda *a, **k: (_ for _ in ()).throw(AssertionError("git ran")))
    assert env.update(mod=mod) == "pin-invalid"


def test_p10_not_json_is_pin_invalid(env):
    env.installed_pin.write_text("{not json", encoding="utf-8")
    assert env.update() == "pin-invalid"


# ---- P11 eol -----------------------------------------------------------------------------------------------------

def test_p11_crlf_bytes_hash_as_stored_and_install_unconverted(tmp_path, monkeypatch):
    e = Env(tmp_path)
    data = b"line one\r\nline two\r\n"
    oid = _g(e.work, "hash-object", "-w", "--no-filters", "--stdin", data=data)
    _g(e.work, "add", "-A")
    _g(e.work, "update-index", "--add", "--cacheinfo", f"100644,{oid},.claude/skills/cost-router/crlf.txt")
    _g(e.work, "commit", "-q", "-m", "crlf")
    c1 = _g(e.work, "rev-parse", "HEAD")
    e.publish()
    pin = e.pin(c1)
    assert pin["files"][".claude/skills/cost-router/crlf.txt"] == hashlib.sha256(data).hexdigest()
    e.install()
    monkeypatch.setenv("GIT_CONFIG_COUNT", "1")
    monkeypatch.setenv("GIT_CONFIG_KEY_0", "core.autocrlf")
    monkeypatch.setenv("GIT_CONFIG_VALUE_0", "true")
    assert e.update() == "updated"
    assert (e.home / ".claude/skills/cost-router/crlf.txt").read_bytes() == data


# ---- gates kept --------------------------------------------------------------------------------------------------

def test_throttle_and_disabled_marker(env):
    mod = _hook(env.home)
    assert env.update(now=1_000_000.0, mod=mod) == "updated"
    assert env.update(now=1_000_100.0, mod=mod) == "throttled"
    assert env.update(now=1_000_000.0 + 7200, mod=mod) == "updated"
    (env.home / ".claude" / "cost-router.disabled").write_text("x", encoding="utf-8")
    assert env.update(now=9_000_000.0, mod=mod) == "disabled"


def test_main_gates_are_unchanged(tmp_path, monkeypatch):
    home = tmp_path / "home"
    home.mkdir()
    monkeypatch.setenv("HOME", str(home))
    monkeypatch.setenv("USERPROFILE", str(home))
    monkeypatch.delenv("COST_ROUTER_SELF_INSTALL", raising=False)
    mod = _rp_hook_from_source()
    calls = []
    monkeypatch.setattr(mod, "update", lambda h, *a, **k: calls.append(h) or "x")
    monkeypatch.setattr(mod.sys, "stdin", __import__("io").StringIO("{}"))
    assert mod.main() == 0 and calls == []  # default OFF
    (home / ".claude").mkdir()
    (home / ".claude" / "cost-router.auto-refresh").write_text("1", encoding="utf-8")
    monkeypatch.setattr(mod.sys, "stdin", __import__("io").StringIO("{}"))
    assert mod.main() == 0 and calls == [home]
    monkeypatch.setenv("COST_ROUTER_NO_SELF_INSTALL", "1")
    assert mod.main() == 0 and calls == [home]


def _rp_hook_from_source():
    spec = importlib.util.spec_from_file_location("pinned_hook_src", SKILL / "hook_self_update.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def test_hook_imports_sparse_from_the_pin_module():
    assert _rp_hook_from_source().SPARSE == _rp_module().SPARSE


def test_fetch_is_filtered_and_blobs_outside_sparse_are_never_requested(tmp_path, monkeypatch):
    e = Env(tmp_path)
    (e.work / "big").mkdir()
    (e.work / "big" / "payload.bin").write_bytes(os.urandom(200_000))
    big_oid = _g(e.work, "hash-object", "big/payload.bin")
    c1 = e.commit_all()
    e.publish()
    e.pin(c1)
    e.install()
    mod = _hook(e.home)
    real_run, real_read = subprocess.run, mod._rp._read_blob
    fetches, probes = [], []

    def spy(cmd, *a, **k):
        if isinstance(cmd, list) and "fetch" in cmd:
            fetches.append(cmd)
        return real_run(cmd, *a, **k)

    def read(repo, oid, timeout):
        if not probes:  # the object store at read time: the big blob outside SPARSE must be absent
            probes.append(real_run(["git", "-C", str(repo), "cat-file", "-e", big_oid],
                                   env=dict(os.environ, GIT_NO_LAZY_FETCH="1"), capture_output=True).returncode)
        return real_read(repo, oid, timeout)

    monkeypatch.setattr(mod.subprocess, "run", spy)
    monkeypatch.setattr(mod._rp, "_read_blob", read)
    assert e.update(mod=mod) == "updated"
    assert len(fetches) >= 2 and all("--filter=blob:none" in c for c in fetches)
    assert not any(big_oid in c for c in fetches)
    assert probes and probes[0] != 0, "a blob outside SPARSE reached the temp object store"


# ---- review round 1 fixes ----------------------------------------------------------------------------------------

def test_f1_checkout_install_newer_than_the_pin_is_not_rolled_back(env, monkeypatch):
    other = "b" * 40
    skill = env.home / ".claude" / "skills" / "cost-router"
    (skill / "VERSION").write_text(other + "\nhash\n", encoding="utf-8")  # owner installed from a checkout ref
    assert (skill / "VERSION").read_text(encoding="utf-8").splitlines()[0] == other
    before = _snapshot(env.home)
    mod = _hook(env.home)

    def boom(*a, **k):
        raise AssertionError("git/subprocess used for a non-pinned install")

    monkeypatch.setattr(mod, "_git", boom)
    monkeypatch.setattr(mod.subprocess, "run", boom)
    monkeypatch.setattr(mod._rp, "fetch_verified", boom)
    assert mod.update(env.home, now=7.0, url=env.bare.as_uri()) == "not-pinned-install"
    assert _snapshot(env.home) == before


@pytest.mark.parametrize("version", ["unknown", "abc1234", "{c1}+dirty", ""])
def test_f1_odd_versions_are_not_pinned_installs(env, version):
    skill = env.home / ".claude" / "skills" / "cost-router"
    (skill / "VERSION").write_text(version.format(c1=env.c1) + "\nhash\n", encoding="utf-8")
    assert env.update() == "not-pinned-install"


def test_f1_pinned_install_is_still_repaired(env):
    skill = env.home / ".claude" / "skills" / "cost-router"
    assert (skill / "VERSION").read_text(encoding="utf-8").splitlines()[0] == env.pin_dict["commit"]
    good = (skill / "SKILL.md").read_bytes()
    (skill / "SKILL.md").write_text("tampered\n", encoding="utf-8")
    assert env.update() == "updated"
    assert (skill / "SKILL.md").read_bytes() == good


def test_f2_release_pin_and_source_ref_must_agree(env, tmp_path):
    fresh = tmp_path / "fresh-home"
    r = _installer(fresh, "--release-pin", str(env.pinfile), "--source-ref", "c" * 40, check=False)
    assert r.returncode == 1 and "not installed" in r.stderr
    assert not (fresh / ".claude" / "skills").exists()
    before = _snapshot(env.home)
    r = _installer(env.home, "--release-pin", str(env.pinfile), "--source-ref", "c" * 40, "--force", check=False)
    assert r.returncode == 1 and _snapshot(env.home) == before


def test_f3_pin_is_read_once_and_the_validated_bytes_are_written(env, tmp_path, monkeypatch):
    spec = importlib.util.spec_from_file_location("install_under_test", INSTALL)
    inst = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(inst)

    real_load = inst.release_pin.load_pin

    def guarded(path):
        assert Path(path) != env.pinfile, "the pin being installed must not be re-read through load_pin"
        return real_load(path)

    monkeypatch.setattr(inst.release_pin, "load_pin", guarded)
    monkeypatch.setattr(inst, "_repo_head", lambda: env.c1)
    home = tmp_path / "h3"
    inst.install(home, False, False, with_context=False, source_ref=env.c1, pin_file=env.pinfile)
    assert (home / ".claude/skills/cost-router/release_pin.json").read_bytes() == env.pinfile.read_bytes()


def test_f3_parse_pin_validates_bytes(env):
    assert env.rp.parse_pin(env.pinfile.read_bytes())["commit"] == env.c1
    with pytest.raises(env.rp.PinError):
        env.rp.parse_pin(b"\xff\xfe not json")
    with pytest.raises(env.rp.PinError):
        env.rp.parse_pin(b"{}")


@pytest.mark.parametrize("bad", [".claude/skills/cost-router/a.", ".claude/skills/cost-router/a ",
                                 ".claude/skills/cost-router/a:b", ".claude/skills/cost-router/a.txt:stream"])
def test_safe_rel_rejects_trailing_dot_space_and_ads(bad):
    rp = _rp_module()
    with pytest.raises(rp.PinError):
        rp.safe_rel(bad)


# ---- round-2 review: the documented release procedure, and the race under the lock ------------------------------

def _work_installer(env, home: Path, *extra: str):
    work_install = env.work / ".claude" / "skills" / "cost-router" / "install.py"
    return subprocess.run([sys.executable, str(work_install), "--home", str(home), "--no-hook", "--no-context", *extra],
                          env=_env(home), capture_output=True, text=True, timeout=90)


def test_documented_release_procedure_keeps_the_hook_active(tmp_path):
    env = Env(tmp_path)
    c1 = env.commit_all()
    pin = env.rp.generate(env.work, c1, 1)
    _write_pin(env.work / ".claude" / "skills" / "cost-router" / "release_pin.json", pin)  # step 3: pin in a later commit
    c2 = env.commit_all("pin")
    env.publish()
    home = tmp_path / "doc-home"
    r = _work_installer(env, home, "--source-ref", c1)  # step 4 as documented
    assert r.returncode == 0, r.stdout + r.stderr
    skill = home / ".claude" / "skills" / "cost-router"
    assert (skill / "VERSION").read_text(encoding="utf-8").splitlines()[0] == c1
    (skill / "SKILL.md").write_text("tampered\n", encoding="utf-8")
    assert _hook(home).update(home, now=1_000_000.0, url=env.bare.as_uri()) == "updated"
    blob = subprocess.run(["git", "-C", str(env.work), "cat-file", "blob", f"{c1}:.claude/skills/cost-router/SKILL.md"],
                          check=True, capture_output=True).stdout
    assert (skill / "SKILL.md").read_bytes() == blob  # repaired to the exact pinned bytes
    # without --source-ref the checkout HEAD is recorded and the hook leaves that install alone
    home2 = tmp_path / "doc-home2"
    assert _work_installer(env, home2).returncode == 0
    assert (home2 / ".claude/skills/cost-router/VERSION").read_text(encoding="utf-8").splitlines()[0] != c1
    assert _hook(home2).update(home2, now=1_000_000.0, url=env.bare.as_uri()) == "not-pinned-install"
    # a --source-ref that is not the commit the source pin names is refused, nothing installed
    home3 = tmp_path / "doc-home3"
    r = _work_installer(env, home3, "--source-ref", c2)
    assert r.returncode == 1 and "not installed" in r.stderr
    assert not (home3 / ".claude" / "skills" / "cost-router").exists()


def test_self_update_rechecks_the_installed_version_under_the_lock(env, tmp_path):
    spec = importlib.util.spec_from_file_location("install_race", INSTALL)
    inst = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(inst)
    skill = env.home / ".claude" / "skills" / "cost-router"
    (skill / "VERSION").write_text("d" * 40 + "\nhash\n", encoding="utf-8")  # the owner installed during the fetch
    before = _snapshot(env.home)
    with pytest.raises(ValueError, match="no longer the pinned release"):
        inst.install(env.home, False, False, with_context=False, source_ref=env.c1, self_update=True,
                     pin_file=env.pinfile)
    assert _snapshot(env.home) == before
