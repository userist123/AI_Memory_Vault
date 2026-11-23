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
"""
from __future__ import annotations

import atexit
import hashlib
import os
import re
import shutil
import threading
import time
from collections.abc import Callable
from pathlib import Path
from typing import Any

from studio_core import activity, audit
from studio_core.aas_models import AppSetting
from studio_core.errors import PolicyDenied, Unavailable, ValidationFailed
from studio_core.ids import utcnow
from studio_core.policy import Actor

SETTING_KEY = "device_enabled"
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
    roots = {os.environ.get("SystemRoot", r"C:\Windows")[:3]}
    lad = os.environ.get("LOCALAPPDATA")
    if lad and len(lad) > 2 and lad[1] == ":":
        roots.add(lad[:3])
    return min(shutil.disk_usage(r).free for r in roots)


class DeviceService:
    def __init__(self, studio: Any, *, preflight: Callable[[], dict[str, Any]] | None = None,
                 free_bytes: Callable[[], int] = _free_bytes,
                 session_factory: Callable[[str], Any] | None = None, alive: Callable[[], bool] | None = None,
                 run_id: str | None = None) -> None:
        self.studio = studio
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

    def start(self, form: str = "phone") -> dict[str, Any]:
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
        s = self._need()
        x, y = self._coord(x), self._coord(y)
        self._rec("device.input", action="tap", x=x, y=y)
        return s.tap(x, y)

    def screenshot(self) -> dict[str, Any]:
        s = self._need()
        png = s.screenshot()
        sha = hashlib.sha256(png).hexdigest()
        d = self.studio.settings.data_dir / "device" / "shots"
        d.mkdir(parents=True, exist_ok=True)
        (d / f"{sha}.png").write_bytes(png)
        self._rec("device.log", text=f"screenshot {sha[:12]} {len(png)} bytes")
        return {"sha256": sha, "size": len(png)}

    def install(self, project_id: str, *, launch: bool = False) -> dict[str, Any]:
        """Install the project's debug APK (the device host accepts only dev.aiappstudio.p<16 hex>); with ``launch``
        the installed package must be exactly the project's package id and is then started."""
        s = self._need()
        if not isinstance(project_id, str) or not _PID.match(project_id):
            raise ValidationFailed("invalid project id")
        apk = self.studio.settings.data_dir / "preview" / project_id / "app-debug.apk"
        if not apk.is_file() or apk.is_symlink():
            raise Unavailable("no APK has been built for this project (call app_build with target android first)")
        sha = hashlib.sha256(apk.read_bytes()).hexdigest()
        self._rec("device.log", text=f"install {apk.name} sha256 {sha[:12]}")
        rep = s.install(apk)
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

    def stop(self) -> dict[str, Any]:
        with self._lock:
            s, self.session = self.session, None
        if s is not None:
            try:
                s.shutdown()
            finally:
                self._rec("device.state", state="stopped")
        return {"state": "stopped"}


__all__ = ["DeviceService", "is_enabled", "set_enabled", "MIN_FREE_BYTES", "Path"]
