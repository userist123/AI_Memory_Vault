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
(empty, studio-owned: the owner's ~/.cargo config and credentials are never read) and ``tmp/``.
This module starts no process (P1c); the build itself is ``runner.run_desktop_build``.
"""
from __future__ import annotations

import json
import os
import re
import shutil
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
