"""Device tools service (start phone, install, tap, screenshot) in front of the S5 launcher's public API.

Gates, all fail closed and checked before any ``wsl.exe`` is started:
  1. the owner setting ``device_enabled`` (default OFF; only the authenticated owner can change it);
  2. free space on the system and LOCALAPPDATA drives >= 8 GiB (the S5 emulator crash-looped and filled C: with WSL dumps);
  3. the WSL preflight (networking mode nat, pinned scripts, only aas-* AVDs).
The only thing imported from ``apps.device_host`` is ``launcher`` (``preflight``, ``DeviceSession``); this module
never builds an argv and never calls ``wsl.exe``. Screenshots are saved under ``<data>/device/shots/<sha256>.png``
and only their digest and size are returned to the agent.
"""
from __future__ import annotations

import hashlib
import os
import re
import shutil
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
                 session_factory: Callable[[str], Any] | None = None) -> None:
        self.studio = studio
        self._free = free_bytes
        if preflight is None or session_factory is None:
            from apps.device_host import launcher
            preflight = preflight or launcher.preflight
            session_factory = session_factory or launcher.DeviceSession
        self._preflight, self._factory = preflight, session_factory
        self.session: Any = None

    def _gate(self) -> None:
        if not is_enabled(self.studio):
            raise Unavailable("device tools are disabled: the owner has not enabled them in the studio settings")
        free = self._free()
        if free < MIN_FREE_BYTES:
            raise Unavailable(f"device tools are disabled: less than 8 GiB free on the system drive ({free // 2**20} MiB)")
        try:
            self._preflight()
        except Exception as e:  # noqa: BLE001 - LauncherRefused, Tampered, DeviceError, OSError: all fail closed
            raise Unavailable(f"device tools are disabled: WSL preflight failed ({type(e).__name__}: {str(e)[:160]})") from e

    def _rec(self, kind: str, **payload: Any) -> None:
        try:
            activity.record(self.studio, kind, source="device", payload=payload)
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
        return self.session

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

    def install(self, project_id: str) -> dict[str, Any]:
        s = self._need()
        if not isinstance(project_id, str) or not _PID.match(project_id):
            raise ValidationFailed("invalid project id")
        apk = self.studio.settings.data_dir / "preview" / project_id / "app-debug.apk"
        if not apk.is_file() or apk.is_symlink():
            raise Unavailable("no APK has been built for this project (the Android build is not available yet)")
        self._rec("device.log", text=f"install {apk.name}")
        return s.install(apk)

    def stop(self) -> dict[str, Any]:
        s, self.session = self.session, None
        if s is not None:
            s.shutdown()
            self._rec("device.state", state="stopped")
        return {"state": "stopped"}


__all__ = ["DeviceService", "is_enabled", "set_enabled", "MIN_FREE_BYTES", "Path"]
