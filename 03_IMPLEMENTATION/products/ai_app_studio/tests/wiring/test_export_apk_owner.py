"""Owner report 2026-11-23 "Nu merge sa o export APK": Export APK answered 503 "no APK has been built for this project"
(the owner had no way to build one) and, where an APK existed, it was the one of an OLDER revision. Fixed flow: export
-> 409 build_required -> owner build (background, no agent plan gate) -> 428 approval bound to the APK sha256 ->
approve -> written to the export folder -> the folder can be opened (path computed server-side)."""
from __future__ import annotations

import functools
import hashlib
import time
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import pytest
from aas_build import export, owner_build, service
from starlette.testclient import TestClient
from studio_core import activity, plans, project_kind
from studio_core.config import Settings
from studio_core.errors import ConflictError, Unavailable

from .conftest import OWNER
from .test_build_service import DOCKER, IMAGE, make_fake
from .test_s7_android_capture import fake_android, fake_keys
from .test_s9b_export import apk_bytes, built

AUTH = {"Authorization": "Bearer " + "t" * 40}


def fake_builds(b: owner_build.OwnerBuilds, marker: str = "v1", fail: str | None = None) -> None:
    b._web = functools.partial(service.build_web, run_build=make_fake(marker), docker=DOCKER, image=IMAGE)  # noqa: SLF001
    b._android = functools.partial(service.build_android, run_build=fake_android(fail), ensure_keys=fake_keys,  # noqa: SLF001
                                   docker=DOCKER, image=IMAGE)


def wait(b: owner_build.OwnerBuilds, pid: str, timeout: float = 20) -> dict[str, Any]:
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        st = b.status(pid)
        if st["state"] != "building":
            return st
        time.sleep(0.05)
    raise AssertionError("build did not finish")


def bump(studio: Any, pid: str) -> None:
    rev = studio.get_revision(pid)
    studio.write_file(OWNER, pid, rev, "src/Extra.tsx", "export const x = 1;\n")


# ---------------------------------------------------------------- export.prepare
def test_missing_apk_is_build_required_409(studio) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    with pytest.raises(export.BuildRequired) as ei:
        export.prepare(studio, pid, "android")
    assert ei.value.http_status == 409 and ei.value.code == "build_required"
    assert isinstance(ei.value, Unavailable)  # older callers (MCP app_export) keep their contract


def test_apk_of_an_older_revision_is_not_exported(studio) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    rev = studio.get_revision(pid)
    data = apk_bytes()
    service.apk_path(studio.settings, pid).write_bytes(data)
    sha = hashlib.sha256(data).hexdigest()
    activity.record(studio, "build.finished", project_id=pid, source="build", artifact_sha256=sha,
                    payload={"target": "android", "status": "ok", "revision": rev, "artifactSha256": sha})
    assert export.prepare(studio, pid, "android").sha256 == sha  # [+] same revision exports
    bump(studio, pid)
    with pytest.raises(export.BuildRequired) as ei:
        export.prepare(studio, pid, "android")
    assert ei.value.details["builtRevision"] == rev


# ---------------------------------------------------------------- OwnerBuilds
def test_owner_android_build_rebuilds_a_stale_web_site_first(studio) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    bump(studio, pid)  # the preview site is now older than the app
    b = owner_build.OwnerBuilds(studio, OWNER, busy=lambda: None)
    fake_builds(b, marker="v2")
    assert b.start(pid, "android")["state"] == "building"
    st = wait(b, pid)
    assert st["state"] == "ok", st
    assert owner_build.web_revision(studio, pid) == studio.get_revision(pid)
    art = export.prepare(studio, pid, "android")
    assert art.revision == studio.get_revision(pid)


def test_owner_build_refused_while_a_run_is_alive(studio) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    b = owner_build.OwnerBuilds(studio, OWNER, busy=lambda: pid)
    fake_builds(b)
    with pytest.raises(ConflictError):
        b.start(pid, "android")
    assert b.status(pid)["state"] == "idle"


def test_owner_build_failure_is_reported(studio) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    b = owner_build.OwnerBuilds(studio, OWNER, busy=lambda: None)
    fake_builds(b, fail="gradle exploded")
    b.start(pid, "android")
    st = wait(b, pid)
    assert st["state"] == "failed" and "gradle exploded" in st["message"]


# ---------------------------------------------------------------- REST end to end (owner)
@pytest.fixture()
def client(tmp_path: Path) -> Iterator[TestClient]:
    from apps.backend.main import create_app
    app = create_app(Settings(data_dir=tmp_path / "data", preview_port=0), "t" * 40)
    with TestClient(app, base_url="http://127.0.0.1:17875") as c:
        yield c
    project_kind.unregister("app")


def test_rest_export_apk_builds_then_asks_approval_then_writes(client: TestClient, tmp_path: Path) -> None:
    st = client.app.state.studio
    builds = client.app.state.owner_builds
    fake_builds(builds)
    opened: list[str] = []
    client.app.state.open_folder = opened.append
    assert client.put("/api/v1/settings/export-folder", json={"path": str(tmp_path / "ex")}, headers=AUTH).status_code == 200
    pid = client.post("/api/v1/projects", json={"name": "Notes"}, headers=AUTH).json()["id"]
    service.build_web(st, pid, run_build=make_fake("r1"), docker=DOCKER, image=IMAGE)

    r = client.post(f"/api/v1/projects/{pid}/export", json={"target": "android"}, headers=AUTH)
    assert r.status_code == 409 and r.json()["error"] == "build_required", r.text  # the owner's verbatim failure, now 409

    assert client.post(f"/api/v1/projects/{pid}/build", json={"target": "android"}).status_code == 401
    assert client.post(f"/api/v1/projects/{pid}/build", json={"target": "ios"}, headers=AUTH).status_code == 422
    r = client.post(f"/api/v1/projects/{pid}/build", json={"target": "android"}, headers=AUTH)
    assert r.status_code == 202 and r.json()["state"] == "building", r.text
    assert wait(builds, pid)["state"] == "ok"
    assert client.get(f"/api/v1/projects/{pid}/build", headers=AUTH).json()["state"] == "ok"

    assert client.post(f"/api/v1/projects/{pid}/export/open-folder", headers=AUTH).status_code == 404  # nothing yet
    r = client.post(f"/api/v1/projects/{pid}/export", json={"target": "android"}, headers=AUTH)
    assert r.status_code == 428, r.text
    d = r.json()["details"]
    apk = service.apk_path(st.settings, pid).read_bytes()
    assert d["sha256"] == hashlib.sha256(apk).hexdigest()
    assert client.post(f"/api/v1/approvals/{d['approvalId']}/decide", json={"decision": "approve"}, headers=AUTH).status_code == 200
    # approved from the Approvals card first: asking again reuses the approved grant (the UI must not decide it twice)
    r = client.post(f"/api/v1/projects/{pid}/export", json={"target": "android"}, headers=AUTH)
    assert r.status_code == 428 and r.json()["details"]["status"] == "approved"
    r = client.post(f"/api/v1/projects/{pid}/export", json={"target": "android", "approvalId": d["approvalId"]}, headers=AUTH)
    assert r.status_code == 200, r.text
    out = Path(r.json()["path"])
    assert out.parent == tmp_path / "ex" / f"Notes-{pid[4:]}" and out.read_bytes() == apk

    r = client.post(f"/api/v1/projects/{pid}/export/open-folder", headers=AUTH)
    assert r.status_code == 200 and opened == [str(out.parent)]
    assert client.post(f"/api/v1/projects/{pid}/export/open-folder").status_code == 401
    assert client.post("/api/v1/projects/prj_zz/export/open-folder", headers=AUTH).status_code == 422
