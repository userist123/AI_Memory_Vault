"""Build service: project files + studio kit -> offline Docker build (``runner.run_web_build``) -> preview site.

Used by the MCP ``app_build`` tool and nothing else. Flow: build context in ``<data>/build/<id>/ctx`` (studio kit
files + the project's ``src/`` and ``public/`` only, never mounted from the project workspace) -> fixed-argv
``docker run --network none`` -> validated output copied to ``<data>/preview/<project>/site-<ms>`` -> pointer file
``current`` switched -> the preview server (``p-<id16>.localhost``) serves the new site on the next request. A failed
build leaves the previous site in place. Events: build.started, build.log (tail lines), build.error, build.finished,
preview.captured. One build at a time across processes (`build_lock`: in-process lock + OS file lock; S9b review M1).
S9 (B4/B12): no build starts with less than 5 GiB free on the data or system drive, and every build (web and android)
runs inside ``Studio.operation`` (policy ``app.build``, tool_executions + audit row with actor, project and target).
"""
from __future__ import annotations

import hashlib
import os
import re
import shutil
import threading
import time
import uuid
from collections.abc import Callable, Iterator
from contextlib import ExitStack, contextmanager
from pathlib import Path
from typing import Any

from studio_core import activity
from studio_core.config import Settings
from studio_core.errors import StudioError, Unavailable
from studio_core.policy import Actor

from . import brand, desktop, runner

PRODUCT = Path(__file__).resolve().parents[2]
KIT = PRODUCT / "kit"
KIT_FILES = ("package.json", "pnpm-lock.yaml", "pnpm-workspace.yaml", ".npmrc", "index.html", "tsconfig.json",
             "capacitor.config.ts", "vite.config.ts", "aas-env.d.ts")
KIT_INSPECTOR = KIT / "template" / "src" / "aas-inspector.ts"  # W4: preview-only click-to-edit inspector
KEEP_SITES = 2
LOG_LINES = 40
_ID16 = re.compile(r"^[0-9a-f]{16}$")
_SITE = re.compile(r"^site-[0-9]{10,16}$")
_LOCK = threading.Lock()
_BUILDING: dict[str, str] = {}  # "project" -> project id of the build holding _LOCK (S9a review: delete is refused)
DESKTOP_MIN_FREE_DEFAULT_ROOT = 10 * 10 ** 9  # N4: target/ alone is ~2 GB; below this on the data drive, say where to move
MIN_FREE_BYTES = 5 * 1024 ** 3  # B4: a build (Docker layers, node_modules copy, Gradle) never starts below this
BUILD_ACTOR = Actor("system", "build", "system")  # only when a caller passes no actor; still policy-checked + audited


def _free_bytes(settings: Settings) -> int:
    """Free bytes on the drive of the data root and on the system drive (Docker's virtual disk lives there)."""
    roots: set[str] = set()
    p = Path(settings.data_dir)
    while not p.exists() and p.parent != p:
        p = p.parent
    roots.add(str(p))
    sysroot = os.environ.get("SystemRoot", "")
    if len(sysroot) > 2 and sysroot[1] == ":":
        roots.add(sysroot[:3])
    return min(shutil.disk_usage(r).free for r in roots)


def _disk_gate(settings: Settings, free_bytes: Callable[[], int] | None) -> None:
    free = free_bytes() if free_bytes else _free_bytes(settings)
    if free < MIN_FREE_BYTES:
        raise Unavailable(f"builds are disabled: less than 5 GiB free on the data or system drive ({free // 2**20} MiB)")


def _audited(studio: Any, actor: Actor | None, project_id: str, target: str, fn: Callable[[], dict[str, Any]]) -> dict[str, Any]:
    """B12: every build goes through Studio.operation (policy ``app.build`` + tool_executions + audit row)."""
    pid = project_id if isinstance(project_id, str) and re.fullmatch(r"^prj_[0-9a-f]{16}$", project_id) else None
    return studio.operation(actor or BUILD_ACTOR, "app.build", "app.build", {"projectId": str(project_id)[:40],
                            "target": target}, lambda _ctx: fn(), project_id=pid)


class BuildFailed(StudioError):
    code = "build_failed"
    http_status = 422


def _lock_files(settings: Any) -> tuple[Path, Path]:
    d = Path(settings.data_dir) / "build"
    return d / "build.lock", d / "build.holder"


def _try_os_lock(fd: int) -> bool:
    """Exclusive, non-blocking OS lock on byte 0 (released by the OS when the holder process dies)."""
    os.lseek(fd, 0, os.SEEK_SET)
    try:
        if os.name == "nt":
            import msvcrt
            msvcrt.locking(fd, msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        return False
    return True


def _os_unlock(fd: int) -> None:
    os.lseek(fd, 0, os.SEEK_SET)
    try:
        if os.name == "nt":
            import msvcrt
            msvcrt.locking(fd, msvcrt.LK_UNLCK, 1)
        else:
            import fcntl
            fcntl.flock(fd, fcntl.LOCK_UN)
    except OSError:
        pass


def _retry(fn: Callable[[], Any]) -> None:
    """A concurrent probe may have the holder file open for a moment (Windows refuses rename/delete then)."""
    for i in range(100):
        try:
            fn()
            return
        except PermissionError:
            if i == 99:
                raise
            time.sleep(0.01)


@contextmanager
def build_lock(settings: Any, project_id: str) -> Iterator[None]:
    """S9b review M1: ONE build at a time across processes (backend, built-in runner MCP child, external MCP server).
    In-process: ``_LOCK``; cross-process: an OS lock on ``<data>/build/build.lock`` (dies with its process, so a
    crashed holder never leaves a stale lock) + ``build.holder`` naming the project for ``building_project``."""
    if not _LOCK.acquire(blocking=False):
        raise Unavailable("a build is already running")
    try:
        lock, holder = _lock_files(settings)
        lock.parent.mkdir(parents=True, exist_ok=True)
        fd = os.open(lock, os.O_RDWR | os.O_CREAT | getattr(os, "O_BINARY", 0), 0o600)
        try:
            if os.fstat(fd).st_size == 0:
                try:
                    os.write(fd, b"L")  # the probe reads this byte (a read of a locked byte fails: no lock needed)
                except OSError:
                    pass
            if not _try_os_lock(fd):
                raise Unavailable("a build is already running (another studio process)")
            try:
                tmp = holder.with_name(f"build.holder.{os.getpid()}.tmp")
                tmp.write_text(f"{os.getpid()}:{project_id[:40]}", encoding="utf-8")
                _retry(lambda: os.replace(tmp, holder))
                _BUILDING["project"] = project_id
                yield
            finally:
                _BUILDING.pop("project", None)
                _retry(lambda: holder.unlink(missing_ok=True))
                _os_unlock(fd)
        finally:
            os.close(fd)
    finally:
        _LOCK.release()


def _lock_is_held(fd: int, holder: Path) -> bool:
    """Is the build lock held by another handle/process? WITHOUT taking it (S9c review L2: a probe that locks, even
    briefly, makes a build that starts at that moment fail with "already running"). Windows: the holder's byte-range
    lock makes a read of that byte fail. POSIX (flock is advisory): the holder file names ``<pid>`` and the pid must be
    alive."""
    if os.name == "nt":
        os.lseek(fd, 0, os.SEEK_SET)
        try:
            os.read(fd, 1)
        except OSError:
            return True
        return False
    try:
        pid = int(holder.read_text(encoding="utf-8").split(":", 1)[0])
        os.kill(pid, 0)
        return True
    except (OSError, ValueError):
        return False


def building_project(settings: Any | None = None) -> str | None:
    """Project whose build (web, android or desktop; any process incl. an external MCP server) holds the build lock,
    else None. Without ``settings`` only this process is checked. Never takes the lock."""
    if _BUILDING.get("project"):
        return _BUILDING["project"]
    if settings is None:
        return None
    lock, holder = _lock_files(settings)
    try:
        fd = os.open(lock, os.O_RDONLY | getattr(os, "O_BINARY", 0))
    except OSError:
        return None
    try:
        if not _lock_is_held(fd, holder):
            return None
        try:
            name = holder.read_text(encoding="utf-8").split(":", 1)[-1][:40].strip()
        except OSError:
            return "unknown"
        return name if re.fullmatch(r"prj_[0-9a-f]{16}", name) else "unknown"
    finally:
        os.close(fd)


def preview_base(settings: Settings, project_id: str) -> Path:
    return settings.data_dir / "preview" / project_id


def site_root_for(settings: Settings) -> Callable[[str], Path | None]:
    """The preview server's resolver: ``<id16>`` (host label) -> the current built site, or None."""
    def resolve(id16: str) -> Path | None:
        if not isinstance(id16, str) or not _ID16.fullmatch(id16):
            return None
        base = preview_base(settings, "prj_" + id16)
        try:
            name = (base / "current").read_text(encoding="utf-8")[:40].strip()
        except OSError:
            return None
        if not _SITE.fullmatch(name):
            return None
        site = base / name
        return site if site.is_dir() and not site.is_symlink() else None
    return resolve


def _materialise(studio: Any, project_id: str, ctx: Path) -> int:
    ctx.mkdir(parents=True)
    for name in KIT_FILES:
        shutil.copy2(KIT / name, ctx / name)
    ws = studio.workspace(project_id, create=False)
    n = 0
    for top in ("src", "public"):
        for rel in ws.iter_files(top):
            target = ctx / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(ws.read_bytes(rel))
            n += 1
    # W4: the kit's preview inspector (index.html loads /src/aas-inspector.ts) always comes from the kit, never from
    # the project: a project file of the same name is overwritten here.
    (ctx / "src").mkdir(parents=True, exist_ok=True)
    shutil.copy2(KIT_INSPECTOR, ctx / "src" / "aas-inspector.ts")
    return n


def _rec(studio: Any, kind: str, project_id: str, run_id: str | None, **payload: Any) -> None:
    try:
        activity.record(studio, kind, project_id=project_id, run_id=run_id, source="build", payload=payload)
    except Exception:  # noqa: BLE001 - the activity stream never breaks a build
        activity._note_dropped(studio)  # noqa: SLF001


def _prune(base: Path, keep: str) -> None:
    sites = sorted((p for p in base.iterdir() if p.is_dir() and _SITE.fullmatch(p.name)), key=lambda p: p.name)
    for p in sites[:-KEEP_SITES]:
        if p.name != keep:
            shutil.rmtree(p, ignore_errors=True)


def build_web(studio: Any, project_id: str, *, run_id: str | None = None,
              run_build: Callable[..., runner.BuildResult] = runner.run_web_build,
              docker: str | None = None, image: str | None = None, actor: Actor | None = None,
              free_bytes: Callable[[], int] | None = None) -> dict[str, Any]:
    return _audited(studio, actor, project_id, "web", lambda: _build_web(
        studio, project_id, run_id=run_id, run_build=run_build, docker=docker, image=image, free_bytes=free_bytes))


def _build_web(studio: Any, project_id: str, *, run_id: str | None, run_build: Callable[..., runner.BuildResult],
               docker: str | None, image: str | None, free_bytes: Callable[[], int] | None) -> dict[str, Any]:
    if not re.fullmatch(r"^prj_[0-9a-f]{16}$", project_id):
        raise BuildFailed("invalid project id")
    _disk_gate(studio.settings, free_bytes)
    held = ExitStack()
    held.enter_context(build_lock(studio.settings, project_id))
    t0 = time.monotonic()
    build_root = studio.settings.data_dir / "build" / ("b" + uuid.uuid4().hex[:16])
    try:
        revision = studio.get_revision(project_id)
        docker = docker or shutil.which("docker")
        if not docker or not os.path.isabs(docker):
            raise Unavailable("docker is not installed or not on PATH")
        try:
            image = image or runner.resolve_image(docker)
        except runner.BuildError as e:
            raise Unavailable(str(e)) from e
        _rec(studio, "build.started", project_id, run_id, target="web", revision=revision)
        base = preview_base(studio.settings, project_id)
        base.mkdir(parents=True, exist_ok=True)
        name = f"site-{int(time.time() * 1000):013d}"
        ctx, out = build_root / "ctx", build_root / "out"
        try:
            _materialise(studio, project_id, ctx)
            out.mkdir(parents=True)
            res = run_build(docker=docker, build_id=build_root.name, ctx=ctx, out=out, image=image, dest=base / name)
        except (runner.BuildError, OSError, StudioError) as e:
            msg = str(e)[:1000]
            _rec(studio, "build.error", project_id, run_id, code="build_failed", message=msg)
            _rec(studio, "build.finished", project_id, run_id, status="failed", revision=revision,
                 durationMs=int((time.monotonic() - t0) * 1000))
            raise BuildFailed(msg) from e
        for line in [ln for ln in res.log_tail.splitlines() if ln.strip()][-LOG_LINES:]:
            _rec(studio, "build.log", project_id, run_id, stream="stdout", line=line)
        tmp_ptr = base / "current.tmp"
        tmp_ptr.write_text(name, encoding="utf-8")
        os.replace(tmp_ptr, base / "current")  # atomic pointer switch: the preview reloads from the new site
        _prune(base, name)
        dur = int((time.monotonic() - t0) * 1000)
        _rec(studio, "build.finished", project_id, run_id, status="ok", revision=revision, durationMs=dur,
             artifactSha256=res.dist_sha256)
        # S8: the reload signal is preview.revision; preview.captured is only for real PNG captures (thumbnails)
        _rec(studio, "preview.revision", project_id, run_id, revision=revision)
        return {"ok": True, "revision": revision, "files": len(res.files), "distSha256": res.dist_sha256,
                "durationMs": dur, "url": f"http://p-{project_id[4:]}.localhost:{studio.settings.preview_port}/"}
    finally:
        shutil.rmtree(build_root, ignore_errors=True)
        held.close()


def apk_path(settings: Settings, project_id: str) -> Path:
    """Where the project's current debug APK lives (read by aas_device.DeviceService.install)."""
    return preview_base(settings, project_id) / "app-debug.apk"


def keys_dir(settings: Settings) -> Path:
    """Studio debug keystore directory: <data root>/keys (%LOCALAPPDATA%/AIAppStudio/keys), never the owner's
    Android user dir."""
    return settings.data_dir / "keys"


def build_android(studio: Any, project_id: str, *, run_id: str | None = None,
                  run_build: Callable[..., runner.AndroidResult] = runner.run_android_build,
                  ensure_keys: Callable[..., tuple[Path, str]] = runner.ensure_keystore,
                  docker: str | None = None, image: str | None = None, actor: Actor | None = None,
                  free_bytes: Callable[[], int] | None = None) -> dict[str, Any]:
    """Current web build of the project (the preview site) -> offline Docker Gradle build -> signed debug APK at
    ``apk_path``. Events: build.started {target android}, build.log, build.error, build.finished {target, status,
    artifactSha256, sizeBytes} with the APK sha256 also in the typed artifact column."""
    return _audited(studio, actor, project_id, "android", lambda: _build_android(
        studio, project_id, run_id=run_id, run_build=run_build, ensure_keys=ensure_keys, docker=docker, image=image,
        free_bytes=free_bytes))


def _build_android(studio: Any, project_id: str, *, run_id: str | None, run_build: Callable[..., runner.AndroidResult],
                   ensure_keys: Callable[..., tuple[Path, str]], docker: str | None, image: str | None,
                   free_bytes: Callable[[], int] | None) -> dict[str, Any]:
    if not re.fullmatch(r"^prj_[0-9a-f]{16}$", project_id):
        raise BuildFailed("invalid project id")
    _disk_gate(studio.settings, free_bytes)
    held = ExitStack()
    held.enter_context(build_lock(studio.settings, project_id))
    t0 = time.monotonic()
    build_root = studio.settings.data_dir / "build" / ("a" + uuid.uuid4().hex[:16])
    try:
        revision = studio.get_revision(project_id)
        site = site_root_for(studio.settings)(project_id[4:])
        if site is None:
            raise Unavailable("build the web target first (app_build target web): the APK packages the web build")
        docker = docker or shutil.which("docker")
        if not docker or not os.path.isabs(docker):
            raise Unavailable("docker is not installed or not on PATH")
        try:
            image = image or runner.resolve_image(docker, runner.ANDROID_TAG)
        except runner.BuildError as e:
            raise Unavailable(str(e).replace("images/web-build", "images/android-build")) from e
        _rec(studio, "build.started", project_id, run_id, target="android", revision=revision)
        out = build_root / "out"
        try:
            keystore, signer = ensure_keys(docker=docker, image=image, keys_dir=keys_dir(studio.settings))
            out.mkdir(parents=True)
            app_id = runner.app_id_for(project_id)
            store = brand.BrandStore(studio.settings.data_dir)
            code = store.next_version_code(project_id)  # W5: consumed only after the build succeeded (below)
            name = str(studio.get_project(Actor("system", "brand", "system"), project_id,
                                          with_components=False).get("name", ""))
            brand_dir = brand.write_brand_dir(_brand_config(studio, project_id, store), name, app_id, code,
                                              build_root / "brand")
            res = run_build(docker=docker, build_id=build_root.name, dist=site, keystore=keystore,
                            expected_signer=signer, out=out, image=image, app_id=app_id,
                            dest=apk_path(studio.settings, project_id), brand=brand_dir)
            store.commit_version_code(project_id, code)
        except (runner.BuildError, brand.BrandError, OSError, StudioError) as e:
            msg = str(e)[:1000]
            _rec(studio, "build.error", project_id, run_id, code="build_failed", message=msg)
            _rec(studio, "build.finished", project_id, run_id, target="android", status="failed", revision=revision,
                 durationMs=int((time.monotonic() - t0) * 1000))
            raise BuildFailed(msg) from e
        for line in [ln for ln in res.log_tail.splitlines() if ln.strip()][-LOG_LINES:]:
            _rec(studio, "build.log", project_id, run_id, stream="stdout", line=line)
        dur = int((time.monotonic() - t0) * 1000)
        try:
            activity.record(studio, "build.finished", project_id=project_id, run_id=run_id, source="build",
                            artifact_sha256=res.sha256,
                            payload={"target": "android", "status": "ok", "revision": revision, "durationMs": dur,
                                     "artifactSha256": res.sha256, "sizeBytes": res.size,
                                     "signerSha256": res.signer_sha256})
        except Exception:  # noqa: BLE001
            activity._note_dropped(studio)  # noqa: SLF001
        cfg = _brand_config(studio, project_id, store)
        try:  # W5: 16 KB page-size report of the native libraries (informational; never fails the build)
            align = brand.check_native_alignment(res.apk)
        except (OSError, ValueError) as e:
            align = {"status": "unknown", "problems": [type(e).__name__]}
        return {"ok": True, "target": "android", "revision": revision, "apkSha256": res.sha256, "sizeBytes": res.size,
                "signerSha256": res.signer_sha256, "packageId": runner.app_id_for(project_id), "durationMs": dur,
                "versionCode": code, "versionName": brand.semver(cfg, code), "alignment16k": align}
    finally:
        shutil.rmtree(build_root, ignore_errors=True)
        held.close()


def _brand_config(studio: Any, project_id: str, store: brand.BrandStore) -> brand.BrandConfig:
    """The project's saved brand, or one seeded from the project's design tokens (src/index.css) when none was saved."""
    if (store.dir(project_id) / "brand.json").is_file():
        return store.config(project_id)
    try:
        css = studio.read_file(Actor("system", "brand", "system"), project_id, "src/index.css", quiet=True).get("content", "")
    except Exception:  # noqa: BLE001 - no token file: the default palette
        css = ""
    pal = brand.palette_from_css(str(css))
    try:
        return brand.validate(brand.BrandConfig(**pal))
    except brand.BrandError:
        return brand.BrandConfig(background=pal.get("background", brand.DEFAULT_BACKGROUND))


# ---- Windows desktop (S9c) ---------------------------------------------------------------------------------------------
_BUILD_ID = re.compile(r"^d[0-9a-f]{16}$")


def desktop_dir(settings: Settings, project_id: str, build_id: str) -> Path:
    """One finished desktop build = one dir holding the exe AND the zip of the dist it embeds (written together in a
    staging dir, put in place by ONE directory rename; S9c review M1)."""
    if not isinstance(build_id, str) or not _BUILD_ID.fullmatch(build_id):
        raise Unavailable("invalid desktop build id")
    return preview_base(settings, project_id) / "desktop" / build_id


def desktop_path(settings: Settings, project_id: str, build_id: str) -> Path:
    """The unsigned desktop exe of build ``build_id`` (export target ``desktop``)."""
    return desktop_dir(settings, project_id, build_id) / "app-desktop.exe"


def desktop_dist_path(settings: Settings, project_id: str, build_id: str) -> Path:
    """Deterministic zip of the exact dist embedded in that build's exe (scanned on export)."""
    return desktop_dir(settings, project_id, build_id) / "app-desktop.dist.zip"


def _sha_file(p: Path) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def build_desktop(studio: Any, project_id: str, *, run_id: str | None = None,
                  run_build: Callable[..., runner.DesktopResult] = runner.run_desktop_build,
                  cargo: str | None = None, actor: Actor | None = None,
                  free_bytes: Callable[[], int] | None = None) -> dict[str, Any]:
    """Current web build (preview site) -> studio Tauri wrapper -> host ``cargo build --release --offline --locked`` ->
    unsigned exe at ``desktop_path``. Events: build.started/log/error/finished {target desktop, artifactSha256,
    sizeBytes}; the exe sha256 is also in the typed artifact column (export binds the approval to it)."""
    return _audited(studio, actor, project_id, "desktop", lambda: _build_desktop(
        studio, project_id, run_id=run_id, run_build=run_build, cargo=cargo, free_bytes=free_bytes))


def _desktop_drive_free(path: Path) -> int:
    p = Path(path)
    while not p.exists() and p != p.parent:
        p = p.parent
    return shutil.disk_usage(p).free


def _build_desktop(studio: Any, project_id: str, *, run_id: str | None, run_build: Callable[..., runner.DesktopResult],
                   cargo: str | None, free_bytes: Callable[[], int] | None) -> dict[str, Any]:
    if not re.fullmatch(r"^prj_[0-9a-f]{16}$", project_id):
        raise BuildFailed("invalid project id")
    _disk_gate(studio.settings, free_bytes)
    try:
        root = desktop.desktop_root(studio.settings)
    except runner.BuildError as e:
        raise Unavailable(str(e)) from e
    if free_bytes is None and root.exists() and shutil.disk_usage(root).free < MIN_FREE_BYTES:
        raise Unavailable("builds are disabled: less than 5 GiB free on the desktop build drive")
    if not os.environ.get("AAS_DESKTOP_ROOT") and _desktop_drive_free(root) < DESKTOP_MIN_FREE_DEFAULT_ROOT:
        raise Unavailable("desktop builds need 10 GB free for the default desktop root (<data>/desktop): set "
                          "AAS_DESKTOP_ROOT to an owner-only folder on another drive (for example an aas-desktop folder; create it "
                          "with `python -m aas_build.desktop secure`)")
    held = ExitStack()
    held.enter_context(build_lock(studio.settings, project_id))
    t0 = time.monotonic()
    build_root = root / "builds" / ("d" + uuid.uuid4().hex[:16])
    try:
        revision = studio.get_revision(project_id)
        site = site_root_for(studio.settings)(project_id[4:])
        if site is None:
            raise Unavailable("build the web target first (app_build target web): the exe packages the web build")
        vendor = root / "vendor"
        if not vendor.is_dir() or vendor.is_symlink() or not any(vendor.iterdir()):
            raise Unavailable(f"the vendored crate dir is missing ({vendor}): run the one-time cargo vendor step "
                              "(docs/slices/S9c.md)")
        try:
            desktop.assert_root_trusted(root)  # S9c review M2: owner-only root chain
            desktop.refuse_planted_cargo_config(build_root)  # M2: cargo merges .cargo/config of every parent
            for d in ("target", "cargo-home", "tmp", "builds"):
                if (root / d).is_symlink():
                    raise runner.BuildError(f"{d} in the desktop root is a link")
            desktop.verify_vendor(studio.settings, vendor)  # M3: per-file hashes from the studio-held manifest
        except runner.BuildError as e:
            raise Unavailable(str(e)) from e
        try:
            cargo = cargo or runner.resolve_cargo()
        except runner.BuildError as e:
            raise Unavailable(str(e)) from e
        project = studio.get_project(Actor("system", "build", "system"), project_id, with_components=False)
        _rec(studio, "build.started", project_id, run_id, target="desktop", revision=revision)
        build_id = "d" + uuid.uuid4().hex[:16]
        base = preview_base(studio.settings, project_id)
        stage = base / f".desktop-{uuid.uuid4().hex[:8]}"
        final = desktop_dir(studio.settings, project_id, build_id)
        try:
            wrapper = desktop.materialise(build_root, site, project_id, str(project.get("name", "")), vendor)
            zdata = desktop.dist_zip(build_root / "dist")
            desktop.write_cargo_home(root / "cargo-home")
            stage.mkdir(parents=True)
            desktop.refuse_planted_cargo_config(build_root)  # N3: again right before the spawn (vendor hashing is
            desktop.assert_root_trusted(root)  # slow: a config planted or an ACL loosened meanwhile is caught here)
            res = run_build(cargo=cargo, wrapper=wrapper, target_dir=root / "target", cargo_home=root / "cargo-home",
                            tmp=root / "tmp", dest=stage / "app-desktop.exe",
                            cargo_args=desktop.cargo_args(root, vendor, build_root))
            (stage / "app-desktop.dist.zip").write_bytes(zdata)
            exe_sha, dist_sha = _sha_file(stage / "app-desktop.exe"), hashlib.sha256(zdata).hexdigest()
            if exe_sha != res.sha256:
                raise runner.BuildError("the staged exe changed after the build")
            leaks = desktop.host_path_leaks((stage / "app-desktop.exe").read_bytes(),
                                            desktop.leak_prefixes(root, vendor, build_root))
            if leaks:
                raise runner.BuildError(f"the exe contains a host path ({leaks[0][:60]}): refusing to keep it")
            final.parent.mkdir(parents=True, exist_ok=True)
            os.replace(stage, final)  # the pair is put in place by one rename: both files or neither
        except (runner.BuildError, OSError, StudioError) as e:
            shutil.rmtree(stage, ignore_errors=True)
            msg = str(e)[:1000]
            _rec(studio, "build.error", project_id, run_id, code="build_failed", message=msg)
            _rec(studio, "build.finished", project_id, run_id, target="desktop", status="failed", revision=revision,
                 durationMs=int((time.monotonic() - t0) * 1000))
            raise BuildFailed(msg) from e
        for line in [ln for ln in res.log_tail.splitlines() if ln.strip()][-LOG_LINES:]:
            _rec(studio, "build.log", project_id, run_id, stream="stdout", line=line)
        dur = int((time.monotonic() - t0) * 1000)
        try:
            activity.record(studio, "build.finished", project_id=project_id, run_id=run_id, source="build",
                            artifact_sha256=exe_sha,
                            payload={"target": "desktop", "status": "ok", "revision": revision, "durationMs": dur,
                                     "artifactSha256": exe_sha, "distSha256": dist_sha, "buildId": build_id,
                                     "sizeBytes": res.size})
            for old in (base / "desktop").iterdir():  # only after the record exists: older pairs are no longer current
                if old.name != build_id:
                    shutil.rmtree(old, ignore_errors=True)
        except Exception:  # noqa: BLE001
            activity._note_dropped(studio)  # noqa: SLF001
        return {"ok": True, "target": "desktop", "revision": revision, "exeSha256": exe_sha, "distSha256": dist_sha,
                "buildId": build_id, "sizeBytes": res.size,
                "appId": desktop.app_id(project_id), "signed": False, "durationMs": dur}
    finally:
        shutil.rmtree(build_root, ignore_errors=True)
        held.close()
