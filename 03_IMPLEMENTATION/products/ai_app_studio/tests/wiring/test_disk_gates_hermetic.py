"""The free-disk gates read the hermetic ``disk_free`` probe (tests/hermetic_disk.py): each gate refuses below its
threshold and passes above it (negative + positive control), and the fake really replaces the real drive value."""
from __future__ import annotations

import shutil
from pathlib import Path

import pytest
from aas_build import capture, service
from aas_device import service as dsvc
from studio_core.config import Settings
from studio_core.errors import Unavailable

from tests.hermetic_disk import FAKE_FREE_BYTES

GIB = 1024 ** 3


def test_the_fake_is_active_and_keeps_total(disk_free, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    u = shutil.disk_usage(tmp_path)
    assert u.free == FAKE_FREE_BYTES and u.total > 0
    disk_free["free"] = 123
    assert shutil.disk_usage(tmp_path).free == 123


@pytest.mark.real_disk
def test_real_disk_marker_reads_the_real_drive(tmp_path: Path) -> None:
    assert shutil.disk_usage(tmp_path).free != FAKE_FREE_BYTES


def test_build_gate(disk_free, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    settings = Settings(data_dir=tmp_path / "data")
    disk_free["free"] = service.MIN_FREE_BYTES - 1
    with pytest.raises(Unavailable, match="5 GiB"):
        service._disk_gate(settings, None)
    disk_free["free"] = service.MIN_FREE_BYTES
    service._disk_gate(settings, None)  # positive control


def test_desktop_drive_probe(disk_free, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    disk_free["free"] = service.DESKTOP_MIN_FREE_DEFAULT_ROOT - 1
    assert service._desktop_drive_free(tmp_path / "missing" / "desktop") < service.DESKTOP_MIN_FREE_DEFAULT_ROOT
    disk_free["free"] = service.DESKTOP_MIN_FREE_DEFAULT_ROOT
    assert service._desktop_drive_free(tmp_path) >= service.DESKTOP_MIN_FREE_DEFAULT_ROOT


def test_capture_gate(disk_free, studio) -> None:  # type: ignore[no-untyped-def]
    disk_free["free"] = capture.MIN_FREE_BYTES - 1
    assert capture._free_bytes(capture.captures_dir(studio.settings)) < capture.MIN_FREE_BYTES
    disk_free["free"] = capture.MIN_FREE_BYTES
    assert capture._free_bytes(capture.captures_dir(studio.settings)) >= capture.MIN_FREE_BYTES


def test_device_gate_default_probe(disk_free) -> None:  # type: ignore[no-untyped-def]
    disk_free["free"] = dsvc.MIN_FREE_BYTES - 1
    assert dsvc._free_bytes() < dsvc.MIN_FREE_BYTES
    disk_free["free"] = 20 * GIB
    assert dsvc._free_bytes() >= dsvc.MIN_FREE_BYTES
