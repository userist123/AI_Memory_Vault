"""Device tools service (start phone, install, tap, screenshot) in front of the S5 launcher's public API.

Gates, all fail closed and checked before any device process is started:
  1. the owner setting ``device_enabled`` (default OFF; only the authenticated owner can change it);
  2. free space on the system and LOCALAPPDATA drives >= 8 GiB (the S5 emulator crash-looped and filled C: with crash dumps);
  3. the device-host preflight (networking mode nat, pinned scripts, only aas-* AVDs).
The only thing imported from ``apps.device_host`` is ``launcher`` (``preflight``, ``DeviceSession``); this module
never builds an argv and never starts a process itself. Screenshots are saved under ``<data>/device/shots/<sha256>.png``
and only their digest and size are returned to the agent.

While a session runs (S6 review): gates 1 and 2 are re-checked on every tap/screenshot/install and by a 1 s watcher;
a failed re-check stops the device. The watcher also stops it when ``alive()`` turns false (the run's grant was
revoked: run end, stop, backend shutdown), and ``close`` is registered with ``atexit`` for a normal process exit.

S9 (B4/B12): screenshots are capped (one PNG <= MAX_SHOT_BYTES; the shots directory keeps at most MAX_SHOTS files and
MAX_SHOT_BYTES_TOTAL bytes, oldest pruned first), and start/tap/screenshot/install run inside ``Studio.operation``
(policy ``device.control``, tool_executions + audit row; refused calls are audited too).
"""
from __future__ import annotations

import atexit
import hashlib
import os
import re
import shutil
import tempfile
import threading
import time
from collections.abc import Callable
from pathlib import Path
from typing import Any

from sqlalchemy import select
from studio_core import activity, audit
from studio_core.aas_models import ActivityEvent, AppSetting
from studio_core.errors import PolicyDenied, Unavailable, ValidationFailed
from studio_core.ids import utcnow
from studio_core.policy import Actor

from . import video_relay

SETTING_KEY = "device_enabled"
MAX_SHOT_BYTES = 8 * 1024 * 1024
MAX_SHOTS = 200
MAX_SHOT_BYTES_TOTAL = 200 * 1024 * 1024
DEVICE_ACTOR = Actor("system", "device", "system")  # only when no actor is given; still policy-checked + audited
MIN_FREE_BYTES = 8 * 1024 ** 3
COORD_MAX = 10_000
_PID = re.compile(r"^prj_[0-9a-f]{16}$")
PKG_PREFIX = "dev.aiappstudio.p"  # + id16 = the device host's PKG_RE (device host script aas_devhost.py)


def is_enabled(studio: Any) -> bool:
    with studio.session() as s:
        row = s.get(AppSetting, SETTING_KEY)
        return row is not None and row.value == "1"


def set_enabled(studio: Any, actor: Actor, enabled: bool) -> dict[str, Any]:
    if not actor.is_owner:
        raise PolicyDenied("only the authenticated human owner can change device access")
    with studio.audit_session() as s:
        row = s.get(AppSetting, SETTING_KEY)
        if row is None:
            row = AppSetting(key=SETTING_KEY, value="0")
            s.add(row)
        row.value = "1" if enabled else "0"
        row.updated_at = utcnow()
        audit.record(s, actor, "settings.device", "enabled" if enabled else "disabled")
    return {"enabled": bool(enabled)}


def _free_bytes() -> int:
    roots = {(os.environ.get("SystemRoot") or os.environ.get("SystemDrive", "") + os.sep)[:3]}
    lad = os.environ.get("LOCALAPPDATA")
    if lad and len(lad) > 2 and lad[1] == ":":
        roots.add(lad[:3])
    return min(shutil.disk_usage(r).free for r in roots)


_STAMP_LOCK = threading.Lock()
_LAST_STAMP = [0]


def _stamp(p: Path) -> None:
    """Strictly increasing mtime per write (the prune order), even for writes within one clock tick."""
    with _STAMP_LOCK:
        ns = max(time.time_ns(), _LAST_STAMP[0] + 1000)
        _LAST_STAMP[0] = ns
    os.utime(p, ns=(ns, ns))


def _prune_shots(d: Path, keep: str) -> None:
    """Oldest first until the directory is within MAX_SHOTS files and MAX_SHOT_BYTES_TOTAL bytes (``keep`` stays)."""
    shots: list[tuple[float, str, int]] = []
    for p in d.glob("*.png"):
        try:
            st = p.lstat()
        except OSError:
            continue
        shots.append((float(st.st_mtime_ns) if p.name != keep else float("inf"), p.name, st.st_size))
    shots.sort()
    total = sum(x[2] for x in shots)
    while len(shots) > 1 and (len(shots) > MAX_SHOTS or total > MAX_SHOT_BYTES_TOTAL):
        _t, name, size = shots.pop(0)
        if name == keep:
            break
        (d / name).unlink(missing_ok=True)
        total -= size


class DeviceService:
    def __init__(self, studio: Any, *, preflight: Callable[[], dict[str, Any]] | None = None,
                 free_bytes: Callable[[], int] = _free_bytes,
                 session_factory: Callable[[str], Any] | None = None, alive: Callable[[], bool] | None = None,
                 run_id: str | None = None, actor: Callable[[], Actor] | None = None) -> None:
        self.studio = studio
        self._actor = actor or (lambda: DEVICE_ACTOR)
        self._free = free_bytes
        self._alive = alive or (lambda: True)
        self.run_id = run_id
        self._lock = threading.RLock()
        self._watching = False
        atexit.register(self.close)
        if preflight is None or session_factory is None:
            from apps.device_host import launcher
            preflight = preflight or launcher.preflight
            session_factory = session_factory or launcher.DeviceSession
        self._preflight, self._factory = preflight, session_factory
        self.session: Any = None
        self._video: Any = None

    def _setting_and_disk(self) -> None:
        if not is_enabled(self.studio):
            raise Unavailable("device tools are disabled: the owner has not enabled them in the studio settings")
        free = self._free()
        if free < MIN_FREE_BYTES:
            raise Unavailable(f"device tools are disabled: less than 8 GiB free on the system drive ({free // 2**20} MiB)")

    def _gate(self) -> None:
        self._setting_and_disk()
        try:
            self._preflight()
        except Exception as e:  # noqa: BLE001 - LauncherRefused, Tampered, DeviceError, OSError: all fail closed
            raise Unavailable(f"device tools are disabled: device-host preflight failed ({type(e).__name__}: {str(e)[:160]})") from e

    def _rec(self, kind: str, **payload: Any) -> None:
        try:
            activity.record(self.studio, kind, run_id=self.run_id, source="device", payload=payload)
        except Exception:  # noqa: BLE001
            activity._note_dropped(self.studio)  # noqa: SLF001

    def _op(self, tool: str, params: dict[str, Any], fn: Callable[[], dict[str, Any]],
            project_id: str | None = None) -> dict[str, Any]:
        return self.studio.operation(self._actor(), tool, "device.control", params, lambda _ctx: fn(),
                                     project_id=project_id)

    def start(self, form: str = "phone") -> dict[str, Any]:
        return self._op("device.start", {"form": str(form)[:16]}, lambda: self._start(form))

    def _start(self, form: str) -> dict[str, Any]:
        if form not in ("phone", "tablet"):
            raise ValidationFailed("form must be phone or tablet")
        self._gate()
        if self.session is not None:
            raise Unavailable("a device session is already running")
        sess = self._factory(form)
        self._rec("device.state", state="starting", avd=f"aas-{form}")
        sess.start()
        self.session = sess
        self._rec("device.state", state="running", avd=f"aas-{form}")
        # S8: the run's mirror pipe (env from the runner); without it there is no in-studio mirror
        sender = video_relay.VideoSender.from_env(sess)
        self._video = sender.start() if sender is not None else None
        return {"state": "running", "form": form}

    def _need(self) -> Any:
        if self.session is None:
            raise Unavailable("the device session is not started (call device_start first)")
        try:
            self._setting_and_disk()  # re-checked on every call: switched off / low disk -> the device is stopped
        except Unavailable:
            self.stop()
            raise
        return self.session

    # ---------- lifetime ----------
    def watch_tick(self) -> bool:
        """One watcher step; returns True while the session keeps running."""
        if self.session is None:
            return False
        try:
            ok = bool(self._alive())
            if ok:
                self._setting_and_disk()
        except Exception:  # noqa: BLE001 - Unavailable or a failing check: fail closed
            ok = False
        if not ok:
            self.stop()
        return self.session is not None

    def start_watch(self, interval: float = 1.0) -> None:
        with self._lock:
            if self._watching:
                return
            self._watching = True

        def loop() -> None:
            try:
                while self.session is not None:
                    time.sleep(interval)
                    self.watch_tick()
            finally:
                self._watching = False
        threading.Thread(target=loop, name="aas-device-watch", daemon=True).start()

    def close(self) -> None:
        """Idempotent; also the atexit hook. Never raises."""
        try:
            self.stop()
        except Exception:  # noqa: BLE001
            pass

    @staticmethod
    def _coord(v: Any) -> int:
        if isinstance(v, bool) or not isinstance(v, int) or not 0 <= v <= COORD_MAX:
            raise ValidationFailed("coordinate must be an integer between 0 and 10000")
        return v

    def tap(self, x: Any, y: Any) -> dict[str, Any]:
        return self._op("device.tap", {"x": x, "y": y}, lambda: self._tap(x, y))

    def _tap(self, x: Any, y: Any) -> dict[str, Any]:
        s = self._need()
        x, y = self._coord(x), self._coord(y)
        self._rec("device.input", action="tap", x=x, y=y)
        return s.tap(x, y)

    def screenshot(self) -> dict[str, Any]:
        return self._op("device.screenshot", {}, self._screenshot)

    def _screenshot(self) -> dict[str, Any]:
        s = self._need()
        png = s.screenshot()
        if not isinstance(png, bytes | bytearray) or not 0 < len(png) <= MAX_SHOT_BYTES:
            raise Unavailable(f"device screenshot refused: size outside 1..{MAX_SHOT_BYTES} bytes")
        png = bytes(png)
        sha = hashlib.sha256(png).hexdigest()
        d = self.studio.settings.data_dir / "device" / "shots"
        d.mkdir(parents=True, exist_ok=True)
        (d / f"{sha}.png").write_bytes(png)
        _stamp(d / f"{sha}.png")
        _prune_shots(d, keep=f"{sha}.png")
        self._rec("device.log", text=f"screenshot {sha[:12]} {len(png)} bytes")
        return {"sha256": sha, "size": len(png)}

    def install(self, project_id: str, *, launch: bool = False) -> dict[str, Any]:
        """Install the project's debug APK (the device host accepts only dev.aiappstudio.p<16 hex>); with ``launch``
        the installed package must be exactly the project's package id and is then started."""
        pid = project_id if isinstance(project_id, str) and _PID.fullmatch(project_id) else None
        return self._op("device.install", {"projectId": str(project_id)[:40], "launch": bool(launch)},
                        lambda: self._install(project_id, launch=launch), project_id=pid)

    def _install(self, project_id: str, *, launch: bool) -> dict[str, Any]:
        s = self._need()
        if not isinstance(project_id, str) or not _PID.fullmatch(project_id):
            raise ValidationFailed("invalid project id")
        apk = self.studio.settings.data_dir / "preview" / project_id / "app-debug.apk"
        if not apk.is_file() or apk.is_symlink():
            raise Unavailable("no APK has been built for this project (call app_build with target android first)")
        data = apk.read_bytes()
        sha = hashlib.sha256(data).hexdigest()
        recorded = self._recorded_apk_sha(project_id)
        if recorded is None:
            self._rec("device.log", text="install refused: no recorded build for this APK")
            raise Unavailable("the APK has no recorded build (build.finished) for this project: rebuild with app_build target android")
        if sha != recorded:
            self._rec("device.log", text=f"install refused: APK sha256 {sha[:12]} != recorded build {recorded[:12]}")
            raise Unavailable("the APK on disk does not match the sha256 recorded by its build: rebuild with app_build target android")
        rep = self._install_verified(s, data, sha)
        self._rec("device.log", text=f"install {apk.name} sha256 {sha[:12]}")
        if not launch:
            return rep
        pkg = PKG_PREFIX + project_id[4:]
        if not rep.get("ok") or rep.get("pkg") != pkg:
            self._rec("device.log", text=f"install failed: {str(rep.get('err') or rep.get('msg') or '')[:200]}")
            return {"install": rep, "launch": None}
        self._rec("device.log", text=f"installed {pkg}")
        self._need()  # gates again before the app runs
        lr = s.launch(pkg)
        self._rec("device.log", text=f"launch {pkg} {'ok' if lr.get('ok') else 'failed'}")
        return {"install": rep, "launch": lr, "packageId": pkg, "apkSha256": sha}

    def _recorded_apk_sha(self, project_id: str) -> str | None:
        """sha256 of the project's most recent successful android build (the ``build.finished`` event), else None."""
        with self.studio.session() as db:
            q = (select(ActivityEvent).where(ActivityEvent.project_id == project_id, ActivityEvent.kind == "build.finished",
                                             ActivityEvent.artifact_sha256.is_not(None))
                 .order_by(ActivityEvent.seq.desc()).limit(50))
            for e in db.execute(q).scalars():
                pl = e.payload or {}
                if pl.get("target") == "android" and pl.get("status") == "ok":
                    return e.artifact_sha256
        return None

    def _install_verified(self, session: Any, data: bytes, sha: str) -> dict[str, Any]:
        """Install from a private copy of the exact bytes that were hashed; the copy is re-hashed before use."""
        stage = Path(tempfile.mkdtemp(prefix="aas-apk-", dir=self._stage_root()))
        try:
            copy = stage / "app-debug.apk"
            fd = os.open(copy, os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_BINARY", 0), 0o600)
            with os.fdopen(fd, "wb") as f:
                f.write(data)
            if hashlib.sha256(copy.read_bytes()).hexdigest() != sha:
                raise Unavailable("the staged APK copy does not match its sha256")
            return session.install(copy)
        finally:
            shutil.rmtree(stage, ignore_errors=True)

    def _stage_root(self) -> Path:
        root = self.studio.settings.data_dir / "device" / "stage"
        root.mkdir(parents=True, exist_ok=True)
        return root

    def stop(self) -> dict[str, Any]:
        with self._lock:
            s, self.session = self.session, None
            v, self._video = self._video, None
        if v is not None:
            v.stop()
        if s is not None:
            try:
                s.shutdown()
            finally:
                self._rec("device.state", state="stopped")
        return {"state": "stopped"}


__all__ = ["DeviceService", "is_enabled", "set_enabled", "MIN_FREE_BYTES", "Path"]
