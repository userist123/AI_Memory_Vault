"""Hermetic free-disk probes for the whole suite (loaded with ``-p tests.hermetic_disk`` from pyproject.toml; kept
out of the vendored tests/conftest.py)."""
from __future__ import annotations

import shutil

import pytest

FAKE_FREE_BYTES = 1 << 40  # 1 TiB: what every free-disk probe sees unless a test sets its own value


@pytest.fixture(autouse=True)
def disk_free(request: pytest.FixtureRequest, monkeypatch: pytest.MonkeyPatch) -> dict[str, int] | None:
    """Hermetic free-disk probes. The build gate (service._free_bytes), the desktop drive gates
    (service._desktop_drive_free and the desktop-root check), the capture gate (capture._free_bytes) and the device gate
    (aas_device.service._free_bytes) all read ``shutil.disk_usage``; here it reports the real total/used and
    ``disk_free["free"]`` (default FAKE_FREE_BYTES), so the suite does not depend on the owner's real C: free space.
    Gate tests set ``disk_free["free"]`` low / high (tests/wiring/test_disk_gates_hermetic.py). Production gates are
    unchanged. Not applied to tests/local_gate (real machine checks) or tests marked ``real_disk``."""
    if "local_gate" in request.node.path.parts or request.node.get_closest_marker("real_disk"):
        return None
    state = {"free": FAKE_FREE_BYTES}
    real = shutil.disk_usage

    def fake(path):  # type: ignore[no-untyped-def]
        u = real(path)
        return type(u)(u.total, u.used, state["free"])

    monkeypatch.setattr(shutil, "disk_usage", fake)
    return state
