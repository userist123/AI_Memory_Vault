"""Cross-encoder reranker wired into MemoryController.search(), OFF by default.

The reorder is applied at the single point production ranks (the fused_score
map of the graph-OFF fused_score branch), which is exactly where the
preregistered experiment injected it (07_EVALUATION/reranker_experiment/).
Any reranker failure keeps the production order.
"""
from __future__ import annotations

import pytest

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle, Principal
from retrieval.reranker import HttpReranker, RerankerError, rerank_text, validate_scores


@pytest.fixture(autouse=True)
def _hmac_secret(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_reranker_wiring_32bytes!")


def _note(note_id, content, lifecycle=Lifecycle.ACTIVE.value):
    return {
        "id": note_id, "type": "knowledge", "lifecycle": lifecycle,
        "category": "c", "tags": ["t"], "created": "2026-09-01", "updated": "2026-09-01",
        "provenance": {"source_type": "user", "source_ref": "unit"},
        "confidence": "high", "verification": "verified", "relations": [],
        "content": content,
    }


def _storage():
    storage = StorageEngine()
    # Lexically strongest notes first; "z-hidden-gem" matches the query weakly,
    # so production ranks it low. A reranker that prefers it must lift it.
    for i in range(12):
        storage.set(f"lex-{i:02d}", _note(f"lex-{i:02d}", "zebra quokka zebra quokka " + "filler " * i))
    storage.set("z-hidden-gem", _note("z-hidden-gem", "GEM answer about zebra"))
    storage.set("raw-secret", _note("raw-secret", "zebra quokka zebra GEM", lifecycle=Lifecycle.RAW.value))
    return storage


class PreferGem:
    """Fake reranker: scores documents containing GEM highest."""
    model = "fake-prefer-gem"
    k = 50

    def __init__(self):
        self.calls = []

    def score(self, query, documents):
        self.calls.append((query, list(documents)))
        return [10.0 if "GEM" in d else -float(i) for i, d in enumerate(documents)]


class Broken:
    model = "fake-broken"
    k = 50

    def score(self, query, documents):
        raise RerankerError("down")


class WrongLength:
    model = "fake-wrong-length"
    k = 50

    def score(self, query, documents):
        return [1.0]


def _ids(pack):
    return [r["id"] for r in pack["results"]]


def _search(controller, query="zebra quokka"):
    return controller.search(Principal.AI_AGENT, query, page_size=5)


def test_off_by_default_and_trace_says_so():
    controller = MemoryController(_storage())
    assert controller.reranker is None
    pack = _search(controller)
    assert pack["candidate_trace"]["reranker"]["status"] == "disabled"
    assert "z-hidden-gem" not in _ids(pack)


def test_reranker_lifts_a_low_fused_candidate_into_the_served_top():
    fake = PreferGem()
    pack = _search(MemoryController(_storage(), reranker=fake))
    assert _ids(pack)[0] == "z-hidden-gem"
    trace = pack["candidate_trace"]["reranker"]
    assert trace["status"] == "ok" and trace["model"] == "fake-prefer-gem"
    assert trace["k"] == 50 and trace["candidates"] >= 13
    assert fake.calls and fake.calls[0][0] == "zebra quokka"


def test_reranker_never_admits_a_gated_note():
    fake = PreferGem()
    pack = _search(MemoryController(_storage(), reranker=fake))
    assert "raw-secret" not in _ids(pack)
    sent = [d for _, docs in fake.calls for d in docs]
    assert not any("zebra quokka zebra GEM" in d for d in sent), "RAW note text was sent to the reranker"


@pytest.mark.parametrize("bad", [Broken(), WrongLength()])
def test_failure_keeps_the_production_order(bad):
    baseline = _ids(_search(MemoryController(_storage())))
    pack = _search(MemoryController(_storage(), reranker=bad))
    assert _ids(pack) == baseline
    assert pack["candidate_trace"]["reranker"]["status"] == "error"


def test_repeated_query_is_reranked_every_time_not_served_from_a_stale_cache():
    fake = PreferGem()
    controller = MemoryController(_storage(), reranker=fake)
    first, second = _ids(_search(controller)), _ids(_search(controller))
    assert first == second and first[0] == "z-hidden-gem"
    assert len(fake.calls) == 2


def test_documents_are_the_measured_text():
    note = _note("n1", "# Title line\n" + "x" * 5000)
    text = rerank_text(note)
    assert text.startswith("Title line\n# Title line")
    assert len(text) == len("Title line\n") + 1200


def test_validate_scores_rejects_bad_payloads():
    assert validate_scores([1, 2.5], 2) == [1.0, 2.5]
    for bad in (None, [1.0], [float("nan"), 1.0], ["1", 2.0], [True, 1.0]):
        with pytest.raises(RerankerError):
            validate_scores(bad, 2)


@pytest.mark.parametrize("url", ["http://10.0.0.5:8765", "https://127.0.0.1:8765", "http://example.com:8765",
                                 "http://0.0.0.0:8765"])
def test_http_reranker_refuses_non_loopback(url):
    with pytest.raises(ValueError):
        HttpReranker(url)


def test_http_reranker_unreachable_is_a_reranker_error():
    client = HttpReranker("http://127.0.0.1:9", timeout=0.5)
    with pytest.raises(RerankerError):
        client.score("q", ["d"])


def test_factory_builds_reranker_only_when_env_is_set(monkeypatch, tmp_path):
    from memory_controller import controller_factory
    monkeypatch.setenv("MEMORY_VAULT_ROOT", str(tmp_path))
    monkeypatch.delenv("AI_MEMORY_VAULT_RERANKER_URL", raising=False)
    monkeypatch.setattr(controller_factory, "_CACHED_CONTROLLER", None)
    assert controller_factory.get_memory_controller().reranker is None
    monkeypatch.setattr(controller_factory, "_CACHED_CONTROLLER", None)
    monkeypatch.setenv("AI_MEMORY_VAULT_RERANKER_URL", "http://127.0.0.1:8765")
    built = controller_factory.get_memory_controller()
    assert isinstance(built.reranker, HttpReranker)
    monkeypatch.setattr(controller_factory, "_CACHED_CONTROLLER", None)


def _serve_once(handler_cls):
    import http.server
    import threading
    srv = http.server.HTTPServer(("127.0.0.1", 0), handler_cls)
    threading.Thread(target=srv.handle_request, daemon=True).start()
    return srv


def test_http_reranker_ignores_proxy_settings(monkeypatch):
    """Note text must reach the loopback server itself, never an HTTP proxy."""
    import http.server
    import json as _json
    seen = {"proxy": 0}

    class Proxy(http.server.BaseHTTPRequestHandler):
        def do_POST(self):
            seen["proxy"] += 1
            body = b'{"scores": [9.0], "model": "proxy"}'
            self.send_response(200)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *a):
            return

    class Real(Proxy):
        def do_POST(self):
            length = int(self.headers["Content-Length"])
            assert _json.loads(self.rfile.read(length))["documents"] == ["NOTETEXT"]
            body = b'{"scores": [1.0], "model": "real"}'
            self.send_response(200)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

    proxy, real = _serve_once(Proxy), _serve_once(Real)
    for var in ("HTTP_PROXY", "http_proxy", "ALL_PROXY"):
        monkeypatch.setenv(var, f"http://127.0.0.1:{proxy.server_port}")
    monkeypatch.delenv("NO_PROXY", raising=False)
    monkeypatch.delenv("no_proxy", raising=False)
    client = HttpReranker(f"http://127.0.0.1:{real.server_port}", timeout=3)
    assert client.score("SECRETQUERY", ["NOTETEXT"]) == [1.0]
    assert client.model == "real" and seen["proxy"] == 0
    proxy.server_close()
    real.server_close()


def test_http_reranker_refuses_redirects():
    import http.server

    class Redirect(http.server.BaseHTTPRequestHandler):
        def do_POST(self):
            self.send_response(302)
            self.send_header("Location", "http://example.com/rerank")
            self.send_header("Content-Length", "0")
            self.end_headers()

        def log_message(self, *a):
            return

    srv = _serve_once(Redirect)
    with pytest.raises(RerankerError):
        HttpReranker(f"http://127.0.0.1:{srv.server_port}", timeout=3).score("q", ["d"])
    srv.server_close()
