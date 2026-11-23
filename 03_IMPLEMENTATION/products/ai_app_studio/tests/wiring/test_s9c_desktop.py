"""S9c - generated app as a Windows desktop app: studio-owned Tauri 2 wrapper (images/desktop-wrapper), host build
``cargo build --release --offline --locked`` against a vendored crate dir, unsigned .exe artifact with sha256,
MCP app_build/app_export target "desktop" (export approval bound to the sha256, same secret scan)."""
from __future__ import annotations

import hashlib
import json
import os
import time
from pathlib import Path
from typing import Any

import pytest
from aas_build import desktop, export, runner, service
from aas_build.runner import BuildError, DesktopResult
from studio_core import activity
from studio_core.errors import Unavailable

from .conftest import OWNER
from .test_s9b_export import KEY, built, site, two_step

CARGO = "C:/fake/toolchain/bin/cargo.exe"
FAKE_EXE = b"MZ\x90\x00" + b"\x00" * 60 + b"PE\x00\x00 fake exe"
SEEN: dict[str, Any] = {}


@pytest.fixture
def droot(tmp_path: Path, monkeypatch: pytest.MonkeyPatch, studio) -> Path:  # type: ignore[no-untyped-def]
    root = tmp_path / "desk"
    (root / "vendor" / "tauri-2.12.7" / "src").mkdir(parents=True)
    (root / "vendor" / "tauri-2.12.7" / "src" / "lib.rs").write_text("pub fn a() {}", encoding="utf-8")
    monkeypatch.setenv("AAS_DESKTOP_ROOT", str(root))
    desktop.secure_dir(root)  # owner-only root (S9c review M2/M3), then the studio-held vendor manifest
    real = desktop.read_acl  # real ACL of the root and below; the temp dir's parents (e.g. D:\w) are not under test
    monkeypatch.setattr(desktop, "read_acl", lambda p: real(p) if Path(p).is_relative_to(root) else (desktop.current_sid(), []))
    desktop.record_vendor(studio.settings, root / "vendor")
    return root


def fake_build(exe: bytes = FAKE_EXE, fail: str | None = None):  # type: ignore[no-untyped-def]
    def run(*, cargo: str, wrapper: Path, target_dir: Path, cargo_home: Path, tmp: Path, dest: Path, **kw: Any) -> DesktopResult:
        SEEN["wrapper"] = wrapper
        SEEN["files"] = sorted(p.relative_to(wrapper.parent).as_posix() for p in wrapper.parent.rglob("*") if p.is_file())
        SEEN["conf"] = json.loads((wrapper / "tauri.conf.json").read_text(encoding="utf-8"))
        SEEN["cargo_config"] = (wrapper / ".cargo" / "config.toml").read_text(encoding="utf-8")
        SEEN["template"] = {f: (wrapper / f).read_bytes() for f in desktop.TEMPLATE_FILES}
        SEEN["dist"] = {p.relative_to(wrapper.parent / "dist").as_posix(): p.read_bytes()
                        for p in (wrapper.parent / "dist").rglob("*") if p.is_file()}
        if fail:
            raise BuildError(fail)
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(exe)
        return DesktopResult(exe=dest, sha256=hashlib.sha256(exe).hexdigest(), size=len(exe), log_tail="Finished `release`")
    return run


# ---------------------------------------------------------------- argv / env / offline
def test_argv_is_fixed_offline_locked_release() -> None:
    argv = runner.build_desktop_argv(cargo=CARGO)
    assert argv == [CARGO, "build", "--release", "--offline", "--locked", "--quiet", "--color", "never"]
    with pytest.raises(BuildError):
        runner.build_desktop_argv(cargo="cargo")  # not absolute


def test_env_is_minimal_offline_and_proxy_sinkholed(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    for k, v in {"ANTHROPIC_API_KEY": "sk-ant-x", "GITHUB_TOKEN": "t", "HTTPS_PROXY": "http://corp:3128",
                 "RUSTFLAGS": "-C link-arg=/evil", "CARGO_REGISTRIES_X_TOKEN": "t", "RUSTC_WRAPPER": "evil.exe",
                 "USERPROFILE": "C:/Users/x", "AAS_OWNER_TOKEN": "z" * 40}.items():
        monkeypatch.setenv(k, v)
    env = runner.desktop_env(cargo=CARGO, cargo_home=tmp_path / "ch", target_dir=tmp_path / "t", tmp=tmp_path / "tmp")
    assert env["CARGO_NET_OFFLINE"] == "true"
    assert env["CARGO_HOME"] == str(tmp_path / "ch") and env["CARGO_TARGET_DIR"] == str(tmp_path / "t")
    assert env["TEMP"] == env["TMP"] == str(tmp_path / "tmp")
    assert env["HTTP_PROXY"] == env["HTTPS_PROXY"] == "http://127.0.0.1:9"  # any build-script fetch hits a dead port
    assert env["RUSTC"].endswith("rustc.exe") and Path(env["RUSTC"]).parent == Path(CARGO).parent
    for bad in ("ANTHROPIC_API_KEY", "GITHUB_TOKEN", "RUSTFLAGS", "CARGO_REGISTRIES_X_TOKEN", "RUSTC_WRAPPER",
                "USERPROFILE", "AAS_OWNER_TOKEN"):
        assert bad not in env, bad
    assert env["PATH"].split(os.pathsep)[0] == str(Path(CARGO).parent)


def test_cargo_config_replaces_crates_io_with_the_vendor_dir_and_is_offline(tmp_path: Path) -> None:
    text = desktop.cargo_config(tmp_path / "vendor")
    assert 'replace-with = "aas-vendored"' in text and "offline = true" in text
    assert (tmp_path / "vendor").as_posix() in text
    assert "[source.crates-io]" in text and "registry" not in text.replace("[source.crates-io]", "")


def test_run_desktop_build_uses_cwd_env_and_reads_the_exe_after_exit(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    wrapper, tgt = tmp_path / "w", tmp_path / "t"
    wrapper.mkdir()
    seen: dict[str, Any] = {}

    class P:
        def __init__(self, argv, **kw):  # type: ignore[no-untyped-def]
            import io
            seen["argv"], seen["kw"] = argv, kw
            self.stdout, self.stderr, self.returncode, self.pid = io.BytesIO(b""), io.BytesIO(b"done"), None, 1

        def wait(self, timeout=None):  # type: ignore[no-untyped-def]
            exe = tgt / "release" / "aas-app.exe"
            exe.parent.mkdir(parents=True, exist_ok=True)
            exe.write_bytes(FAKE_EXE)  # written by the (fake) build, read only after wait() returned
            self.returncode = 0
            return 0

    monkeypatch.setattr(runner.subprocess, "Popen", P)
    dest = tmp_path / "out" / "app.exe"
    res = runner.run_desktop_build(cargo=CARGO, wrapper=wrapper, target_dir=tgt, cargo_home=tmp_path / "ch",
                                   tmp=tmp_path / "tmp", dest=dest)
    assert seen["argv"][1:5] == ["build", "--release", "--offline", "--locked"]
    assert seen["kw"]["cwd"] == str(wrapper) and seen["kw"]["shell"] is False
    assert seen["kw"]["env"]["CARGO_NET_OFFLINE"] == "true"
    assert res.sha256 == hashlib.sha256(FAKE_EXE).hexdigest() and dest.read_bytes() == FAKE_EXE


def test_collect_exe_refuses_links_non_pe_and_oversize(tmp_path: Path) -> None:
    exe = tmp_path / "release" / "aas-app.exe"
    exe.parent.mkdir()
    exe.write_bytes(b"not a pe")
    with pytest.raises(BuildError, match="PE"):
        runner.collect_exe(exe, tmp_path / "d.exe")
    exe.write_bytes(FAKE_EXE)
    with pytest.raises(BuildError, match="over"):
        runner.collect_exe(exe, tmp_path / "d.exe", max_bytes=10)
    real = tmp_path / "real.exe"
    real.write_bytes(FAKE_EXE)
    exe.unlink()
    try:
        os.symlink(real, exe)
    except OSError:
        pytest.skip("symlinks need developer mode")
    with pytest.raises(BuildError, match="link"):
        runner.collect_exe(exe, tmp_path / "d.exe")


# ---------------------------------------------------------------- template / materialise
def test_template_is_pinned_and_has_no_ipc_surface() -> None:
    t = desktop.TEMPLATE
    cargo = (t / "Cargo.toml").read_text(encoding="utf-8")
    assert 'tauri = { version = "=2.12.7"' in cargo and 'tauri-build = { version = "=2.7.6"' in cargo
    assert "plugin" not in cargo
    assert (t / "Cargo.lock").is_file()
    assert ".commands(&[])" in (t / "build.rs").read_text(encoding="utf-8")
    main = (t / "src" / "main.rs").read_text(encoding="utf-8")
    assert "invoke_handler" not in main and "plugin" not in main
    assert not (t / "capabilities").exists() and not (t / "tauri.conf.json").exists()  # config is studio-written


def test_conf_identifier_csp_no_capabilities_and_fixed_dist() -> None:
    conf = desktop.tauri_conf("prj_0123456789abcdef", 'My <App> "x"')
    assert conf["identifier"] == "dev.aiappstudio.p0123456789abcdef"
    assert conf["build"] == {"frontendDist": "../dist"}
    sec = conf["app"]["security"]
    assert sec["csp"] == desktop.CSP and sec["capabilities"] == [] and sec["dangerousDisableAssetCspModification"] is False
    assert "connect-src 'self'" in desktop.CSP and "object-src 'none'" in desktop.CSP
    assert conf["app"]["withGlobalTauri"] is False
    assert conf["bundle"]["active"] is False
    assert conf["productName"] == "My App x"
    win = conf["app"]["windows"][0]
    assert "--proxy-server=http://127.0.0.1:9" in win["additionalBrowserArgs"]
    with pytest.raises(BuildError):
        desktop.tauri_conf("prj_xyz", "n")


def test_build_desktop_needs_a_web_build(studio, droot: Path) -> None:  # type: ignore[no-untyped-def]
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    with pytest.raises(Unavailable, match="web"):
        service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)


def test_agent_files_cannot_override_the_template(studio, droot: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    s = site(studio, pid)
    (s / "assets").mkdir(exist_ok=True)
    (s / "assets" / "app.js").write_text("console.log(1)", encoding="utf-8")
    service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)
    for f in desktop.TEMPLATE_FILES:
        assert SEEN["template"][f] == (desktop.TEMPLATE / f).read_bytes(), f
    assert set(SEEN["dist"]) == {"index.html", "assets/app.js"}
    assert SEEN["conf"]["identifier"] == "dev.aiappstudio.p" + pid[4:]
    assert (droot / "vendor").as_posix() in SEEN["cargo_config"]
    assert not any(f.startswith("dist/") and f.endswith((".rs", ".toml")) for f in SEEN["files"])
    # a dist file with a name outside the web allow-list (e.g. a Rust or TOML file) fails closed
    (s / "build.rs").write_text("fn main(){}", encoding="utf-8")
    with pytest.raises(service.BuildFailed, match="not allowed"):
        service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)


def test_artifact_hash_event_and_sidecar(studio, droot: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    res = service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)
    sha = hashlib.sha256(FAKE_EXE).hexdigest()
    assert res["exeSha256"] == sha and res["sizeBytes"] == len(FAKE_EXE)
    assert res["appId"] == "dev.aiappstudio.p" + pid[4:]
    assert service.desktop_path(studio.settings, pid, res["buildId"]).read_bytes() == FAKE_EXE
    ev = [e for e in activity.list_events(studio, limit=200) if e["kind"] == "build.finished"][-1]
    assert ev["payload"]["target"] == "desktop" and ev["payload"]["artifactSha256"] == sha
    started = [e for e in activity.list_events(studio, limit=200) if e["kind"] == "build.started"][-1]
    assert started["payload"]["target"] == "desktop"
    assert not SEEN["wrapper"].exists()  # fresh build dir removed


def test_failed_build_records_events_and_keeps_no_exe(studio, droot: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    with pytest.raises(service.BuildFailed):
        service.build_desktop(studio, pid, run_build=fake_build(fail="linker error"), cargo=CARGO)
    ddir = service.preview_base(studio.settings, pid) / "desktop"
    assert not ddir.exists() or not any(ddir.iterdir())  # no exe / zip of a failed build
    ev = [e for e in activity.list_events(studio, limit=200) if e["kind"] == "build.finished"][-1]
    assert ev["payload"]["status"] == "failed" and ev["payload"]["target"] == "desktop"


def test_missing_vendor_dir_is_unavailable(studio, tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:  # type: ignore[no-untyped-def]
    monkeypatch.setenv("AAS_DESKTOP_ROOT", str(tmp_path / "empty"))
    pid = built(studio)
    with pytest.raises(Unavailable, match="vendor"):
        service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)


def test_disk_gate_applies(studio, droot: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    with pytest.raises(Unavailable, match="5 GiB"):
        service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO, free_bytes=lambda: 1)


# ---------------------------------------------------------------- export
def test_desktop_export_approval_bound_to_sha_and_scan(studio, droot: Path, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    with pytest.raises(Unavailable):
        export.prepare(studio, pid, "desktop")
    built_res = service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)
    res = two_step(studio, pid, "desktop", tmp_path / "exp")
    assert res["path"].endswith(".exe") and res["sha256"] == hashlib.sha256(FAKE_EXE).hexdigest()
    assert Path(res["path"]).read_bytes() == FAKE_EXE
    # an exe swapped after the build no longer matches the recorded sha256
    service.desktop_path(studio.settings, pid, built_res["buildId"]).write_bytes(FAKE_EXE + b"x")
    with pytest.raises(Unavailable, match="recorded"):
        export.prepare(studio, pid, "desktop")


def test_desktop_export_scans_the_embedded_dist(studio, droot: Path, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    """Tauri embeds the dist brotli-compressed, so the raw exe hides a planted key: the scan also covers the exact
    dist the exe was built from (sidecar zip bound by sha256)."""
    pid = built(studio)
    (site(studio, pid) / "key.txt").write_text(KEY, encoding="utf-8")
    service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)
    with pytest.raises(export.ExportBlocked):
        export.export(studio, OWNER, pid, "desktop", dest_root=tmp_path)


def test_mcp_tools_accept_desktop(studio, droot: Path, monkeypatch: pytest.MonkeyPatch) -> None:  # type: ignore[no-untyped-def]
    from .test_s9b_sight import load_server
    mod = load_server(monkeypatch, studio)
    pid = built(studio)
    called: dict[str, Any] = {}
    monkeypatch.setattr(mod.build_service, "build_desktop", lambda st, p, **kw: called.setdefault("r", {"ok": True, "p": p}))
    refused = mod.app_build(projectId=pid, target="desktop")  # plan-gated like web/android
    assert refused.is_error is True and "plan" in refused.content[0].text and "r" not in called
    monkeypatch.setattr(mod, "plan_approved", lambda _pid: True)
    assert mod.app_build(projectId=pid, target="desktop").is_error is False
    assert called["r"]["p"] == pid
    assert "desktop" in export.TARGETS


# ---------------------------------------------------------------- real build (local gate, Windows + cargo + vendor)
REAL_ROOT = os.environ.get("AAS_DESKTOP_REAL_ROOT", "")


@pytest.mark.skipif(not REAL_ROOT or os.name != "nt", reason="set AAS_DESKTOP_REAL_ROOT to a desktop root with vendor/")
def test_real_blank_template_build(tmp_path: Path) -> None:
    root = Path(REAL_ROOT)
    cargo = runner.resolve_cargo()
    wrapper = root / "builds" / "dreal"
    dist = tmp_path / "dist"
    dist.mkdir()
    (dist / "index.html").write_text("<!doctype html><title>blank</title><h1>Hello</h1>", encoding="utf-8")
    import shutil
    shutil.rmtree(wrapper.parent / "dreal-root", ignore_errors=True)
    w = desktop.materialise(wrapper.parent / "dreal-root", dist, "prj_0123456789abcdef", "Blank", root / "vendor")
    t0 = time.monotonic()
    res = runner.run_desktop_build(cargo=cargo, wrapper=w, target_dir=root / "target", cargo_home=root / "cargo-home",
                                   tmp=root / "tmp", dest=tmp_path / "app.exe")
    print(f"REAL desktop build {time.monotonic() - t0:.1f}s size={res.size} sha={res.sha256}")
    assert res.size > 100_000 and (tmp_path / "app.exe").read_bytes()[:2] == b"MZ"
