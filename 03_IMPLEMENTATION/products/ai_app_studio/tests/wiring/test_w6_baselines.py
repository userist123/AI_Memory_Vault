"""W6 regression protection: owner-approved baselines per route x frame, perceptual diff (identical / small change /
large change / masked volatile region), agent warnings for unrelated changes, owner-only REST, acceptance checklist
with the W3 flow-result contract, quotas and disk gate."""
from __future__ import annotations

import io
import json
from pathlib import Path
from typing import Any

import pytest
from aas_build import baselines, capture, service
from PIL import Image, ImageDraw
from studio_core.errors import PolicyDenied, Unavailable, ValidationFailed
from studio_core.policy import Actor

from .conftest import OWNER
from .test_build_service import DOCKER, IMAGE, make_fake

AGENT = Actor("agent", "a1", "mcp")
BIG = lambda: 50 * 1024 ** 3  # noqa: E731 - plenty of free disk


def png(size: tuple[int, int], draw: list[tuple[tuple[int, int, int, int], tuple[int, int, int]]] | None = None,
        bg: tuple[int, int, int] = (245, 245, 245)) -> bytes:
    im = Image.new("RGB", size, bg)
    d = ImageDraw.Draw(im)
    d.text((20, 20), "Hello studio", fill=(20, 20, 20))
    for box, col in draw or []:
        d.rectangle(box, fill=col)
    buf = io.BytesIO()
    im.save(buf, "PNG")
    return buf.getvalue()


# ---------------------------------------------------------------- pure diff
def test_identical_images_have_no_change() -> None:
    a = png((390, 844))
    d = baselines.diff_images(a, a)
    assert d["changedPct"] == 0 and d["changedPixels"] == 0 and d["bbox"] is None and not d["sizeChanged"]
    assert d["diffPng"].startswith(b"\x89PNG")


def test_small_change_is_detected_and_located() -> None:
    a = png((390, 844))
    b = png((390, 844), [((100, 300, 160, 320), (30, 120, 220))])  # a 61x21 button
    d = baselines.diff_images(a, b)
    assert 0.1 < d["changedPct"] < 1.0
    x, y, w, h = d["bbox"]
    assert 97 <= x <= 100 and 297 <= y <= 300 and 61 <= w <= 67 and 21 <= h <= 27
    diff = Image.open(io.BytesIO(d["diffPng"])).convert("RGB")
    assert diff.getpixel((130, 310)) == (230, 20, 40)  # changed pixels red
    assert diff.getpixel((300, 700)) != (230, 20, 40)


def test_large_change_and_size_change() -> None:
    a = png((390, 844))
    b = png((390, 844), bg=(20, 20, 30))  # dark mode everywhere
    assert baselines.diff_images(a, b)["changedPct"] > 90
    c = baselines.diff_images(a, png((390, 900)))
    assert c["sizeChanged"] and c["changedPct"] > 5


def test_antialias_noise_below_tolerance_is_ignored() -> None:
    a = png((390, 844))
    b = png((390, 844), bg=(240, 240, 240))  # 5-level background shift: not a visible change
    assert baselines.diff_images(a, b)["changedPct"] == 0


def test_masked_volatile_region_is_excluded() -> None:
    a = png((390, 844), [((200, 10, 380, 40), (0, 0, 0))])       # "12:01"
    b = png((390, 844), [((200, 10, 380, 40), (200, 30, 30))])   # "12:02"
    assert baselines.diff_images(a, b)["changedPct"] > 1
    d = baselines.diff_images(a, b, [(198, 8, 186, 36)])
    assert d["changedPct"] == 0 and d["comparedPixels"] < 390 * 844


def test_masks_from_the_page_are_untrusted_and_clamped() -> None:
    raw = [[-5, -5, 20, 20], [10, 10, 1e9, 5], ["x", 1, 1, 1], [1, 2, 3], [float("nan"), 0, 5, 5], [5, 5, 0, 9],
           *([[1, 1, 1, 1]] * 100)]
    m = baselines.clean_masks(raw, 390, 844)
    assert m[0] == (0, 0, 15, 15) and len(m) <= baselines.MAX_MASKS
    assert all(x >= 0 and y >= 0 and x + w <= 390 and y + h <= 844 for x, y, w, h in m)
    assert baselines.clean_masks("nope", 10, 10) == []


def test_non_png_is_refused() -> None:
    with pytest.raises(ValidationFailed):
        baselines.diff_images(b"<html>", png((10, 10)))


# ---------------------------------------------------------------- approve / check (fake browser)
class Screens:
    """A fake snap: one PNG per frame per route; tests mutate ``extra`` (drawn rects) and ``masks``."""

    def __init__(self) -> None:
        self.extra: dict[str, list[Any]] = {}
        self.masks: dict[str, list[Any]] = {}
        self.urls: list[str] = []

    def __call__(self, url: str, frames: list[str]) -> tuple[dict[str, bytes], dict[str, list[Any]]]:
        self.urls.append(url)
        route = "/" + url.split("/", 3)[3]
        return ({f: png(capture.FRAMES[f], self.extra.get(f"{route}@{f}")) for f in frames},
                {f: self.masks.get(f, []) for f in frames})


@pytest.fixture()
def built(studio):  # type: ignore[no-untyped-def]
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    service.build_web(studio, pid, run_build=make_fake("w1"), docker=DOCKER, image=IMAGE)
    return studio, pid


def test_check_without_baseline_does_not_capture(built) -> None:  # type: ignore[no-untyped-def]
    studio, pid = built
    s = Screens()
    res = baselines.check(studio, pid, snap=s, free_bytes=BIG)
    assert res["status"] == "no_baseline" and s.urls == []


def test_only_the_owner_approves(built) -> None:  # type: ignore[no-untyped-def]
    studio, pid = built
    with pytest.raises(PolicyDenied):
        baselines.approve(studio, AGENT, pid, snap=Screens(), free_bytes=BIG)
    with pytest.raises(PolicyDenied):
        baselines.set_checklist(studio, AGENT, pid, {"flows": []})


def test_approve_then_identical_small_large_and_masked(built) -> None:  # type: ignore[no-untyped-def]
    studio, pid = built
    s = Screens()
    st = baselines.approve(studio, OWNER, pid, routes=["/", "/settings"], snap=s, free_bytes=BIG)
    assert len(st["baselines"]) == 6 and {b["frame"] for b in st["baselines"]} == set(capture.FRAMES)
    assert s.urls[0] == f"http://p-{pid[4:]}.localhost:{studio.settings.preview_port}/"
    assert s.urls[1].endswith("/settings")
    # identical
    res = baselines.check(studio, pid, snap=s, free_bytes=BIG)
    assert res["status"] == "same" and not res["lastCheck"]["warnings"]
    assert all(r["changedPct"] == 0 for r in res["lastCheck"]["results"])
    # small change on the screen the agent meant to change: changed, no warning
    s.extra["/@phone"] = [((100, 300, 160, 320), (30, 120, 220))]
    res = baselines.check(studio, pid, intended_routes=["/"], snap=s, free_bytes=BIG)
    row = next(r for r in res["lastCheck"]["results"] if r["key"] == "/@phone")
    assert res["status"] == "changed" and row["status"] == "changed" and row["diffSha256"]
    assert not res["lastCheck"]["warnings"]
    assert baselines.baseline_file(studio.settings, pid, row["diffSha256"]) is not None
    # large change on an UNRELATED screen: a warning for the agent
    s.extra["/settings@desktop"] = [((0, 0, 1440, 600), (10, 10, 10))]
    res = baselines.check(studio, pid, intended_routes=["/"], snap=s, free_bytes=BIG)
    assert any("/settings (desktop)" in w for w in res["lastCheck"]["warnings"])
    # the baselines never move on a check
    assert [b["sha256"] for b in res["baselines"]] == [b["sha256"] for b in st["baselines"]]
    # a volatile region marked in the page is masked
    s.extra = {"/@tablet": [((300, 10, 500, 40), (200, 0, 0))]}
    s.masks = {"tablet": [[290, 5, 220, 40]]}
    res = baselines.check(studio, pid, snap=s, free_bytes=BIG)
    assert res["status"] == "same", res["lastCheck"]["results"]
    # old current/diff PNGs are collected: only referenced files stay
    files = {p.stem for p in baselines.project_dir(studio.settings, pid).glob("*.png")}
    assert files == baselines._referenced(baselines.load_state(studio.settings, pid))  # noqa: SLF001


def test_threshold_is_owner_tunable(built) -> None:  # type: ignore[no-untyped-def]
    studio, pid = built
    s = Screens()
    baselines.approve(studio, OWNER, pid, frames=["phone"], snap=s, free_bytes=BIG)
    s.extra["/@phone"] = [((100, 300, 160, 320), (30, 120, 220))]
    baselines.set_threshold(studio, OWNER, pid, 5.0)
    assert baselines.check(studio, pid, snap=s, free_bytes=BIG)["status"] == "same"
    with pytest.raises(ValidationFailed):
        baselines.set_threshold(studio, OWNER, pid, 0)
    with pytest.raises(PolicyDenied):
        baselines.set_threshold(studio, AGENT, pid, 1.0)


def test_routes_are_validated(built) -> None:  # type: ignore[no-untyped-def]
    studio, pid = built
    for bad in (["//evil.example/"], ["http://x/"], ["/../x"], ["/a b"], [], ["/"] * 2, [f"/{i}" for i in range(9)]):
        with pytest.raises(ValidationFailed):
            baselines.approve(studio, OWNER, pid, routes=bad, snap=Screens(), free_bytes=BIG)


def test_disk_gate_and_quota(built, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    studio, pid = built
    with pytest.raises(Unavailable, match="5 GiB"):
        baselines.approve(studio, OWNER, pid, snap=Screens(), free_bytes=lambda: 1024)
    monkeypatch.setattr(baselines, "MAX_BYTES", 10)
    with pytest.raises(Unavailable, match="quota"):
        baselines.approve(studio, OWNER, pid, snap=Screens(), free_bytes=BIG)


def test_forget_project_removes_everything(built) -> None:  # type: ignore[no-untyped-def]
    studio, pid = built
    baselines.approve(studio, OWNER, pid, frames=["phone"], snap=Screens(), free_bytes=BIG)
    baselines.forget_project(studio.settings, pid)
    assert not baselines.project_dir(studio.settings, pid).exists()
    baselines.forget_project(studio.settings, "../x")  # bad ids are ignored


# ---------------------------------------------------------------- acceptance checklist + W3 contract
def test_checklist_roundtrip_with_flow_results(built) -> None:  # type: ignore[no-untyped-def]
    studio, pid = built
    cl = {"flows": [{"id": "add-note", "title": "Adaug o notiță", "statements": ["Butonul + deschide formularul",
                                                                             "Notița apare în listă"]},
                    {"id": "dark", "title": "Mod întunecat", "statements": ["Textul rămâne lizibil"]}]}
    out = baselines.set_checklist(studio, OWNER, pid, cl)
    assert [f["result"]["status"] for f in out["flows"]] == ["unknown", "unknown"]
    rd = Path(studio.settings.data_dir) / "flow_results"
    rd.mkdir(parents=True)
    (rd / f"{pid}.json").write_text(json.dumps({"results": [{"flow": "add-note", "status": "pass", "at": "t"},
                                                            {"flow": "dark", "status": "fail", "message": "contrast"},
                                                            {"flow": "x", "status": "weird"}]}), encoding="utf-8")
    got = baselines.get_checklist(studio.settings, pid)
    assert [f["result"]["status"] for f in got["flows"]] == ["pass", "fail"]
    assert got["flows"][1]["result"]["message"] == "contrast"
    (rd / f"{pid}.json").write_text("{broken", encoding="utf-8")
    assert baselines.get_checklist(studio.settings, pid)["flows"][0]["result"]["status"] == "unknown"
    # stored as studio data, not in the app sources
    assert (baselines.project_dir(studio.settings, pid) / "checklist.json").is_file()
    for bad in ({"flows": [{"id": "Bad Id", "title": "t", "statements": []}]},
                {"flows": [{"id": "a", "title": "t", "statements": ["x" * 301]}]},
                {"flows": [{"id": "a", "title": "t", "statements": []}, {"id": "a", "title": "u", "statements": []}]},
                {"flows": [{"id": "a", "title": "t", "statements": [], "run": "rm"}]}):
        with pytest.raises(ValidationFailed):
            baselines.set_checklist(studio, OWNER, pid, bad)


# ---------------------------------------------------------------- owner REST
def test_owner_routes(tmp_path, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    from apps.backend.main import create_app
    from fastapi.testclient import TestClient
    from studio_core.config import Settings
    tok = "t" * 40
    app = create_app(Settings(data_dir=tmp_path / "d"), tok)
    h = {"Authorization": f"Bearer {tok}"}
    s = Screens()
    real_approve, real_check = baselines.approve, baselines.check
    monkeypatch.setattr(baselines, "approve", lambda *a, **k: real_approve(*a, **k, snap=s, free_bytes=BIG))
    monkeypatch.setattr(baselines, "check", lambda *a, **k: real_check(*a, **k, snap=s, free_bytes=BIG))
    with TestClient(app, base_url="http://127.0.0.1:17875") as c:
        pid = c.post("/api/v1/projects", json={"name": "p"}, headers=h).json()["id"]
        studio = app.state.studio
        base = f"/api/v1/projects/{pid}"
        assert c.get(f"{base}/baselines").status_code in (401, 403)  # no token
        assert c.post(f"{base}/baselines/approve", json={}).status_code in (401, 403)
        assert c.get(f"{base}/baselines", headers=h).json()["baselines"] == []
        assert c.get("/api/v1/projects/prj_0000000000000000/baselines", headers=h).status_code == 404
        assert c.post(f"{base}/baselines/approve", json={"frames": ["watch"]}, headers=h).status_code == 422
        assert c.post(f"{base}/baselines/approve", json={"routes": ["/"], "x": 1}, headers=h).status_code == 422
        if True:  # build + approve + check over REST
            service.build_web(studio, pid, run_build=make_fake("w1"), docker=DOCKER, image=IMAGE)
            r = c.post(f"{base}/baselines/approve", json={"frames": ["phone"]}, headers=h)
            assert r.status_code == 200, r.text
            sha = r.json()["baselines"][0]["sha256"]
            img = c.get(f"{base}/baselines/images/{sha}", headers=h)
            assert img.status_code == 200 and img.headers["content-type"] == "image/png"
            assert c.get(f"{base}/baselines/images/{'0' * 64}", headers=h).status_code == 404
            s.extra["/@phone"] = [((0, 0, 390, 400), (0, 0, 0))]
            chk = c.post(f"{base}/baselines/check", json={"intendedRoutes": []}, headers=h).json()
            assert chk["status"] == "changed" and chk["lastCheck"]["warnings"]
            assert c.put(f"{base}/baselines/threshold", json={"thresholdPct": 50}, headers=h).status_code == 422
            assert c.put(f"{base}/baselines/threshold", json={"thresholdPct": 2.5}, headers=h).json()["thresholdPct"] == 2.5
            assert c.delete(f"{base}/baselines", headers=h).json()["baselines"] == []
        put = c.put(f"{base}/checklist", json={"flows": [{"id": "login", "title": "Login", "statements": ["ok"]}]},
                    headers=h)
        assert put.status_code == 200 and put.json()["flows"][0]["result"]["status"] == "unknown"
        assert c.get(f"{base}/checklist", headers=h).json()["flows"][0]["id"] == "login"
        assert c.put(f"{base}/checklist", json={"flows": [{"id": "a", "title": "t", "statements": [], "cmd": "x"}]},
                     headers=h).status_code == 422


# ---------------------------------------------------------------- MCP baseline_status (agent: compare only)
def test_mcp_baseline_status_warns_and_attaches_the_diff(built, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    from .test_s9b_sight import load_server
    studio, pid = built
    mod = load_server(monkeypatch, studio)
    s = Screens()
    real_check = baselines.check
    monkeypatch.setattr(baselines, "check", lambda *a, **k: real_check(*a, **k, snap=s, free_bytes=BIG))
    res = mod.baseline_status(projectId=pid)
    assert res.is_error is False and res.structured_content["result"]["status"] == "no_baseline"
    baselines.approve(studio, OWNER, pid, frames=["phone", "desktop"], snap=s, free_bytes=BIG)
    s.extra["/@desktop"] = [((0, 0, 1440, 450), (10, 10, 10))]
    res = mod.baseline_status(projectId=pid, intendedRoutes=["/about"])
    body = res.structured_content["result"]
    assert body["status"] == "changed" and body["lastCheck"]["warnings"]
    assert [c.type for c in res.content] == ["text", "text", "image"]
    assert res.content[1].text.startswith("<<UNTRUSTED DATA: baseline diff>>")
    bad = mod.baseline_status(projectId=pid, intendedRoutes=["//evil"])
    assert bad.is_error is True


# ---------------------------------------------------------------- real browser: approve -> edit -> diff
def _chromium_available() -> bool:
    import os
    root = os.environ.get("PLAYWRIGHT_BROWSERS_PATH") or os.path.join(os.environ.get("LOCALAPPDATA", ""), "ms-playwright")
    return any(Path(root).glob("chromium*"))


@pytest.mark.browser
@pytest.mark.skipif(not _chromium_available(), reason="Playwright Chromium not installed")
def test_real_browser_approve_edit_and_diff(studio) -> None:  # type: ignore[no-untyped-def]
    from aas_build.preview_server import PreviewServer
    from aas_build.runner import BuildResult
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]

    def page(button: str, clock: str) -> str:
        return ("<!doctype html><meta name=viewport content='width=device-width'><title>w6</title>"
                "<body style='margin:0;font:16px sans-serif;background:#fafafa'>"
                f"<header style='padding:16px'>Notițe <span data-aas-volatile style='float:right'>{clock}</span></header>"
                f"<main style='padding:16px'><button style='padding:12px 20px;background:{button};color:#fff;border:0'>"
                "Adaugă</button><p>Lista ta de notițe.</p></main></body>")

    def fake_for(html: str):  # type: ignore[no-untyped-def]
        def fake(*, dest: Path, **kw: Any) -> BuildResult:
            dest.mkdir(parents=True)
            (dest / "index.html").write_text(html, encoding="utf-8")
            return BuildResult(dest=dest, files=["index.html"], total_bytes=len(html), dist_sha256="0" * 64)
        return fake
    service.build_web(studio, pid, run_build=fake_for(page("#1565c0", "12:01:07")), docker=DOCKER, image=IMAGE)
    srv = PreviewServer(service.site_root_for(studio.settings), port=0).start()
    import dataclasses
    studio.settings = dataclasses.replace(studio.settings, preview_port=srv.port)  # the preview origin on a test port
    try:
        st = baselines.approve(studio, OWNER, pid, frames=["phone", "desktop"], free_bytes=BIG)
        assert all(b["masks"] for b in st["baselines"])  # the clock was measured
        same = baselines.check(studio, pid, free_bytes=BIG)
        assert same["status"] == "same", same["lastCheck"]["results"]
        # the clock ticks (masked) and the button turns green (a real edit)
        service.build_web(studio, pid, run_build=fake_for(page("#2e7d32", "12:02:59")), docker=DOCKER, image=IMAGE)
        res = baselines.check(studio, pid, intended_routes=["/"], free_bytes=BIG)
    finally:
        srv.stop()
    rows = res["lastCheck"]["results"]
    assert res["status"] == "changed" and all(r["status"] == "changed" for r in rows), rows
    assert all(0.1 < r["changedPct"] < 5 for r in rows), rows
    assert not res["lastCheck"]["warnings"]
