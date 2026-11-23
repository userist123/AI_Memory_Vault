"""aas_build.runner: fixed-argv docker invocation and the link-failing output reader (P1c, P7a, P8b, P8c, P8d)."""
from __future__ import annotations

import hashlib
import re
from pathlib import Path

import pytest
from aas_build import runner
from aas_build.runner import BuildError, BuildLimits

from .conftest import make_link

IMAGE = "aas-web-build@sha256:" + "a" * 64
NOEXIST = {"container_exists": lambda n: False, "container_name": "aas-b-x"}


def _argv(**kw):  # type: ignore[no-untyped-def]
    args = dict(docker="C:/Program Files/Docker/docker.exe", build_id="b0123456789abcdef", ctx=Path("D:/x/ctx"), out=Path("D:/x/out"), image=IMAGE)
    args.update(kw)
    return runner.build_web_argv(**args)


# ---- argv shape -----------------------------------------------------------------------------------------------------
def test_argv_is_hardened_and_offline() -> None:
    a = _argv()
    assert isinstance(a, list) and all(isinstance(x, str) for x in a)
    joined = " ".join(a)
    for needed in ("--network none", "--read-only", "--cap-drop ALL", "--security-opt no-new-privileges", "--user 1000:1000", "--pids-limit 512", "--memory 2g", "--ulimit fsize="):
        assert needed in joined, needed
    tmpfs = [a[i + 1] for i, x in enumerate(a) if x == "--tmpfs"]
    assert len(tmpfs) == 1 and "noexec" in tmpfs[0] and "nosuid" in tmpfs[0] and "nodev" in tmpfs[0] and "exec," not in tmpfs[0].replace("noexec", "")
    assert a[0].endswith("docker.exe") and a[1] == "run" and "--rm" in a
    assert a[-1] == "/usr/local/bin/aas-build-web" and a[-2] == IMAGE


def test_argv_mounts_src_readonly_out_rw() -> None:
    a = _argv()
    mounts = [a[i + 1] for i, x in enumerate(a) if x == "-v"]
    assert any(m.endswith(":/src:ro") for m in mounts)
    assert any(m.endswith(":/out:rw") for m in mounts)
    assert len(mounts) == 2


@pytest.mark.parametrize("bad", ["-p", "--publish", "--network=bridge", "--network host", "--privileged", "--net=host"])
def test_argv_never_contains_network_or_publish_tokens(bad: str) -> None:
    a = _argv()
    assert bad not in a  # exact token
    assert f" {bad} " not in " " + " ".join(a) + " "  # and as a whole word sequence (substring '-p' would match '--pids-limit')
    assert sum(1 for x in a if x.startswith("--net")) == 1  # exactly one --network (value 'none')
    assert a[a.index("--network") + 1] == "none"


@pytest.mark.parametrize("name", ["b;rm -rf", "../x", "a b", "", "B" * 80, "x\ny"])
def test_argv_refuses_bad_build_id(name: str) -> None:
    with pytest.raises(BuildError):
        _argv(build_id=name)


@pytest.mark.parametrize("img", ["aas-web-build:latest", "node@sha256:short", "x y@sha256:" + "a" * 64, "--privileged"])
def test_argv_requires_digest_pinned_image(img: str) -> None:
    with pytest.raises(BuildError):
        _argv(image=img)


def test_argv_requires_absolute_docker_and_dirs() -> None:
    with pytest.raises(BuildError):
        _argv(docker="docker")
    with pytest.raises(BuildError):
        _argv(ctx=Path("rel/ctx"))
    with pytest.raises(BuildError):
        _argv(out=Path("D:/x/o,ut"))  # a comma would let a -v/--mount option smuggle extra fields


class FakePopen:
    """Stand-in for subprocess.Popen: emits canned stdout/stderr, optionally blocks in wait() until killed."""

    instances: list = []

    def __init__(self, argv, **kw):  # type: ignore[no-untyped-def]
        import io
        import threading

        type(self).instances.append(self)
        self.argv, self.kw = argv, kw
        self.stdout = io.BytesIO(type(self).out_bytes)
        self.stderr = io.BytesIO(type(self).err_bytes)
        self.returncode = None
        self._killed = threading.Event()

    out_bytes = b""
    err_bytes = b""
    exit_code = 0
    block = False

    def wait(self, timeout=None):  # type: ignore[no-untyped-def]
        if type(self).block and not self._killed.wait(timeout or 30):
            raise runner.subprocess.TimeoutExpired(self.argv, timeout)
        self.returncode = 137 if self._killed.is_set() else type(self).exit_code
        return self.returncode

    def kill(self) -> None:
        self._killed.set()


@pytest.fixture()
def fake_popen(monkeypatch: pytest.MonkeyPatch):  # type: ignore[no-untyped-def]
    class F(FakePopen):
        instances: list = []

    monkeypatch.setattr(runner.subprocess, "Popen", F)
    monkeypatch.setattr(runner, "_docker_kill", lambda docker, name: [i.kill() for i in F.instances])
    return F


def _build(tmp_path: Path, out: Path, **kw):  # type: ignore[no-untyped-def]
    return runner.run_web_build(docker="C:/d/docker.exe", build_id="b0123456789abcdef", ctx=tmp_path, out=out, image=IMAGE,
                                dest=tmp_path / "site", container_exists=lambda n: False, **kw)


def test_run_uses_no_shell(fake_popen, tmp_path: Path, out_dir: Path) -> None:  # type: ignore[no-untyped-def]
    _build(tmp_path, out_dir)
    p = fake_popen.instances[0]
    assert isinstance(p.argv, list) and p.kw.get("shell", False) is False
    assert "--network" in p.argv and p.argv[p.argv.index("--network") + 1] == "none"


def test_nonzero_exit_fails_build_and_deletes_out(fake_popen, tmp_path: Path, out_dir: Path) -> None:  # type: ignore[no-untyped-def]
    fake_popen.exit_code, fake_popen.err_bytes = 1, b"vite: boom"
    with pytest.raises(BuildError, match="exit code 1"):
        _build(tmp_path, out_dir)
    assert not (tmp_path / "site").exists()  # nothing copied
    assert not out_dir.exists()  # out dir deleted on failure


def test_collect_failure_deletes_out_dir(fake_popen, tmp_path: Path, out_dir: Path) -> None:  # type: ignore[no-untyped-def]
    (out_dir / "dist" / "evil.exe").write_bytes(b"x")
    with pytest.raises(BuildError, match="extension"):
        _build(tmp_path, out_dir)
    assert not out_dir.exists()


def test_captured_logs_are_capped(fake_popen, tmp_path: Path, out_dir: Path) -> None:  # type: ignore[no-untyped-def]
    fake_popen.out_bytes = b"A" * (5 * 1024 * 1024) + b"END-OF-STDOUT"
    fake_popen.err_bytes = b"B" * (3 * 1024 * 1024)
    res = _build(tmp_path, out_dir)
    assert len(res.log_tail) <= 4000 and "END-OF-STDOUT" in res.log_tail  # only the tail survives, memory stays bounded
    assert runner.LOG_CAP_BYTES == 1024 * 1024


def test_out_growth_during_build_kills_container(fake_popen, tmp_path: Path, out_dir: Path) -> None:  # type: ignore[no-untyped-def]
    fake_popen.block = True  # the "container" keeps running until the watchdog kills it
    (out_dir / "dist" / "big.txt").write_bytes(b"x" * 5000)
    with pytest.raises(BuildError, match="during the build"):
        _build(tmp_path, out_dir, limits=BuildLimits(max_files=10, max_total_bytes=1000, max_file_bytes=1000), poll=0.05)
    assert not out_dir.exists()


def test_n1_file_count_growth_during_build_kills_container(fake_popen, tmp_path: Path, out_dir: Path) -> None:  # type: ignore[no-untyped-def]
    """N1 (S3 review): many tiny files stay under the byte cap, so the watchdog counts files as well."""
    fake_popen.block = True
    for i in range(30):
        (out_dir / "dist" / f"f{i}.txt").write_bytes(b"x")  # 30 bytes in total, far below max_total_bytes
    with pytest.raises(BuildError, match="more than 10 files during the build; container killed"):
        _build(tmp_path, out_dir, limits=BuildLimits(max_files=10, max_total_bytes=10_000_000, max_file_bytes=1000), poll=0.05, timeout=5)
    assert not out_dir.exists()


def test_timeout_kills_and_fails(fake_popen, tmp_path: Path, out_dir: Path) -> None:  # type: ignore[no-untyped-def]
    fake_popen.block = True
    with pytest.raises(BuildError, match="timed out"):
        _build(tmp_path, out_dir, timeout=1)


# ---- container-gone check fails closed (review item 4) -----------------------------------------------------------------
class _R:
    def __init__(self, code: int, err: bytes) -> None:
        self.returncode, self.stderr, self.stdout = code, err, b""


@pytest.mark.parametrize("code,err,expected", [
    (0, b"", True),
    (1, b"Error response from daemon: No such container: aas-b-x", False),
    (1, b"Error: No such object: aas-b-x", False),
])
def test_container_exists_answers(monkeypatch: pytest.MonkeyPatch, code: int, err: bytes, expected: bool) -> None:
    monkeypatch.setattr(runner.subprocess, "run", lambda *a, **k: _R(code, err))
    assert runner.docker_container_exists("C:/d/docker.exe", "aas-b-b0123456789abcdef") is expected


@pytest.mark.parametrize("err", [b"", b"error during connect: daemon is not running", b"permission denied", b"context deadline exceeded"])
def test_container_exists_daemon_errors_fail_closed(monkeypatch: pytest.MonkeyPatch, err: bytes) -> None:
    monkeypatch.setattr(runner.subprocess, "run", lambda *a, **k: _R(1, err))
    with pytest.raises(BuildError, match="failing closed"):
        runner.docker_container_exists("C:/d/docker.exe", "aas-b-b0123456789abcdef")



# ---- output reader --------------------------------------------------------------------------------------------------
def test_collect_accepts_clean_output_and_hashes(out_dir: Path, tmp_path: Path) -> None:
    dest = tmp_path / "site" / "r1"
    res = runner.collect_output(out_dir, dest, **NOEXIST)
    assert sorted(res.files) == ["assets/app.js", "index.html"]
    assert (dest / "index.html").read_text(encoding="utf-8").startswith("<!doctype")
    h = hashlib.sha256()
    for rel in sorted(res.files):
        h.update(rel.encode() + b"\0" + (dest / rel).read_bytes() + b"\0")
    assert res.dist_sha256 == h.hexdigest()


def test_collect_refuses_while_container_still_exists(out_dir: Path, tmp_path: Path) -> None:  # P8d
    with pytest.raises(BuildError, match="still"):
        runner.collect_output(out_dir, tmp_path / "s", container_exists=lambda n: True, container_name="aas-b-x")
    assert not (tmp_path / "s").exists()
    # [+] after exit it reads
    runner.collect_output(out_dir, tmp_path / "s", **NOEXIST)
    assert (tmp_path / "s" / "index.html").exists()


@pytest.mark.parametrize("kind", ["symlink", "junction", "hardlink"])
def test_p8b_links_in_output_fail_the_build(kind: str, out_dir: Path, canary: Path, tmp_path: Path) -> None:
    dist = out_dir / "dist"
    if kind == "junction":
        target, link = canary.parent, dist / "jct"
    else:
        target, link = canary, dist / "leak.txt"
    if not make_link(link, target, kind):
        pytest.skip(f"cannot create {kind} on this host")
    # positive control: the naive AWS execution.py style reader (rglob + read_bytes) leaks the canary
    if kind != "hardlink":
        naive = [p.read_bytes() for p in out_dir.rglob("*") if p.is_file()]
        assert b"SECRET-CANARY" in b"".join(naive)
    dest = tmp_path / "site" / "r"
    with pytest.raises(BuildError, match="link|reparse"):
        runner.collect_output(out_dir, dest, **NOEXIST)
    assert not dest.exists() or not any(dest.rglob("*"))
    assert canary.read_text(encoding="utf-8") == "SECRET-CANARY"


def test_symlink_to_directory_refused(out_dir: Path, tmp_path: Path) -> None:
    if not make_link(out_dir / "dist" / "dlink", tmp_path, "symlink"):
        pytest.skip("cannot create symlink")
    with pytest.raises(BuildError, match="link or reparse"):
        runner.collect_output(out_dir, tmp_path / "s", **NOEXIST)


@pytest.mark.parametrize("name", ["evil.exe", "run.bat", "x.php", "noext", "a.JS.exe"])
def test_extension_allow_list(name: str, out_dir: Path, tmp_path: Path) -> None:
    (out_dir / "dist" / name).write_bytes(b"x")
    with pytest.raises(BuildError, match="extension|not allowed"):
        runner.collect_output(out_dir, tmp_path / "s", **NOEXIST)


@pytest.mark.parametrize("name", [".claude", "CLAUDE.md", "AGENTS.md", ".mcp.json"])
def test_control_file_names_fail_build(name: str, out_dir: Path, tmp_path: Path) -> None:
    (out_dir / "dist" / name).write_text("{}", encoding="utf-8")
    with pytest.raises(BuildError):
        runner.collect_output(out_dir, tmp_path / "s", **NOEXIST)


def test_case_insensitive_allowed_extensions(out_dir: Path, tmp_path: Path) -> None:
    (out_dir / "dist" / "Logo.PNG").write_bytes(b"\x89PNG")
    res = runner.collect_output(out_dir, tmp_path / "s", **NOEXIST)
    assert "Logo.PNG" in res.files


def test_p8c_file_count_and_size_caps(out_dir: Path, tmp_path: Path) -> None:
    for i in range(3):
        (out_dir / "dist" / f"f{i}.txt").write_text("x", encoding="utf-8")
    ok = BuildLimits(max_files=5, max_total_bytes=10_000, max_file_bytes=10_000)
    runner.collect_output(out_dir, tmp_path / "ok", limits=ok, **NOEXIST)  # at limit
    with pytest.raises(BuildError, match="files"):
        runner.collect_output(out_dir, tmp_path / "bad", limits=BuildLimits(max_files=4, max_total_bytes=10_000, max_file_bytes=10_000), **NOEXIST)
    with pytest.raises(BuildError, match="size|bytes"):
        runner.collect_output(out_dir, tmp_path / "bad2", limits=BuildLimits(max_files=50, max_total_bytes=20, max_file_bytes=10_000), **NOEXIST)
    with pytest.raises(BuildError, match="size|bytes"):
        runner.collect_output(out_dir, tmp_path / "bad3", limits=BuildLimits(max_files=50, max_total_bytes=10_000, max_file_bytes=10), **NOEXIST)


def test_missing_index_html_fails(tmp_path: Path) -> None:
    o = tmp_path / "o" / "dist"
    o.mkdir(parents=True)
    (o / "a.js").write_text("1", encoding="utf-8")
    with pytest.raises(BuildError, match="index.html"):
        runner.collect_output(tmp_path / "o", tmp_path / "s", **NOEXIST)


def test_dest_must_be_empty_or_absent(out_dir: Path, tmp_path: Path) -> None:
    d = tmp_path / "s"
    d.mkdir()
    (d / "stale.js").write_text("1", encoding="utf-8")
    with pytest.raises(BuildError, match="dest"):
        runner.collect_output(out_dir, d, **NOEXIST)


# ---- P1c: only runner.py may spawn processes -------------------------------------------------------------------------
def _spawners(source: str) -> bool:
    import ast

    for node in ast.walk(ast.parse(source)):
        names: list[str] = []
        if isinstance(node, ast.Import):
            names = [a.name for a in node.names]
        elif isinstance(node, ast.ImportFrom):
            names = [node.module or ""]
        if any(n.split(".")[0] in {"subprocess", "ctypes"} for n in names):
            return True
        if isinstance(node, ast.Attribute) and node.attr in {"system", "popen"} and isinstance(node.value, ast.Name) and node.value.id == "os":
            return True
    return False


# winacl.py is the ONE explicit exception to the ctypes ban: Windows ACL reads only (advapi32/kernel32), never a process
_WINACL = "winacl.py"
_PROCESS_API = re.compile(r"^(createprocess\w*|shellexecute\w*|winexec|system|popen\w*|spawn\w*|exec[lv]p?e?|startfile|"
                          r"fork\w*|posix_spawn\w*|createremotethread\w*|ntcreate(user)?process\w*)$", re.I)
_DYNAMIC = {"getattr", "__getattr__", "__import__", "eval", "exec", "compile", "importlib", "globals", "vars"}


def _winacl_violations(source: str) -> list[str]:
    """Process-creation APIs (names, attributes, string constants), subprocess/os imports of spawners, dynamic lookups
    that could reach them, and any DLL other than advapi32/kernel32."""
    import ast

    bad: list[str] = []
    for node in ast.walk(ast.parse(source)):
        ident = None
        if isinstance(node, ast.Name):
            ident = node.id
        elif isinstance(node, ast.Attribute):
            ident = node.attr
        elif isinstance(node, ast.Constant) and isinstance(node.value, str):
            ident = node.value.strip()
        elif isinstance(node, (ast.Import, ast.ImportFrom)):
            mods = [a.name for a in node.names] if isinstance(node, ast.Import) else [node.module or ""]
            mods += [a.name for a in node.names]
            bad += [m for m in mods if m.split(".")[0] in {"subprocess", "multiprocessing", "importlib", "pty", "os"}
                    or _PROCESS_API.match(m)]
        if ident is not None and (_PROCESS_API.match(ident) or ident in _DYNAMIC):
            bad.append(ident)
        if (isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute) and node.func.attr in {"WinDLL", "CDLL",
                "OleDLL", "PyDLL", "LoadLibrary"}):
            arg = node.args[0] if node.args else None
            if not (isinstance(arg, ast.Constant) and arg.value in {"advapi32", "kernel32"}):
                bad.append(f"dll:{ast.unparse(arg) if arg is not None else '?'}")
    return bad


def test_p1c_ast_only_runner_spawns_processes() -> None:
    pkg = Path(runner.__file__).parent
    offenders = [py.name for py in pkg.glob("*.py")
                 if py.name not in {"runner.py", _WINACL} and _spawners(py.read_text(encoding="utf-8"))]
    assert not offenders, offenders
    winacl = pkg / _WINACL
    assert winacl.is_file()  # the allow-listed module exists, so the exception is not vacuous
    assert _winacl_violations(winacl.read_text(encoding="utf-8")) == []
    # [+] the winacl detector sees what it must refuse
    for src in ("k32.CreateProcessW(None)", "getattr(k32, 'WinExec')", "import subprocess", "from os import system",
                "ctypes.WinDLL('shell32')", "adv.ShellExecuteExW", "os.spawnv(0, 'x', [])"):
        assert _winacl_violations(src), src
    assert _spawners((pkg / "runner.py").read_text(encoding="utf-8"))  # [+] the detector sees the real spawner
    assert _spawners("import os\nos.system('x')")
