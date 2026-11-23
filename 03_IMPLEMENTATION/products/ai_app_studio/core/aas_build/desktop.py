"""Windows desktop wrapper (S9c): the studio-owned Tauri 2 template + the project's built web dist -> fresh build dir.

The agent supplies only the web dist (already validated by the web build reader); it never supplies Rust, Cargo
files, tauri.conf.json, capabilities or cargo config. Layout of one build dir (``<desktop root>/builds/d<hex>``):

    wrapper/   TEMPLATE_FILES copied byte-for-byte from images/desktop-wrapper, plus two studio-written files:
               tauri.conf.json (identifier dev.aiappstudio.p<id16>, kit CSP, no capabilities, frontendDist ../dist)
               .cargo/config.toml (crates-io replaced by the vendored, checksum-locked crate dir; net.offline)
    dist/      the current preview site, re-validated with the web output reader (allow-listed names/extensions,
               no links, no hard links)

Desktop root = ``AAS_DESKTOP_ROOT`` (absolute) or ``<data root>/desktop``; it holds ``vendor/`` (``cargo vendor``
once, see docs/slices/S9c.md), ``target/`` (shared cargo target dir: dependencies compile once), ``cargo-home/``
(studio-owned, own config.toml: the owner's ~/.cargo config and credentials are never read) and ``tmp/``.
The root must be owner-only (``assert_root_trusted``), no ``.cargo`` config may exist in a parent of the build dir,
and ``vendor/`` is checked against a studio-held per-file sha256 manifest (under the data root, not in the vendor
dir) before every build.
This module starts no process (P1c); the build itself is ``runner.run_desktop_build``.
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from collections.abc import Callable
from pathlib import Path
from typing import Any

from . import runner
from .runner import BuildError, BuildLimits

PRODUCT = Path(__file__).resolve().parents[2]
TEMPLATE = PRODUCT / "images" / "desktop-wrapper"
TEMPLATE_FILES = ("Cargo.toml", "Cargo.lock", "build.rs", "src/main.rs", "icons/32x32.png", "icons/128x128.png",
                  "icons/icon.ico")
# = kit/index.html meta CSP (the preview server's header minus frame-ancestors: the app is the top-level document)
CSP = ("default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; "
       "font-src 'self'; connect-src 'self'; media-src 'self' blob:; worker-src 'self'; manifest-src 'self'; "
       "frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'")
# same WebView2 flags as the studio window (B9): no proxy-less egress, no WebRTC host candidates
BROWSER_ARGS = ("--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection --proxy-server=http://127.0.0.1:9 "
                "--force-webrtc-ip-handling-policy=disable_non_proxied_udp")
_PID = re.compile(r"^prj_[0-9a-f]{16}$")


def desktop_root(settings: Any) -> Path:
    raw = os.environ.get("AAS_DESKTOP_ROOT", "")
    if raw:
        p = Path(raw)
        if not p.is_absolute() or raw.startswith(("\\\\", "//")):
            raise BuildError("AAS_DESKTOP_ROOT must be an absolute local path")
        return p
    return Path(settings.data_dir) / "desktop"


def app_id(project_id: str) -> str:
    return runner.app_id_for(project_id)


def product_name(name: str) -> str:
    s = re.sub(r"[^A-Za-z0-9 _-]+", "", name or "")
    s = re.sub(r"\s+", " ", s).strip()[:40]
    return s or "App"


def tauri_conf(project_id: str, name: str) -> dict[str, Any]:
    if not isinstance(project_id, str) or not _PID.match(project_id):
        raise BuildError("invalid project id")
    title = product_name(name)
    return {
        "$schema": "https://schema.tauri.app/config/2",
        "productName": title,
        "version": "0.1.0",
        "identifier": app_id(project_id),
        "build": {"frontendDist": "../dist"},
        "app": {
            "withGlobalTauri": False,
            "windows": [{"label": "main", "title": title, "width": 1200, "height": 800, "minWidth": 360,
                         "minHeight": 480, "additionalBrowserArgs": BROWSER_ARGS}],
            "security": {"csp": CSP, "dangerousDisableAssetCspModification": False, "capabilities": []},
        },
        "bundle": {"active": False, "icon": ["icons/32x32.png", "icons/128x128.png", "icons/icon.ico"]},
    }


def cargo_config(vendor: Path) -> str:
    v = Path(vendor).as_posix()
    if any(c in v for c in ('"', "\n", "\r", "\\")):
        raise BuildError("vendor path contains a character not allowed in the cargo config")
    return ("# studio-written (S9c): crates come only from the vendored, checksum-locked directory\n"
            "[source.crates-io]\nreplace-with = \"aas-vendored\"\n\n"
            f"[source.aas-vendored]\ndirectory = \"{v}\"\n\n"
            "[net]\noffline = true\n\n[build]\nincremental = false\n")


def _copy_template(dst: Path) -> None:
    for rel in TEMPLATE_FILES:
        src = TEMPLATE / rel
        st = os.lstat(src)
        if runner._is_link_like(st) or not src.is_file():  # noqa: SLF001
            raise BuildError(f"desktop template file is not a regular file: {rel}")
        t = dst / rel
        t.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src, t)


def copy_dist(site: Path, dst: Path, limits: BuildLimits | None = None) -> list[str]:
    """Re-validate the built site with the web output reader rules and copy it (no links followed)."""
    limits = limits or BuildLimits()
    files = runner._scan(Path(site), limits)  # noqa: SLF001 - same allow-list as the web build output
    if "index.html" not in {r for r, _ in files}:
        raise BuildError("the web build has no index.html")
    dst.mkdir(parents=True)
    for rel, st in files:
        data = runner._read_regular(Path(site) / rel, st, limits.max_file_bytes)  # noqa: SLF001
        t = dst / rel
        t.parent.mkdir(parents=True, exist_ok=True)
        t.write_bytes(data)
    return sorted(r for r, _ in files)


def materialise(build_dir: Path, site: Path, project_id: str, name: str, vendor: Path) -> Path:
    """Fresh build dir -> returns the wrapper dir (cargo's cwd)."""
    if build_dir.exists():
        raise BuildError("build dir must not exist")
    wrapper = build_dir / "wrapper"
    _copy_template(wrapper)
    conf = tauri_conf(project_id, name)
    (wrapper / "tauri.conf.json").write_text(json.dumps(conf, indent=2), encoding="utf-8")
    (wrapper / ".cargo").mkdir()
    (wrapper / ".cargo" / "config.toml").write_text(cargo_config(vendor), encoding="utf-8")
    copy_dist(site, build_dir / "dist")
    return wrapper


def dist_zip(root: Path) -> bytes:
    """Deterministic zip of the dist the exe embeds (export scans it: Tauri stores the assets brotli-compressed)."""
    import io
    import zipfile
    files = runner._scan(Path(root), BuildLimits())  # noqa: SLF001
    b = io.BytesIO()
    with zipfile.ZipFile(b, "w") as z:
        for rel, st in sorted(files):
            zi = zipfile.ZipInfo(rel, (1980, 1, 1, 0, 0, 0))
            zi.compress_type = zipfile.ZIP_DEFLATED
            zi.create_system = 3
            zi.external_attr = (0o100644 & 0xFFFF) << 16
            z.writestr(zi, runner._read_regular(Path(root) / rel, st, BuildLimits().max_file_bytes), compresslevel=9)  # noqa: SLF001
    return b.getvalue()


# ---- S9c review: owner-only root, cargo config planting, vendored crates manifest, path remap ----
_WRITE_ANY = 0x2 | 0x4 | 0x10 | 0x40 | 0x100 | 0x10000 | 0x40000 | 0x80000 | 0x40000000 | 0x10000000
_REPLACE = 0x40 | 0x10000 | 0x40000 | 0x80000 | 0x10000000  # delete-child / DELETE / write-dac / write-owner / generic-all:
# swap the dir (DELETE on a parent = rename it away with the root inside, then plant another one; S9c review 2 F1)
_PARENT_UNSAFE = _REPLACE | 0x2 | 0x4  # + add-file / add-subdirectory: plant a .cargo config in a parent (N3)
_INHERIT_ONLY = 0x08
_ALWAYS_TRUSTED = {"S-1-5-18", "S-1-5-32-544", "S-1-3-0", "S-1-3-4",
                   "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"}  # SYSTEM, Administrators, CREATOR OWNER, OWNER RIGHTS, TrustedInstaller
Ace = tuple[str, int, int]  # (sid, mask, ace flags) of an ACCESS_ALLOWED ACE


def _win():  # type: ignore[no-untyped-def]
    import ctypes
    from ctypes import wintypes
    adv = ctypes.WinDLL("advapi32", use_last_error=True)
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    return ctypes, wintypes, adv, k32


def _sid_str(ct, wt, adv, k32, psid) -> str:  # type: ignore[no-untyped-def]
    adv.ConvertSidToStringSidW.argtypes = [ct.c_void_p, ct.POINTER(wt.LPWSTR)]
    out = wt.LPWSTR()
    if not adv.ConvertSidToStringSidW(psid, ct.byref(out)):
        raise OSError("ConvertSidToStringSid failed")
    try:
        return str(out.value)
    finally:
        k32.LocalFree.argtypes = [ct.c_void_p]
        k32.LocalFree(ct.cast(out, ct.c_void_p))


def current_sid() -> str:
    """SID of the process token's user (Windows)."""
    ct, wt, adv, k32 = _win()
    k32.GetCurrentProcess.restype = ct.c_void_p
    adv.OpenProcessToken.argtypes = [ct.c_void_p, wt.DWORD, ct.POINTER(ct.c_void_p)]
    tok = ct.c_void_p()
    if not adv.OpenProcessToken(k32.GetCurrentProcess(), 0x8, ct.byref(tok)):  # TOKEN_QUERY
        raise OSError("OpenProcessToken failed")
    try:
        adv.GetTokenInformation.argtypes = [ct.c_void_p, ct.c_int, ct.c_void_p, wt.DWORD, ct.POINTER(wt.DWORD)]
        n = wt.DWORD()
        adv.GetTokenInformation(tok, 1, None, 0, ct.byref(n))  # TokenUser
        buf = ct.create_string_buffer(n.value)
        if not adv.GetTokenInformation(tok, 1, buf, n, ct.byref(n)):
            raise OSError("GetTokenInformation failed")
        psid = ct.cast(buf, ct.POINTER(ct.c_void_p))[0]
        return _sid_str(ct, wt, adv, k32, psid)
    finally:
        k32.CloseHandle.argtypes = [ct.c_void_p]
        k32.CloseHandle(tok)


def read_acl(path: Path) -> tuple[str, list[Ace]]:
    """(owner SID, ACCESS_ALLOWED aces) of a file system object (Windows, ctypes). A NULL DACL = Everyone full."""
    ct, wt, adv, k32 = _win()
    adv.GetNamedSecurityInfoW.argtypes = [wt.LPCWSTR, ct.c_int, wt.DWORD, ct.POINTER(ct.c_void_p), ct.POINTER(ct.c_void_p),
                                          ct.POINTER(ct.c_void_p), ct.POINTER(ct.c_void_p), ct.POINTER(ct.c_void_p)]
    adv.GetNamedSecurityInfoW.restype = wt.DWORD
    owner, dacl, sd = ct.c_void_p(), ct.c_void_p(), ct.c_void_p()
    rc = adv.GetNamedSecurityInfoW(str(path), 1, 0x1 | 0x4, ct.byref(owner), None, ct.byref(dacl), None, ct.byref(sd))
    if rc != 0:
        raise OSError(f"GetNamedSecurityInfo failed ({rc}) for {path}")
    try:
        osid = _sid_str(ct, wt, adv, k32, owner)
        if not dacl.value:
            return osid, [("S-1-1-0", 0x1F01FF, 0)]

        class AclSize(ct.Structure):
            _fields_ = [("AceCount", wt.DWORD), ("AclBytesInUse", wt.DWORD), ("AclBytesFree", wt.DWORD)]
        info = AclSize()
        adv.GetAclInformation.argtypes = [ct.c_void_p, ct.c_void_p, wt.DWORD, ct.c_int]
        if not adv.GetAclInformation(dacl, ct.byref(info), ct.sizeof(info), 2):
            raise OSError("GetAclInformation failed")
        aces: list[Ace] = []
        adv.GetAce.argtypes = [ct.c_void_p, wt.DWORD, ct.POINTER(ct.c_void_p)]
        for i in range(info.AceCount):
            pace = ct.c_void_p()
            if not adv.GetAce(dacl, i, ct.byref(pace)):
                raise OSError("GetAce failed")
            hdr = ct.string_at(pace.value, 8)
            if hdr[0] != 0:  # only ACCESS_ALLOWED_ACE_TYPE grants rights
                continue
            mask = int.from_bytes(hdr[4:8], "little")
            aces.append((_sid_str(ct, wt, adv, k32, pace.value + 8), mask, hdr[1]))
        return osid, aces
    finally:
        k32.LocalFree.argtypes = [ct.c_void_p]
        k32.LocalFree(sd)


def assert_root_trusted(root: Path, *, reader: Callable[[Path], tuple[str, list[Ace]]] | None = None,
                        me: str | None = None) -> None:
    """The desktop root (holds vendor/target/cargo-home) must be writable only by the current user, SYSTEM and
    Administrators: root itself (incl. inherit-only aces: they reach the children) and every parent up to, not
    including, the drive root (delete-child / DELETE / write-dac / write-owner would let another account swap the root;
    so D:\w, where Authenticated Users have modify, is refused: use an owner-only dir under the drive root); add-file /
    add-subdirectory on a parent is refused too (a planted ``.cargo`` config there would be merged by cargo).
    No-op off Windows unless a ``reader`` is given."""
    if reader is None:
        if os.name != "nt":
            return
        reader = read_acl
    me = me or (current_sid() if os.name == "nt" else "")
    trusted = _ALWAYS_TRUSTED | {me}
    root = Path(root)
    chain = [root] + [p for p in root.parents if p != p.parent]
    for i, d in enumerate(chain):
        if not d.exists():
            continue
        try:
            owner, aces = reader(d)
        except OSError as e:
            raise BuildError(f"cannot read the ACL of {d}: {e}") from e
        if owner not in trusted:
            raise BuildError(f"the desktop build root chain has an untrusted owner ({d}); fix its owner")
        for sid, mask, flags in aces:
            if sid in trusted:
                continue
            if i == 0 and mask & _WRITE_ANY:
                raise BuildError(f"the desktop build root is writable by another principal ({sid} on {d}): "
                                 "restrict it to the owner, SYSTEM and Administrators (python -m aas_build.desktop secure)")
            if i > 0 and not flags & _INHERIT_ONLY and mask & _PARENT_UNSAFE:
                raise BuildError(f"a parent of the desktop build root is writable by another principal ({sid} on {d}): "
                                 "it could be replaced; use an owner-only location")


def secure_dir(path: Path) -> None:
    """Make ``path`` owner-only (reversible, our own folder): no inheritance, full control for the current user, SYSTEM and
    Administrators only; existing children are reset to inherit from it. No-op off Windows."""
    if os.name != "nt":
        return
    path = Path(path)
    path.mkdir(parents=True, exist_ok=True)
    me = current_sid()

    def run(argv: list[str]) -> None:
        subprocess.run(argv, check=True, capture_output=True, shell=False, stdin=subprocess.DEVNULL, timeout=1800)  # noqa: S603
    run(["icacls", str(path), "/reset"])  # drop explicit aces (the dir itself only), then no inheritance + 3 grants
    run(["icacls", str(path), "/inheritance:r", "/grant:r", f"*{me}:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F",
         "*S-1-5-32-544:(OI)(CI)F"])
    if any(path.iterdir()):
        run(["icacls", str(path / "*"), "/reset", "/T", "/C", "/Q"])


def refuse_planted_cargo_config(build_dir: Path) -> None:
    """Cargo merges ``.cargo/config(.toml)`` of every parent of its cwd: refuse when any parent of the build dir has one
    (the studio's own config sits in wrapper/.cargo and in CARGO_HOME)."""
    for p in [Path(build_dir), *Path(build_dir).parents]:
        c = p / ".cargo"
        if not os.path.lexists(c):
            continue
        if c.is_symlink() or (c.exists() and not c.is_dir()):
            raise BuildError(f"a .cargo link or file exists at {p}: refusing to build")
        for n in ("config.toml", "config"):
            if os.path.lexists(c / n):
                raise BuildError(f"a .cargo/{n} exists in a parent of the build dir ({p}): cargo would merge it; "
                                 "remove it or use another AAS_DESKTOP_ROOT")


def write_cargo_home(cargo_home: Path) -> None:
    """Studio-owned CARGO_HOME config (rewritten each build): offline, no rustc wrapper, no linker override."""
    cargo_home = Path(cargo_home)
    if cargo_home.is_symlink():
        raise BuildError("cargo-home is a link")
    cargo_home.mkdir(parents=True, exist_ok=True)
    (cargo_home / "config.toml").write_text(
        "# studio-owned (S9c)\n[net]\noffline = true\n\n[build]\nrustc-wrapper = \"\"\nrustc-workspace-wrapper = \"\"\n",
        encoding="utf-8")


def _prefix_pairs(root: Path, vendor: Path) -> list[tuple[str, str]]:
    # general first: rustc applies the last matching mapping. No per-build entry (S9c review 2 F2): the build dir sits
    # under root (-> /aas-desktop/builds/<id>), so the rustflags, part of cargo's fingerprint, stay the same per build.
    return [(str(Path.home()), "~"), (str(root), "/aas-desktop"), (str(root / "target"), "/target"),
            (str(vendor), "/vendor")]


def cargo_args(root: Path, vendor: Path, build_dir: Path) -> list[str]:
    """``--config k=v`` overrides (highest cargo precedence): no rustc wrapper, host path prefixes remapped. The build
    dir must be under ``root`` (covered by its mapping); the exe leak scan still checks it."""
    if not Path(build_dir).is_relative_to(Path(root)):
        raise BuildError("the build dir is not under the desktop root: its path would not be remapped")
    flags: list[str] = []
    for src, dst in _prefix_pairs(Path(root), Path(vendor)):
        if "'" in src or "\n" in src:
            raise BuildError("a build path contains a character not allowed in the cargo config")
        for form in dict.fromkeys((src, src.replace("\\", "/"))):
            flags.append(f"--remap-path-prefix={form}={dst}")
    arr = ", ".join(f"'{f}'" for f in flags)
    return ["--config", 'build.rustc-wrapper=""', "--config", 'build.rustc-workspace-wrapper=""',
            "--config", f"build.rustflags=[{arr}]"]


def host_path_leaks(data: bytes, prefixes: list[Path]) -> list[str]:
    """Host path prefixes (either slash form, ascii or utf-16le, case-insensitive) found in ``data``."""
    low = data.lower()
    found: list[str] = []
    for p in prefixes:
        s = str(p)
        for form in dict.fromkeys((s, s.replace("\\", "/"))):
            f = form.lower()
            if f.encode("utf-8", "replace") in low or f.encode("utf-16le") in low:
                found.append(s)
                break
    return found


def leak_prefixes(root: Path, vendor: Path, build_dir: Path) -> list[Path]:
    return [Path(vendor), Path(build_dir), Path(root), Path.home()]


def manifest_path(settings: Any) -> Path:
    return Path(settings.data_dir) / "desktop-vendor.manifest.json"


def vendor_hashes(vendor: Path) -> dict[str, str]:
    out: dict[str, str] = {}
    for dp, dns, fns in os.walk(vendor):
        for n in dns + fns:
            if runner._is_link_like(os.lstat(os.path.join(dp, n))):  # noqa: SLF001
                raise BuildError(f"link or reparse point in the vendor dir: {os.path.join(dp, n)}")
        for n in fns:
            fp = Path(dp) / n
            h = hashlib.sha256()
            with open(fp, "rb") as f:
                for chunk in iter(lambda: f.read(1 << 20), b""):
                    h.update(chunk)
            out[fp.relative_to(vendor).as_posix()] = h.hexdigest()
    return out


def _tree_sha(files: dict[str, str]) -> str:
    return hashlib.sha256("\n".join(f"{k}\0{v}" for k, v in sorted(files.items())).encode()).hexdigest()


def record_vendor(settings: Any, vendor: Path) -> str:
    """Hash the vendor tree once (right after ``cargo vendor``) into the studio-held manifest; returns the tree sha256."""
    files = vendor_hashes(Path(vendor))
    mp = manifest_path(settings)
    mp.parent.mkdir(parents=True, exist_ok=True)
    tmp = mp.with_name(mp.name + ".partial")
    tmp.write_text(json.dumps({"vendor": str(vendor), "treeSha256": _tree_sha(files), "files": files}), encoding="utf-8")
    os.replace(tmp, mp)
    return _tree_sha(files)


def verify_vendor(settings: Any, vendor: Path) -> None:
    mp = manifest_path(settings)
    try:
        rec = json.loads(mp.read_text(encoding="utf-8"))
        want: dict[str, str] = rec["files"]
    except (OSError, ValueError, KeyError) as e:
        raise BuildError(f"no vendor manifest ({mp}): run `python -m aas_build.desktop record-vendor` after cargo vendor") from e
    if rec.get("vendor") != str(vendor):
        raise BuildError("the vendor manifest was recorded for another vendor dir: re-run record-vendor")
    got = vendor_hashes(Path(vendor))
    if got != want:
        diff = sorted((set(got) ^ set(want)) | {k for k in got.keys() & want.keys() if got[k] != want[k]})
        raise BuildError(f"the vendored crates differ from the recorded manifest ({len(diff)} files, e.g. {diff[0]}): "
                         "refusing to build")


def main(argv: list[str] | None = None) -> int:
    """``python -m aas_build.desktop secure|record-vendor``: one-time owner steps for the desktop root."""
    from studio_core.config import load_settings
    args = list(sys.argv[1:] if argv is None else argv)
    settings = load_settings()
    root = desktop_root(settings)
    if args[:1] == ["secure"]:
        secure_dir(root)
        assert_root_trusted(root)
        print(f"owner-only: {root}")
        return 0
    if args[:1] == ["record-vendor"]:
        print(f"tree sha256 {record_vendor(settings, root / 'vendor')} -> {manifest_path(settings)}")
        return 0
    print("usage: python -m aas_build.desktop secure | record-vendor")
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
