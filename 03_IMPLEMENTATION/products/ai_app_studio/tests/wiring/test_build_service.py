"""MCP build tool -> core/aas_build runner -> preview server -> build.* / preview.captured events (DESIGN 3.4, 4, 5)."""
from __future__ import annotations

import hashlib
import http.client
import threading
from pathlib import Path
from typing import Any

import pytest
from aas_build import service
from aas_build.preview_server import PreviewServer
from aas_build.runner import BuildError, BuildResult
from studio_core import activity
from studio_core.errors import Unavailable

from .conftest import OWNER

IMAGE = "sha256:" + "a" * 64
DOCKER = "C:/fake/docker.exe"
SEEN: dict[str, Any] = {}


def make_fake(marker: str = "v1", fail: str | None = None, hold: threading.Event | None = None):  # type: ignore[no-untyped-def]
    def fake(*, docker: str, build_id: str, ctx: Path, out: Path, image: str, dest: Path, **kw: Any) -> BuildResult:
        SEEN["ctx_files"] = sorted(p.relative_to(ctx).as_posix() for p in ctx.rglob("*") if p.is_file())
        SEEN["app"] = (ctx / "src" / "App.tsx").read_text(encoding="utf-8")
        SEEN["ctx"], SEEN["out"], SEEN["limits"], SEEN["args"] = ctx, out, kw.get("limits"), (docker, image)
        if hold is not None:
            hold.wait(10)
        if fail:
            raise BuildError(fail)
        dest.mkdir(parents=True)
        (dest / "index.html").write_text(f"<!doctype html><title>{marker}</title>", encoding="utf-8")
        return BuildResult(dest=dest, files=["index.html"], total_bytes=10,
                           dist_sha256=hashlib.sha256(marker.encode()).hexdigest(), log_tail="vite v8 building\nok 1 module")
    return fake


def new_project(studio) -> str:  # type: ignore[no-untyped-def]
    return studio.create_project(OWNER, "p1", "blank", "")["id"]


def kinds(studio) -> list[str]:  # type: ignore[no-untyped-def]
    return [e["kind"] for e in activity.list_events(studio, limit=500)]


def test_ctx_has_the_studio_kit_plus_only_the_project_src_and_public(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=make_fake(), docker=DOCKER, image=IMAGE)
    files = SEEN["ctx_files"]
    for must in ("package.json", "pnpm-lock.yaml", "pnpm-workspace.yaml", ".npmrc", "index.html", "vite.config.ts",
                 "tsconfig.json", "src/App.tsx", "src/main.tsx", "public/icon.svg"):
        assert must in files, must
    assert not any(f.startswith("node_modules") or f.startswith(".git") for f in files)
    assert "Hello from AI App Studio" in SEEN["app"]
    assert not str(SEEN["ctx"]).startswith(str(studio.settings.projects_dir))  # never mounted from the workspace
    assert SEEN["args"] == (DOCKER, IMAGE)
    assert not Path(SEEN["ctx"]).exists() and not Path(SEEN["out"]).exists()  # temp dirs removed


def test_events_in_order_and_preview_revision_carries_the_revision(studio) -> None:  # type: ignore[no-untyped-def]
    # S8: a web build announces preview.revision {revision} (the frames reload); preview.captured is reserved for real
    # PNG captures (preview_capture), whose sha256 the UI fetches as a thumbnail. The dist digest is in build.finished.
    pid = new_project(studio)
    r = service.build_web(studio, pid, run_build=make_fake("v1"), docker=DOCKER, image=IMAGE)
    assert r["ok"] is True and r["url"].startswith(f"http://p-{pid[4:]}.localhost:")
    ev = [e for e in activity.list_events(studio, limit=500) if e["projectId"] == pid and e["kind"].startswith(("build", "preview"))]
    ks = [e["kind"] for e in ev]
    assert ks[0] == "build.started" and ks[-1] == "preview.revision" and "build.finished" in ks and "build.log" in ks
    assert "preview.captured" not in ks
    log = next(e for e in ev if e["kind"] == "build.log")
    assert "line" in log["payload"] and "vite v8" in log["payload"]["line"]
    assert ev[-1]["payload"] == {"revision": studio.get_revision(pid)}
    fin = next(e for e in ev if e["kind"] == "build.finished")
    assert fin["payload"]["status"] == "ok"
    assert fin["payload"]["artifactSha256"] == hashlib.sha256(b"v1").hexdigest()


def test_failed_build_emits_error_and_failed_finish_and_keeps_the_last_good_site(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=make_fake("good"), docker=DOCKER, image=IMAGE)
    good = service.site_root_for(studio.settings)(pid[4:])
    assert good is not None and "good" in (good / "index.html").read_text()
    with pytest.raises(service.BuildFailed, match="boom"):
        service.build_web(studio, pid, run_build=make_fake(fail="boom: src/App.tsx(3,1): error TS2304"),
                          docker=DOCKER, image=IMAGE)
    evs = activity.list_events(studio, limit=500)
    err = [e for e in evs if e["kind"] == "build.error"]
    assert err and "boom" in err[-1]["payload"]["message"]
    assert [e for e in evs if e["kind"] == "build.finished"][-1]["payload"]["status"] == "failed"
    assert service.site_root_for(studio.settings)(pid[4:]) == good  # preview still serves the last good build
    assert not any(e["kind"] in ("preview.captured", "preview.revision") for e in evs[-3:])


def test_one_build_at_a_time(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    hold = threading.Event()
    t = threading.Thread(target=lambda: service.build_web(studio, pid, run_build=make_fake(hold=hold), docker=DOCKER,
                                                          image=IMAGE))
    t.start()
    try:
        deadline = 100
        while "ctx" not in SEEN or not Path(SEEN["ctx"]).exists():
            deadline -= 1
            assert deadline > 0
            threading.Event().wait(0.05)
        with pytest.raises(Unavailable, match="already running"):
            service.build_web(studio, pid, run_build=make_fake(), docker=DOCKER, image=IMAGE)
    finally:
        hold.set()
        t.join(20)


def test_site_root_for_rejects_bad_ids_and_old_builds_are_pruned(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    root_for = service.site_root_for(studio.settings)
    for bad in ("", "../x", "ZZZZ", "0" * 15, "0" * 17):
        assert root_for(bad) is None
    assert root_for(pid[4:]) is None  # nothing built yet
    for i in range(4):
        service.build_web(studio, pid, run_build=make_fake(f"v{i}"), docker=DOCKER, image=IMAGE)
    base = studio.settings.data_dir / "preview" / pid
    assert len([p for p in base.iterdir() if p.is_dir()]) <= 2
    assert "v3" in (root_for(pid[4:]) / "index.html").read_text()


def test_preview_server_serves_the_built_site_and_reloads_after_a_rebuild(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    srv = PreviewServer(service.site_root_for(studio.settings), port=0).start()
    try:
        def get(path: str = "/") -> tuple[int, str]:
            c = http.client.HTTPConnection("127.0.0.1", srv.port, timeout=10)
            c.request("GET", path, headers={"Host": f"p-{pid[4:]}.localhost:{srv.port}"})
            r = c.getresponse()
            return r.status, r.read().decode()
        assert get()[0] == 404  # nothing built yet
        service.build_web(studio, pid, run_build=make_fake("first"), docker=DOCKER, image=IMAGE)
        st, body = get()
        assert st == 200 and "first" in body
        service.build_web(studio, pid, run_build=make_fake("second"), docker=DOCKER, image=IMAGE)
        assert "second" in get()[1]
    finally:
        srv.stop()


def test_preview_url_uses_the_configured_preview_port(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    r = service.build_web(studio, pid, run_build=make_fake(), docker=DOCKER, image=IMAGE)
    assert r["url"] == f"http://p-{pid[4:]}.localhost:{studio.settings.preview_port}/"
    assert kinds(studio).count("build.started") == 1
