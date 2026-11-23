"""S9a B3: the preview server runs inside the backend (lifespan), main() turns it on, the backend logs to a capped file
under the data root, and the preview port is bound exclusively."""
from __future__ import annotations

import http.client
import socket
import sys
from pathlib import Path
from typing import Any

import pytest
from aas_build import service as build_service
from aas_build.preview_server import PreviewServer
from apps.backend import main as backend
from starlette.testclient import TestClient
from studio_core import project_kind
from studio_core.config import Settings

TOKEN = "t" * 40
AUTH = {"Authorization": f"Bearer {TOKEN}"}


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return int(s.getsockname()[1])


def _listening(port: int) -> bool:
    with socket.socket() as s:
        s.settimeout(0.5)
        return s.connect_ex(("127.0.0.1", port)) == 0


def _get(port: int, host: str) -> tuple[int, bytes, dict[str, str]]:
    c = http.client.HTTPConnection("127.0.0.1", port, timeout=5)
    c.request("GET", "/", headers={"Host": host})
    r = c.getresponse()
    body = r.read()
    hdrs = {k.lower(): v for k, v in r.getheaders()}
    c.close()
    return r.status, body, hdrs


def test_preview_server_lives_inside_the_backend_lifespan(tmp_path: Path) -> None:
    port = _free_port()
    settings = Settings(data_dir=tmp_path / "data", preview_port=port)
    app = backend.create_app(settings, TOKEN, preview=True, dev_origins=["http://127.0.0.1:1421"])
    assert not _listening(port)
    with TestClient(app, base_url="http://127.0.0.1:17875") as c:
        pid = c.post("/api/v1/projects", json={"name": "p"}, headers=AUTH).json()["id"]
        base = build_service.preview_base(settings, pid)
        (base / "site-0000000000001").mkdir(parents=True)
        (base / "site-0000000000001" / "index.html").write_text("<!doctype html><title>s9</title>", encoding="utf-8")
        (base / "current").write_text("site-0000000000001", encoding="utf-8")
        assert _listening(port)
        st, body, hdrs = _get(port, f"p-{pid[4:]}.localhost:{port}")
        assert st == 200 and b"<title>s9</title>" in body
        csp = hdrs["content-security-policy"]
        assert "frame-ancestors http://tauri.localhost tauri://localhost http://127.0.0.1:1421" in csp
        assert app.state.preview_server is not None and app.state.preview_server.port == port
    assert not _listening(port)
    project_kind.unregister("app")


def test_production_preview_has_no_dev_frame_ancestor(tmp_path: Path) -> None:
    port = _free_port()
    app = backend.create_app(Settings(data_dir=tmp_path / "data", preview_port=port), TOKEN, preview=True)
    with TestClient(app, base_url="http://127.0.0.1:17875"):
        _st, _b, hdrs = _get(port, f"p-{'0' * 16}.localhost:{port}")
        assert "127.0.0.1:1421" not in hdrs.get("content-security-policy", "") or _st == 404
    project_kind.unregister("app")


def test_main_starts_the_backend_with_the_preview_server(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    seen: dict[str, Any] = {}
    real = backend.create_app

    def spy(*a: Any, **kw: Any) -> Any:
        seen.update(kw)
        return real(*a, **kw)
    monkeypatch.setattr(backend, "create_app", spy)
    monkeypatch.setattr(backend, "serve", lambda app, port: None)
    monkeypatch.setenv("AAS_DATA_DIR", str(tmp_path / "data"))
    monkeypatch.delenv("AAS_DEV_ORIGINS", raising=False)
    backend.main(["--dev-token-file"])
    assert seen.get("preview") is True
    project_kind.unregister("app")


def test_capped_log_rotates_and_stays_bounded(tmp_path: Path) -> None:
    log = backend.CappedLog(tmp_path / "logs" / "backend.log", cap=1000)
    for _ in range(50):
        log.write("x" * 99 + "\n")
    log.flush()
    files = sorted((tmp_path / "logs").iterdir())
    assert [f.name for f in files] == ["backend.log", "backend.log.1"]
    assert all(f.stat().st_size <= 1100 for f in files)
    log.close()


def test_main_logs_to_a_capped_file_under_the_data_root(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(backend, "serve", lambda app, port: print("serving", file=sys.stderr))
    monkeypatch.setenv("AAS_DATA_DIR", str(tmp_path / "data"))
    old = sys.stdout, sys.stderr
    try:
        backend.main(["--dev-token-file", "--log-to-data-root"])
        sys.stderr.flush()
    finally:
        cur = sys.stderr
        sys.stdout, sys.stderr = old
        if isinstance(cur, backend.CappedLog):
            cur.close()
    text = (tmp_path / "data" / "logs" / "backend.log").read_text(encoding="utf-8")
    assert "serving" in text and "dev token file mode" in text
    assert "t" * 40 not in text
    project_kind.unregister("app")


def test_preview_port_is_bound_exclusively() -> None:
    a = PreviewServer(lambda _i: None, port=0).start()
    try:
        with pytest.raises(OSError):
            PreviewServer(lambda _i: None, port=a.port)
    finally:
        a.stop()


LIB_RS = Path(__file__).resolve().parents[2] / "apps" / "desktop" / "src-tauri" / "src" / "lib.rs"


def test_shell_resolves_the_product_root_at_runtime_and_logs_to_the_data_root() -> None:
    src = LIB_RS.read_text(encoding="utf-8")
    assert "CARGO_MANIFEST_DIR" not in src  # no compile-time product root (B3)
    assert "current_exe" in src and "runtime.zip" in src  # repo mode from the exe location, installed mode bundle
    assert "--log-to-data-root" in src and "backend-boot.log" in src
    assert "Stdio::null()).stderr(Stdio::null())" not in src


def test_webview_args_keep_the_wry_defaults_and_the_egress_args() -> None:
    """B9: a custom additionalBrowserArgs REPLACES wry's defaults (wry 0.57.3 src/webview2/mod.rs), so they are re-added."""
    import json
    conf = json.loads((LIB_RS.parents[1] / "tauri.conf.json").read_text(encoding="utf-8"))
    args = conf["app"]["windows"][0]["additionalBrowserArgs"].split()
    assert "--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection" in args
    assert "--proxy-server=http://127.0.0.1:9" in args
    assert "--force-webrtc-ip-handling-policy=disable_non_proxied_udp" in args
    assert sum(a.startswith("--disable-features=") for a in args) == 1  # a second one would override the first
