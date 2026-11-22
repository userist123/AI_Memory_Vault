"""aas_build.runner: fixed-argv docker invocation and the link-failing output reader (P1c, P7a, P8b, P8c, P8d)."""
from __future__ import annotations

import hashlib
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
    for needed in ("--network none", "--read-only", "--cap-drop ALL", "--security-opt no-new-privileges", "--user 1000:1000", "--pids-limit 512", "--memory 2g"):
        assert needed in joined, needed
    assert any(x.startswith("--tmpfs") for x in a)
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


def test_run_uses_no_shell(monkeypatch: pytest.MonkeyPatch, tmp_path: Path, out_dir: Path) -> None:
    seen: dict = {}

    class R:
        returncode = 0
        stdout = b""
        stderr = b""

    def fake_run(argv, **kw):  # type: ignore[no-untyped-def]
        seen["argv"], seen["kw"] = argv, kw
        return R()

    monkeypatch.setattr(runner.subprocess, "run", fake_run)
    runner.run_web_build(docker="C:/d/docker.exe", build_id="b0123456789abcdef", ctx=tmp_path, out=out_dir, image=IMAGE,
                         dest=tmp_path / "site", container_exists=lambda n: False)
    assert isinstance(seen["argv"], list)
    assert seen["kw"].get("shell", False) is False
    assert seen["kw"].get("timeout")


def test_nonzero_exit_fails_build(monkeypatch: pytest.MonkeyPatch, tmp_path: Path, out_dir: Path) -> None:
    class R:
        returncode = 1
        stdout = b""
        stderr = b"vite: boom"

    monkeypatch.setattr(runner.subprocess, "run", lambda *a, **k: R())
    with pytest.raises(BuildError, match="exit"):
        runner.run_web_build(docker="C:/d/docker.exe", build_id="b0123456789abcdef", ctx=tmp_path, out=out_dir, image=IMAGE,
                             dest=tmp_path / "site", container_exists=lambda n: False)
    assert not (tmp_path / "site").exists()  # nothing copied


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
    with pytest.raises(BuildError):
        runner.collect_output(out_dir, dest, **NOEXIST)
    assert not dest.exists() or not any(dest.rglob("*"))
    assert canary.read_text(encoding="utf-8") == "SECRET-CANARY"


def test_symlink_to_directory_refused(out_dir: Path, tmp_path: Path) -> None:
    if not make_link(out_dir / "dist" / "dlink", tmp_path, "symlink"):
        pytest.skip("cannot create symlink")
    with pytest.raises(BuildError):
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


def test_p1c_ast_only_runner_spawns_processes() -> None:
    pkg = Path(runner.__file__).parent
    offenders = [py.name for py in pkg.glob("*.py") if py.name != "runner.py" and _spawners(py.read_text(encoding="utf-8"))]
    assert not offenders, offenders
    assert _spawners((pkg / "runner.py").read_text(encoding="utf-8"))  # [+] the detector sees the real spawner
    assert _spawners("import os\nos.system('x')")
