"""S9b item 2 - export (web zip / debug APK) needs the owner's approval bound to the artifact sha256, writes into the
owner's export folder (default %USERPROFILE%\\Downloads\\AIAppStudio\\<project>), and a final secret scan of the exact
artifact bytes blocks the export on a key pattern (P7d, with the clean-project positive control)."""
from __future__ import annotations

import hashlib
import io
import os
import threading
import time
import zipfile
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import pytest
from aas_build import export, service
from starlette.testclient import TestClient
from studio_core import activity, plans, project_kind
from studio_core.config import Settings
from studio_core.errors import ApprovalRequired, PolicyDenied, Unavailable, ValidationFailed
from studio_core.policy import POLICY, Actor, Decision

from .conftest import OWNER
from .test_build_service import DOCKER, IMAGE, make_fake
from .test_s9b_sight import load_server

AGENT = Actor("agent", "runner:run_0123456789abcdef", "mcp")
KEY = "sk-ant-api03-" + "Q7x" * 30


def built(studio: Any, marker: str = "v1", name: str = "My App") -> str:  # type: ignore[no-untyped-def]
    pid = studio.create_project(OWNER, name, "blank", "")["id"]
    service.build_web(studio, pid, run_build=make_fake(marker), docker=DOCKER, image=IMAGE)
    return pid


def site(studio: Any, pid: str) -> Path:
    p = service.site_root_for(studio.settings)(pid[4:])
    assert p is not None
    return p


def approve(studio: Any, apr: str) -> None:
    plans.decide_approval(studio, OWNER, apr, "approve")


def two_step(studio: Any, pid: str, target: str, dest: Path) -> dict[str, Any]:
    with pytest.raises(ApprovalRequired) as ei:
        export.export(studio, OWNER, pid, target, dest_root=dest)
    d = ei.value.details
    assert d["sha256"] and d["approvalId"].startswith("apr_")
    approve(studio, d["approvalId"])
    return export.export(studio, OWNER, pid, target, approval_id=d["approvalId"], dest_root=dest)


def test_policy_export_needs_approval_for_owner_and_agent() -> None:
    assert POLICY["app.export"] == (Decision.APPROVAL, Decision.APPROVAL)


def test_web_export_two_step_deterministic_zip_dist_only(studio, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    (site(studio, pid) / "assets").mkdir()
    (site(studio, pid) / "assets" / "app.js").write_text("console.log(1)", encoding="utf-8")
    a1 = export.prepare(studio, pid, "web")
    time.sleep(1.1)
    os.utime(site(studio, pid) / "index.html")
    a2 = export.prepare(studio, pid, "web")
    assert a1.data == a2.data and a1.sha256 == hashlib.sha256(a1.data).hexdigest()  # deterministic
    with zipfile.ZipFile(io.BytesIO(a1.data)) as z:
        assert z.namelist() == ["assets/app.js", "index.html"]
        assert all(i.date_time == (1980, 1, 1, 0, 0, 0) for i in z.infolist())
    res = two_step(studio, pid, "web", tmp_path / "exports")
    out = Path(res["path"])
    assert out.parent == tmp_path / "exports" / f"My-App-{pid[4:]}"
    assert out.suffix == ".zip" and hashlib.sha256(out.read_bytes()).hexdigest() == res["sha256"] == a1.sha256


def test_approval_is_bound_to_the_artifact_sha(studio, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    with pytest.raises(ApprovalRequired) as ei:
        export.export(studio, OWNER, pid, "web", dest_root=tmp_path)
    apr = ei.value.details["approvalId"]
    approve(studio, apr)
    (site(studio, pid) / "index.html").write_text("<!doctype html><title>changed after approval</title>", encoding="utf-8")
    with pytest.raises(ApprovalRequired):
        export.export(studio, OWNER, pid, "web", approval_id=apr, dest_root=tmp_path)
    assert not any(tmp_path.rglob("*.zip"))


def test_p7d_planted_key_blocks_export_clean_project_exports(studio, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    clean = built(studio)
    assert two_step(studio, clean, "web", tmp_path)["sha256"]  # positive control
    dirty = built(studio, "v2", "Dirty App")
    (site(studio, dirty) / "app.js").write_text(f'const k = "{KEY}";', encoding="utf-8")
    n_apr = len(studio.list_approvals(None))
    with pytest.raises(export.ExportBlocked) as ei:
        export.export(studio, OWNER, dirty, "web", dest_root=tmp_path)
    assert "anthropic-key" in str(ei.value.details) and KEY not in str(ei.value.to_dict())
    assert len(studio.list_approvals(None)) == n_apr  # no approval is even requested
    assert not (tmp_path / f"Dirty-App-{dirty[4:]}").exists()


@pytest.mark.parametrize("planted", [b"-----BEGIN RSA PRIVATE KEY-----\nMIIB", b"AKIAABCDEFGHIJKLMNOP", "utf16"])
def test_scan_finds_keys_in_raw_and_utf16_bytes(planted: Any) -> None:
    data = (" " + "ghp_" + "a1B2" * 9).encode("utf-16-le") if planted == "utf16" else b"\0\1" + planted + b"\0"
    assert export.scan_bytes(data)
    assert export.scan_bytes(b"function a(){return 'token: x'}") == []


def test_web_export_refuses_links_in_the_site(studio, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    target = tmp_path / "outside.txt"
    target.write_text("owner data", encoding="utf-8")
    try:
        os.symlink(target, site(studio, pid) / "leak.txt")
    except OSError:
        pytest.skip("symlinks need developer mode / privilege on this machine")
    with pytest.raises((Unavailable, ValidationFailed)):
        export.prepare(studio, pid, "web")


def apk_bytes(extra: bytes = b"") -> bytes:
    b = io.BytesIO()
    with zipfile.ZipFile(b, "w") as z:
        z.writestr("AndroidManifest.xml", b"\x03\x00manifest")
        z.writestr("classes.dex", b"dex\n035\0" + extra)
    return b.getvalue()


def record_apk(studio: Any, pid: str, data: bytes) -> None:
    p = service.apk_path(studio.settings, pid)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_bytes(data)
    sha = hashlib.sha256(data).hexdigest()
    activity.record(studio, "build.finished", project_id=pid, source="build", artifact_sha256=sha,
                    payload={"target": "android", "status": "ok", "artifactSha256": sha})


def test_android_export_needs_a_recorded_build_and_scans_entries(studio, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    with pytest.raises(Unavailable):
        export.prepare(studio, pid, "android")  # no APK
    service.apk_path(studio.settings, pid).write_bytes(apk_bytes())
    with pytest.raises(Unavailable):
        export.prepare(studio, pid, "android")  # APK without a recorded build
    record_apk(studio, pid, apk_bytes())
    res = two_step(studio, pid, "android", tmp_path)
    assert res["path"].endswith(".apk") and Path(res["path"]).read_bytes() == apk_bytes()
    record_apk(studio, pid, apk_bytes(b"AIza" + b"S" * 35))  # planted Google key inside a compressed entry
    with pytest.raises(export.ExportBlocked):
        export.export(studio, OWNER, pid, "android", dest_root=tmp_path)


def test_agent_cannot_self_approve(studio, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    with pytest.raises(ApprovalRequired) as ei:
        export.export(studio, AGENT, pid, "web", dest_root=tmp_path)
    with pytest.raises(PolicyDenied):
        studio.decide_approval(AGENT, ei.value.details["approvalId"], True)


def test_export_folder_setting_owner_only_and_validated(studio, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    assert export.export_root(studio) == Path(os.path.expandvars(r"%USERPROFILE%")) / "Downloads" / "AIAppStudio"
    with pytest.raises(PolicyDenied):
        export.set_export_root(studio, AGENT, str(tmp_path))
    for bad in ("relative\\dir", str(studio.settings.data_dir / "x"), str(studio.settings.projects_dir)):
        with pytest.raises(ValidationFailed):
            export.set_export_root(studio, OWNER, bad)
    export.set_export_root(studio, OWNER, str(tmp_path / "mine"))
    assert export.export_root(studio) == tmp_path / "mine"


# ---------------------------------------------------------------- MCP tool (in process)
def test_app_export_tool_waits_for_the_owner_then_writes(studio, monkeypatch, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    export.set_export_root(studio, OWNER, str(tmp_path / "out"))
    mod = load_server(monkeypatch, studio)
    monkeypatch.setenv("AAS_PLAN_WAIT_SECONDS", "10")

    def owner() -> None:
        for _ in range(100):
            pend = [a for a in studio.list_approvals("pending") if a["operation"] == "app.export"]
            if pend:
                approve(studio, pend[0]["approvalId"])
                return
            time.sleep(0.05)
    t = threading.Thread(target=owner)
    t.start()
    res = mod.app_export(projectId=pid, target="web")
    t.join()
    body = res.structured_content
    assert body["ok"] is True, body
    assert Path(body["result"]["path"]).is_file() and body["result"]["status"] == "exported"


def test_app_export_tool_timeout_reports_pending_and_reuses_the_request(studio, monkeypatch, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    export.set_export_root(studio, OWNER, str(tmp_path / "out"))
    mod = load_server(monkeypatch, studio)
    monkeypatch.setenv("AAS_PLAN_WAIT_SECONDS", "0.5")
    r1 = mod.app_export(projectId=pid, target="web").structured_content["result"]
    assert r1["status"] == "pending"
    r2 = mod.app_export(projectId=pid, target="web").structured_content["result"]
    assert r2["approvalId"] == r1["approvalId"]  # same artifact -> same pending request, no flood
    approve(studio, r1["approvalId"])
    r3 = mod.app_export(projectId=pid, target="web").structured_content["result"]
    assert r3["status"] == "exported"


# ---------------------------------------------------------------- REST (owner)
@pytest.fixture()
def client(tmp_path: Path) -> Iterator[TestClient]:
    from apps.backend.main import create_app
    app = create_app(Settings(data_dir=tmp_path / "data", preview_port=0), "t" * 40)
    with TestClient(app, base_url="http://127.0.0.1:17875") as c:
        yield c
    project_kind.unregister("app")


AUTH = {"Authorization": "Bearer " + "t" * 40}


def test_rest_export_two_step_and_folder_setting(client: TestClient, tmp_path: Path) -> None:
    st = client.app.state.studio
    pid = client.post("/api/v1/projects", json={"name": "web one"}, headers=AUTH).json()["id"]
    service.build_web(st, pid, run_build=make_fake("r1"), docker=DOCKER, image=IMAGE)
    assert client.post(f"/api/v1/projects/{pid}/export", json={"target": "web"}).status_code == 401
    assert client.put("/api/v1/settings/export-folder", json={"path": str(tmp_path / "ex")}).status_code == 401
    assert client.put("/api/v1/settings/export-folder", json={"path": str(tmp_path / "ex")}, headers=AUTH).status_code == 200
    assert client.get("/api/v1/settings/export-folder", headers=AUTH).json()["path"] == str(tmp_path / "ex")
    r = client.post(f"/api/v1/projects/{pid}/export", json={"target": "web"}, headers=AUTH)
    assert r.status_code == 428, r.text
    apr = r.json()["details"]["approvalId"]
    assert client.post(f"/api/v1/approvals/{apr}/decide", json={"decision": "approve"}, headers=AUTH).status_code == 200
    r = client.post(f"/api/v1/projects/{pid}/export", json={"target": "web", "approvalId": apr}, headers=AUTH)
    assert r.status_code == 200, r.text
    assert Path(r.json()["path"]).is_file()
    assert client.post(f"/api/v1/projects/{pid}/export", json={"target": "ios"}, headers=AUTH).status_code == 422
