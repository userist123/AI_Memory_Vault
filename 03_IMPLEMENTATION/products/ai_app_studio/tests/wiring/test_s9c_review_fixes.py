"""S9c review fixes: exe+zip pair bound by one build record (M1), cargo config planting / root ACL (M2), vendored crates
tamper check (M3), path remap (L1), non-stealing build probe (L2). Written before the code (red first)."""
from __future__ import annotations

import hashlib
import io
import os
import subprocess
import threading
import zipfile
from pathlib import Path
from types import SimpleNamespace
from typing import Any

import pytest
from aas_build import desktop, export, runner, service
from aas_build.runner import BuildError
from studio_core import activity
from studio_core.errors import Unavailable

from .test_s9b_export import built, two_step
from .test_s9c_desktop import CARGO, FAKE_EXE, droot, fake_build  # noqa: F401

WIN = os.name == "nt"


def _finished(studio: Any) -> dict[str, Any]:
    return [e for e in activity.list_events(studio, limit=200) if e["kind"] == "build.finished"][-1]["payload"]


# ---------------------------------------------------------------- M1: pair binding
def test_finished_event_records_both_hashes_and_the_build_dir(studio, droot: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    res = service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)
    pl = _finished(studio)
    exe = service.desktop_path(studio.settings, pid, res["buildId"])
    zp = service.desktop_dist_path(studio.settings, pid, res["buildId"])
    assert pl["artifactSha256"] == hashlib.sha256(exe.read_bytes()).hexdigest()
    assert pl["distSha256"] == hashlib.sha256(zp.read_bytes()).hexdigest() and pl["buildId"] == res["buildId"]


def test_pair_replace_failure_ships_only_the_matching_previous_pair(studio, droot: Path, monkeypatch: pytest.MonkeyPatch) -> None:  # type: ignore[no-untyped-def]
    """Reviewer's case: the second build's pair cannot be put in place -> nothing of it is exported, the first pair
    (exe A + zip A) stays whole and exportable."""
    pid = built(studio)
    a = service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)
    real = os.replace

    def flaky(src, dst, *a_, **k):  # type: ignore[no-untyped-def]
        if ".desktop-" in str(src):
            raise OSError("exe replace failed")
        return real(src, dst, *a_, **k)
    monkeypatch.setattr(service.os, "replace", flaky)
    with pytest.raises(service.BuildFailed):
        service.build_desktop(studio, pid, run_build=fake_build(exe=FAKE_EXE + b"-B"), cargo=CARGO)
    monkeypatch.setattr(service.os, "replace", real)
    art = export.prepare(studio, pid, "desktop")
    assert art.sha256 == a["exeSha256"] == hashlib.sha256(FAKE_EXE).hexdigest()
    with zipfile.ZipFile(io.BytesIO(art.embedded)) as z:
        assert "index.html" in z.namelist()
    assert not list(service.preview_base(studio.settings, pid).rglob(".desktop-*"))  # staging cleaned


def test_failed_pair_without_a_previous_build_exports_nothing(studio, droot: Path, monkeypatch: pytest.MonkeyPatch) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    real = os.replace

    def flaky(s, d, *a, **k):  # type: ignore[no-untyped-def]
        if ".desktop-" in str(s):
            raise OSError("x")
        return real(s, d, *a, **k)
    monkeypatch.setattr(service.os, "replace", flaky)
    with pytest.raises(service.BuildFailed):
        service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)
    with pytest.raises(Unavailable):
        export.prepare(studio, pid, "desktop")


def test_export_refuses_a_zip_that_is_not_the_recorded_one(studio, droot: Path, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    res = service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)
    assert two_step(studio, pid, "desktop", tmp_path / "exp")["sha256"] == res["exeSha256"]
    zp = service.desktop_dist_path(studio.settings, pid, res["buildId"])
    b = io.BytesIO()
    with zipfile.ZipFile(b, "w") as z:
        z.writestr("index.html", "<h1>other</h1>")
    zp.write_bytes(b.getvalue())  # a different (valid) dist zip next to the unchanged exe
    with pytest.raises(Unavailable, match="recorded"):
        export.prepare(studio, pid, "desktop")


# ---------------------------------------------------------------- M2: cargo config planting
def test_planted_cargo_config_in_a_parent_dir_refuses_the_build(studio, droot: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    for where in (droot, droot / "builds", droot.parent):
        (where / ".cargo").mkdir(parents=True, exist_ok=True)
        (where / ".cargo" / "config.toml").write_text("[build]\nrustc-wrapper='evil.exe'\n", encoding="utf-8")
        with pytest.raises(Unavailable, match=r"\.cargo"):
            service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)
        (where / ".cargo" / "config.toml").unlink()
    service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)  # empty .cargo dirs are harmless


def test_root_checks_are_repeated_right_before_cargo_runs(studio, droot: Path, monkeypatch: pytest.MonkeyPatch) -> None:  # type: ignore[no-untyped-def]
    """N3: a .cargo config planted (or an ACL loosened) during the long vendor hashing is caught before the spawn."""
    pid = built(studio)
    real_verify = desktop.verify_vendor
    ran: list[int] = []

    def plant(settings: Any, vendor: Path) -> None:
        real_verify(settings, vendor)
        (droot / ".cargo").mkdir(exist_ok=True)
        (droot / ".cargo" / "config.toml").write_text("[build]\nrustc-wrapper='evil.exe'\n", encoding="utf-8")
    monkeypatch.setattr(desktop, "verify_vendor", plant)
    with pytest.raises((Unavailable, service.BuildFailed), match=r"\.cargo"):
        service.build_desktop(studio, pid, run_build=lambda **kw: ran.append(1), cargo=CARGO)
    assert ran == []
    (droot / ".cargo" / "config.toml").unlink()
    calls: list[str] = []
    real_trust = desktop.assert_root_trusted

    def loosen(settings: Any, vendor: Path) -> None:
        real_verify(settings, vendor)
        calls.append("verify")
    monkeypatch.setattr(desktop, "verify_vendor", loosen)
    monkeypatch.setattr(desktop, "assert_root_trusted",
                        lambda root, **kw: (calls.append("acl"), real_trust(root, **kw))[1])
    service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)
    assert "acl" in calls[calls.index("verify"):]  # the ACL is checked again after the vendor hashing


def test_low_space_on_the_data_drive_points_to_aas_desktop_root(studio, monkeypatch: pytest.MonkeyPatch) -> None:  # type: ignore[no-untyped-def]
    """N4: AAS_DESKTOP_ROOT unset and < 10 GB free on the data drive -> a clear message, not a failure mid-build."""
    pid = built(studio)
    monkeypatch.delenv("AAS_DESKTOP_ROOT", raising=False)
    monkeypatch.setattr(service, "_desktop_drive_free", lambda path: 9 * 10**9)
    with pytest.raises(Unavailable, match="AAS_DESKTOP_ROOT"):
        service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO, free_bytes=lambda: 100 * 2**30)


def test_cargo_overrides_pin_wrapper_and_no_linker(tmp_path: Path) -> None:
    root = tmp_path / "desk"
    args = desktop.cargo_args(root, root / "vendor", root / "builds" / "dabc")
    assert args[0::2] == ["--config"] * (len(args) // 2)
    assert 'build.rustc-wrapper=""' in args and 'build.rustc-workspace-wrapper=""' in args
    assert "linker" not in " ".join(args)
    argv = runner.build_desktop_argv(cargo=CARGO, extra=args)
    assert argv[:8] == [CARGO, "build", "--release", "--offline", "--locked", "--quiet", "--color", "never"]
    assert argv[8:] == args


def test_cargo_home_has_a_studio_config(tmp_path: Path) -> None:
    desktop.write_cargo_home(tmp_path / "ch")
    txt = (tmp_path / "ch" / "config.toml").read_text(encoding="utf-8")
    assert 'rustc-wrapper = ""' in txt and "linker" not in txt and "offline = true" in txt


MODIFY = 0x1301BF
FULL = 0x1F01FF
USERS, ME, SYSTEM, ADMINS = "S-1-5-32-545", "S-1-5-21-1-2-3-1001", "S-1-5-18", "S-1-5-32-544"


def _fake_acl(owner: str, aces: dict[Path, list[tuple[str, int, int]]]):  # type: ignore[no-untyped-def]
    def reader(path: Path):  # type: ignore[no-untyped-def]
        return owner, aces.get(Path(path), [])
    return reader


def test_acl_check_refuses_a_root_writable_by_other_principals(tmp_path: Path) -> None:
    root = tmp_path / "r"
    root.mkdir()
    good = _fake_acl(ME, {root: [(ME, FULL, 0), (SYSTEM, FULL, 0), (ADMINS, FULL, 0), (USERS, 0x1200A9, 0)]})
    desktop.assert_root_trusted(root, reader=good, me=ME)  # read/execute for Users is fine
    bad = _fake_acl(ME, {root: [(ME, FULL, 0), (USERS, MODIFY, 0)]})
    with pytest.raises(BuildError, match="writable"):
        desktop.assert_root_trusted(root, reader=bad, me=ME)
    inh = _fake_acl(ME, {root: [(ME, FULL, 0), (USERS, MODIFY, 0x08 | 0x03)]})  # inherit-only: children writable
    with pytest.raises(BuildError, match="writable"):
        desktop.assert_root_trusted(root, reader=inh, me=ME)
    own = _fake_acl(USERS, {root: [(ME, FULL, 0)]})
    with pytest.raises(BuildError, match="owner"):
        desktop.assert_root_trusted(root, reader=own, me=ME)


def test_acl_check_covers_the_parent_chain(tmp_path: Path) -> None:
    root = tmp_path / "a" / "r"
    root.mkdir(parents=True)
    rd = _fake_acl(ME, {root: [(ME, FULL, 0)], root.parent: [(ME, FULL, 0), (USERS, FULL, 0)]})
    with pytest.raises(BuildError, match="writable"):
        desktop.assert_root_trusted(root, reader=rd, me=ME)  # a parent others can rename/replace the root through
    for create in (0x2, 0x4):  # N3: add-file / add-subdirectory on a parent (plant a .cargo config there) is untrusted
        rd2 = _fake_acl(ME, {root: [(ME, FULL, 0)], root.parent: [(ME, FULL, 0), (USERS, 0x1200A9 | create, 0)]})
        with pytest.raises(BuildError, match="parent"):
            desktop.assert_root_trusted(root, reader=rd2, me=ME)
    rd3 = _fake_acl(ME, {root: [(ME, FULL, 0)], root.parent: [(ME, FULL, 0), (USERS, 0x1200A9, 0)]})
    desktop.assert_root_trusted(root, reader=rd3, me=ME)  # read/execute on a parent is fine


AUTH_USERS = "S-1-5-11"


def test_acl_check_refuses_a_parent_others_can_modify(tmp_path: Path) -> None:
    r"""S9c review 2 F1: MODIFY (incl. DELETE) on a parent lets another account rename the root away and plant its own
    (e.g. D:\w grants Authenticated Users modify, so D:\w\_aas_desktop is refused)."""
    root = tmp_path / "w" / "_aas_desktop"
    root.mkdir(parents=True)
    owner_only = [(ME, FULL, 0), (SYSTEM, FULL, 0), (ADMINS, FULL, 0)]
    for mask in (MODIFY, 0x10000):  # modify, and DELETE alone
        rd = _fake_acl(ME, {root: owner_only, root.parent: [*owner_only, (AUTH_USERS, mask, 0x10)]})
        with pytest.raises(BuildError, match="parent"):
            desktop.assert_root_trusted(root, reader=rd, me=ME)
    io_only = _fake_acl(ME, {root: owner_only, root.parent: [*owner_only, (AUTH_USERS, MODIFY, 0x08 | 0x03)]})
    desktop.assert_root_trusted(root, reader=io_only, me=ME)  # inherit-only on the parent: root has no inheritance


def test_acl_check_accepts_an_owner_only_root_under_the_drive_root(tmp_path: Path) -> None:
    r"""D:\aas-desktop: the only checked dir is the root itself (the drive root is excluded)."""
    anchor = Path(tmp_path.anchor)
    root = anchor / "aas-desktop"
    seen: list[Path] = []

    def reader(p: Path):  # type: ignore[no-untyped-def]
        seen.append(Path(p))
        return ME, [(ME, FULL, 0x03), (SYSTEM, FULL, 0x03), (ADMINS, FULL, 0x03), (USERS, 0x1200A9, 0x03)]
    desktop.assert_root_trusted(root, reader=reader, me=ME)
    assert seen == ([root] if root.exists() else [])


def test_service_refuses_an_untrusted_root(studio, droot: Path, monkeypatch: pytest.MonkeyPatch) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    monkeypatch.setattr(desktop, "read_acl", lambda p: (ME, [(ME, FULL, 0), (USERS, MODIFY, 0)]))
    monkeypatch.setattr(desktop, "current_sid", lambda: ME)
    with pytest.raises(Unavailable, match="writable"):
        service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)


@pytest.mark.skipif(not WIN, reason="Windows ACLs")
def test_real_acl_secure_dir_then_check(tmp_path: Path) -> None:
    d = tmp_path / "sec"
    d.mkdir()
    subprocess.run(["icacls", str(d), "/grant", "*S-1-5-32-545:(OI)(CI)M"], check=True, capture_output=True)
    with pytest.raises(BuildError, match="writable"):
        desktop.assert_root_trusted(d)
    desktop.secure_dir(d)
    real = desktop.read_acl  # the real ACL of d; the test machine's temp parents are out of scope here (fake-ACL tests)
    desktop.assert_root_trusted(d, reader=lambda p: real(p) if Path(p) == d else (desktop.current_sid(), []))


# ---------------------------------------------------------------- M3: vendored crates tamper check
def test_vendor_manifest_is_outside_the_vendor_dir_and_detects_tamper(studio, droot: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    v = droot / "vendor"
    mp = desktop.manifest_path(studio.settings)
    assert not str(mp).startswith(str(v)) and mp.is_file()
    service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)  # untouched vendor: builds
    lib = v / "tauri-2.12.7" / "src" / "lib.rs"
    lib.write_text("pub fn evil() {}", encoding="utf-8")
    with pytest.raises(Unavailable, match="vendor"):
        service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)
    lib.write_text("pub fn a() {}", encoding="utf-8")
    extra = v / "tauri-2.12.7" / "build.rs"
    extra.write_text("fn main(){}", encoding="utf-8")  # a planted extra file
    with pytest.raises(Unavailable, match="vendor"):
        service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)
    extra.unlink()
    lib.unlink()  # a removed file
    with pytest.raises(Unavailable, match="vendor"):
        service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)


def test_missing_manifest_refuses_the_build(studio, droot: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    desktop.manifest_path(studio.settings).unlink()
    with pytest.raises(Unavailable, match="manifest"):
        service.build_desktop(studio, pid, run_build=fake_build(), cargo=CARGO)


# ---------------------------------------------------------------- L1: path remap
def test_remap_flags_cover_vendor_build_and_home(tmp_path: Path) -> None:
    root = tmp_path / "desk"
    args = desktop.cargo_args(root, root / "vendor", root / "builds" / "dabc")
    flags = [a for a in args if a.startswith("build.rustflags=")][0]
    assert flags.count("--remap-path-prefix=") >= 3
    assert f"{root / 'vendor'}=/vendor" in flags and f"{root}=/aas-desktop" in flags
    assert "builds" not in flags and "/src" not in flags  # the build dir is covered by root -> /aas-desktop (F2)
    assert f"{Path.home()}=~" in flags


def test_two_builds_get_identical_rustflags(tmp_path: Path) -> None:
    """S9c review 2 F2: no per-build remap, so the rustflags (part of cargo's fingerprint) never change across builds."""
    root = tmp_path / "desk"
    a = desktop.cargo_args(root, root / "vendor", root / "builds" / "d0000000000000001")
    b = desktop.cargo_args(root, root / "vendor", root / "builds" / "d0000000000000002")
    assert a == b
    assert root / "builds" / "d1" in desktop.leak_prefixes(root, root / "vendor", root / "builds" / "d1")  # scan kept


def test_a_build_dir_outside_the_root_is_refused(tmp_path: Path) -> None:
    root = tmp_path / "desk"
    with pytest.raises(BuildError, match="build dir"):
        desktop.cargo_args(root, root / "vendor", tmp_path / "elsewhere" / "d1")


def test_leak_scan_finds_host_paths_in_ascii_and_utf16(tmp_path: Path) -> None:
    pre = [tmp_path / "vendor", tmp_path / "b", Path("C:/Users/SomeName")]
    clean = b"MZ" + b"/vendor/tauri-2.12.7/src/lib.rs /src/build.rs ~/x"
    assert desktop.host_path_leaks(clean, pre) == []
    dirty = b"MZ" + str(pre[0] / "a.rs").encode() + b"\0" + "C:\\Users\\SomeName\\x".encode("utf-16le")
    assert len(desktop.host_path_leaks(dirty, pre)) == 2


def test_a_build_whose_exe_leaks_the_vendor_path_fails_closed(studio, droot: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    leaky = FAKE_EXE + str(droot / "vendor").encode()
    with pytest.raises(service.BuildFailed, match="host path"):
        service.build_desktop(studio, pid, run_build=fake_build(exe=leaky), cargo=CARGO)
    d = service.preview_base(studio.settings, pid) / "desktop"
    assert not d.exists() or not any(d.iterdir())


# ---------------------------------------------------------------- L2: the probe never steals the lock
def test_building_project_does_not_take_the_os_lock(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    s = SimpleNamespace(data_dir=tmp_path)
    with service.build_lock(s, "prj_" + "a" * 16):
        pass  # the lock file exists now

    def boom(fd: int) -> bool:
        raise AssertionError("probe took the lock")
    monkeypatch.setattr(service, "_try_os_lock", boom)
    assert service.building_project(s) is None


def test_probe_never_refuses_a_concurrent_build(tmp_path: Path) -> None:
    s = SimpleNamespace(data_dir=tmp_path)
    stop = threading.Event()
    seen: list[str | None] = []

    def hammer() -> None:
        while not stop.is_set():
            seen.append(service.building_project(s))
    t = threading.Thread(target=hammer, daemon=True)
    t.start()
    try:
        for _ in range(150):
            with service.build_lock(s, "prj_" + "c" * 16):  # raises Unavailable if the probe held the lock
                pass
    finally:
        stop.set()
        t.join(5)
    assert set(seen) <= {None, "prj_" + "c" * 16, "unknown"}  # "unknown" = lock taken, holder file not yet written
