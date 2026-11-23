"""aas_build.preview_server: per-project origin, headers on every response, no listing, no links (P3d, P3e)."""
from __future__ import annotations

import http.client
import socket
import time
from collections.abc import Iterator
from pathlib import Path

import pytest
from tests.winpaths import system_drive
from aas_build.preview_server import PreviewServer

from .conftest import make_link

PID_A = "a" * 16
PID_B = "b" * 16
REQUIRED = {
    "content-security-policy", "permissions-policy", "cross-origin-opener-policy", "cross-origin-resource-policy",
    "x-frame-options", "x-content-type-options", "referrer-policy", "cache-control",
}


@pytest.fixture()
def sites(tmp_path: Path) -> dict[str, Path]:
    out = {}
    for pid, tag in ((PID_A, "A"), (PID_B, "B")):
        d = tmp_path / "sites" / pid
        (d / "assets").mkdir(parents=True)
        (d / "index.html").write_text(f"<html>{tag}</html>", encoding="utf-8")
        (d / "assets" / "app.js").write_text("1", encoding="utf-8")
        (d / "assets" / "s.svg").write_text("<svg/>", encoding="utf-8")
        (d / ".hidden").write_text("h", encoding="utf-8")
        out[pid] = d
    return out


@pytest.fixture()
def server(sites: dict[str, Path]) -> Iterator[PreviewServer]:
    s = PreviewServer(lambda pid: sites.get(pid), port=0).start()
    yield s
    s.stop()


def req(s: PreviewServer, path: str, host: str | None = None, method: str = "GET", pid: str = PID_A):  # type: ignore[no-untyped-def]
    c = http.client.HTTPConnection("127.0.0.1", s.port, timeout=5)
    c.putrequest(method, path, skip_host=True, skip_accept_encoding=True)
    c.putheader("Host", host if host is not None else f"p-{pid}.localhost:{s.port}")
    c.endheaders()
    r = c.getresponse()
    body = r.read()
    h = {k.lower(): v for k, v in r.getheaders()}
    c.close()
    return r.status, h, body


def test_serves_index_and_assets(server: PreviewServer) -> None:
    st, h, body = req(server, "/")
    assert st == 200 and body == b"<html>A</html>" and h["content-type"].startswith("text/html")
    st, h, _ = req(server, "/assets/app.js")
    assert st == 200 and h["content-type"].startswith(("text/javascript", "application/javascript"))


def test_p3d_project_comes_from_host_not_path(server: PreviewServer) -> None:
    assert req(server, "/", pid=PID_B)[2] == b"<html>B</html>"
    st, _, _ = req(server, f"/p/{PID_B}/index.html")  # no path-based project selection
    assert st == 404


@pytest.mark.parametrize("host", [
    "127.0.0.1:{p}", "localhost:{p}", "p-{a}.localhost", "p-{a}.localhost:1", "evil.com:{p}", "p-{a}.localhost.evil.com:{p}",
    "P-{a}.localhost:{p}", "p-{a}.localhost:{p}.evil", "p-zz.localhost:{p}", "", "p-{c}.localhost:{p}",
])
def test_host_must_match_exactly(server: PreviewServer, host: str) -> None:
    st, _, _ = req(server, "/", host=host.format(p=server.port, a=PID_A, c="c" * 16))
    assert st in (404, 421)


def test_p3e_headers_on_every_response(server: PreviewServer) -> None:
    cases = [("/", None), ("/assets/app.js", None), ("/assets/s.svg", None), ("/nope.js", None), ("/", "evil.com"),
             ("/assets/", None), ("/../x", None), ("/.hidden", None)]
    for path, host in cases:
        st, h, _ = req(server, path, host=host)
        assert REQUIRED <= h.keys(), (path, st, REQUIRED - h.keys())
    st, h, _ = req(server, "/", method="POST")
    assert st == 405 and REQUIRED <= h.keys()
    st, h, _ = req(server, "/", method="HEAD")
    assert st == 200 and REQUIRED <= h.keys()


def test_exact_header_values(server: PreviewServer) -> None:
    _, h, _ = req(server, "/")
    csp = h["content-security-policy"]
    for part in ("default-src 'none'", "script-src 'self'", "connect-src 'self'", "form-action 'none'", "frame-src 'none'",
                 "object-src 'none'", "base-uri 'none'", "img-src 'self' data: blob:",
                 "frame-ancestors http://tauri.localhost tauri://localhost"):
        assert part in csp, part
    assert "unsafe-eval" not in csp and "*" not in csp
    assert h["cross-origin-opener-policy"] == "same-origin"
    assert h["cross-origin-resource-policy"] == "same-origin"
    assert h["x-frame-options"] in ("SAMEORIGIN", "DENY")
    for p in ("camera=()", "microphone=()", "geolocation=()", "usb=()", "payment=()"):
        assert p in h["permissions-policy"]
    assert h["x-content-type-options"] == "nosniff" and h["cache-control"] == "no-store"
    assert "access-control-allow-origin" not in h


def test_no_directory_listing(server: PreviewServer) -> None:
    st, _, body = req(server, "/assets/")
    assert st == 404 and b"app.js" not in body
    assert req(server, "/assets")[0] == 404


def test_dotfiles_and_traversal_refused(server: PreviewServer, sites: dict[str, Path]) -> None:
    (sites[PID_A].parent / "secret.txt").write_text("S", encoding="utf-8")
    for p in ("/.hidden", "/assets/../.hidden", "/../secret.txt", "/%2e%2e/secret.txt", "/..%5csecret.txt", "/assets\\..\\..\\secret.txt",
              "/%00", "/assets/app.js%00.png", f"/{system_drive()}/Windows/win.ini", "//etc/passwd", "/assets/app.js::$DATA"):
        st, _, body = req(server, p)
        assert st in (400, 403, 404) and body != b"S", p


def test_symlink_not_followed(server: PreviewServer, sites: dict[str, Path], canary: Path) -> None:
    d = sites[PID_A]
    made = False
    if make_link(d / "leak.txt", canary, "symlink"):
        made = True
        st, _, body = req(server, "/leak.txt")
        assert st in (403, 404) and b"SECRET" not in body
    if make_link(d / "jct", canary.parent, "junction"):
        made = True
        st, _, body = req(server, "/jct/host_canary.txt")
        assert st in (403, 404) and b"SECRET" not in body
    if make_link(d / "hl.txt", canary, "hardlink"):
        made = True
        st, _, body = req(server, "/hl.txt")
        assert st in (403, 404) and b"SECRET" not in body
    if not made:
        pytest.skip("cannot create any link on this host")


def test_unknown_project_404_and_methods(server: PreviewServer) -> None:
    assert req(server, "/", pid="c" * 16)[0] == 404
    for m in ("PUT", "DELETE", "PATCH", "OPTIONS"):
        assert req(server, "/", method=m)[0] in (405, 501)


def test_default_port_and_bind() -> None:
    from aas_build import preview_server as ps

    assert ps.DEFAULT_PORT == 17880 and ps.BIND_HOST == "127.0.0.1"
    with pytest.raises(ValueError):
        PreviewServer(lambda p: None, host="0.0.0.0", port=0)  # noqa: S104 - must be refused


# ---- raw-socket probes (malformed input, slow clients, connection flood) ---------------------------------------------

def raw(s: PreviewServer, data: bytes, timeout: float = 5.0) -> tuple[int, dict[str, str], bytes]:
    with socket.create_connection(("127.0.0.1", s.port), timeout=timeout) as c:
        c.sendall(data)
        buf = b""
        while True:
            try:
                chunk = c.recv(65536)
            except (ConnectionResetError, ConnectionAbortedError, TimeoutError):
                break
            if not chunk:
                break
            buf += chunk
    head, _, body = buf.partition(b"\r\n\r\n")
    lines = head.decode("iso-8859-1").split("\r\n")
    status = int(lines[0].split(" ")[1]) if lines[0].startswith("HTTP/") else 0
    hdrs = {k.strip().lower(): v.strip() for k, _, v in (ln.partition(":") for ln in lines[1:]) if k}
    return status, hdrs, body


def _host(s: PreviewServer) -> bytes:
    return f"p-{PID_A}.localhost:{s.port}".encode()


@pytest.mark.parametrize("path", [b"/%ff", b"/%c0%af", b"/assets/%c0%afapp.js", b"/%e2%82", b"/\xc3\xa9.html", b"/\xff"])
def test_invalid_percent_utf8_and_non_ascii_paths_are_400_with_headers(server: PreviewServer, path: bytes) -> None:
    st, h, _ = raw(server, b"GET " + path + b" HTTP/1.1\r\nHost: " + _host(server) + b"\r\n\r\n")
    assert st == 400, (path, st)
    assert REQUIRED <= set(h), REQUIRED - set(h)


def test_duplicate_host_header_is_400(server: PreviewServer) -> None:
    other = f"p-{PID_B}.localhost:{server.port}".encode()
    for second in (_host(server), other):
        st, h, _ = raw(server, b"GET / HTTP/1.1\r\nHost: " + _host(server) + b"\r\nHost: " + second + b"\r\n\r\n")
        assert st == 400 and REQUIRED <= set(h)
    st, _, body = raw(server, b"GET / HTTP/1.1\r\nHost: " + _host(server) + b"\r\n\r\n")  # [+] single Host still works
    assert st == 200 and body == b"<html>A</html>"


@pytest.mark.parametrize("line", [b"GARBAGE", b"GET / HTTP/1.1 extra", b"GET / HTTP/x.y", b"POST /", b"GET /", b"GET / XTTP/1.1"])
def test_malformed_request_line_is_400_with_headers(server: PreviewServer, line: bytes) -> None:
    st, h, _ = raw(server, line + b"\r\nHost: " + _host(server) + b"\r\n\r\n")
    assert st == 400, (line, st)
    assert REQUIRED <= set(h), REQUIRED - set(h)


def test_idle_connection_is_dropped(sites: dict[str, Path]) -> None:
    from aas_build import preview_server as ps

    assert ps.IDLE_TIMEOUT_S == 10.0
    s = PreviewServer(lambda pid: sites.get(pid), port=0, idle_timeout=0.5).start()
    try:
        with socket.create_connection(("127.0.0.1", s.port), timeout=5) as c:
            c.sendall(b"GET / HTTP/1.1\r\n")  # slowloris: never finish the headers
            t0 = time.monotonic()
            try:
                data = c.recv(1024)
            except (ConnectionResetError, ConnectionAbortedError):
                data = b""
            elapsed = time.monotonic() - t0
        assert data == b"" and elapsed < 4, (data, elapsed)  # the server closed the socket, well before the client's 5 s
    finally:
        s.stop()


def test_connection_cap_refuses_extra_and_frees_slots(sites: dict[str, Path]) -> None:
    from aas_build import preview_server as ps

    assert ps.MAX_CONNECTIONS == 32
    s = PreviewServer(lambda pid: sites.get(pid), port=0, idle_timeout=5, max_connections=2).start()
    held: list[socket.socket] = []
    try:
        for _ in range(2):
            c = socket.create_connection(("127.0.0.1", s.port), timeout=5)
            c.sendall(b"GET / HTTP/1.1\r\n")  # occupy a slot without completing the request
            held.append(c)
        time.sleep(0.5)
        st, _, _ = raw(s, b"GET / HTTP/1.1\r\nHost: " + _host(s) + b"\r\n\r\n", timeout=3)
        assert st == 0  # third connection closed without any response
        for c in held:
            c.close()
        held.clear()
        deadline = time.monotonic() + 5
        while True:  # [+] slots come back once the held connections end
            st, _, body = raw(s, b"GET / HTTP/1.1\r\nHost: " + _host(s) + b"\r\n\r\n", timeout=3)
            if st == 200 or time.monotonic() > deadline:
                break
            time.sleep(0.1)
        assert st == 200 and body == b"<html>A</html>"
    finally:
        for c in held:
            c.close()
        s.stop()


def test_s6_dev_frame_ancestors_are_opt_in_and_strict(sites: dict[str, Path]) -> None:
    """The vite dev/preview UI (http://127.0.0.1:<port>) may frame the preview only when the server was started with
    that exact origin; the default header stays Tauri-only and bad origins are refused."""
    default = PreviewServer(lambda pid: sites.get(pid), port=0).start()
    dev = PreviewServer(lambda pid: sites.get(pid), port=0, extra_frame_ancestors=["http://127.0.0.1:1421"]).start()
    try:
        assert "http://127.0.0.1:1421" not in req(default, "/")[1]["content-security-policy"]
        csp = req(dev, "/")[1]["content-security-policy"]
        assert "frame-ancestors http://tauri.localhost tauri://localhost http://127.0.0.1:1421" in csp
        assert req(dev, "/nope")[1]["content-security-policy"] == csp  # also on error responses
    finally:
        default.stop()
        dev.stop()
    for bad in ("*", "http://evil.example:1421", "https://127.0.0.1:1421", "http://127.0.0.1", "http://127.0.0.1:1421 evil.com",
                "http://127.0.0.1:1421;"):
        with pytest.raises(ValueError):
            PreviewServer(lambda pid: None, port=0, extra_frame_ancestors=[bad])
