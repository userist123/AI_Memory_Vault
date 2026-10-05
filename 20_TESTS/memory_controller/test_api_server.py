import os
import pytest
import json
import urllib.request
import urllib.error
import threading
import time
from memory_controller.api_server import run_server, HTTPServer, BrowserMemoryAPIHandler

@pytest.fixture(scope="module")
def api_server(monkeypatch_module=None):
    os.environ["AI_MEMORY_VAULT_API_TOKEN"] = "test-token-vault"
    server_address = ("127.0.0.1", 8999)
    httpd = HTTPServer(server_address, BrowserMemoryAPIHandler)
    thread = threading.Thread(target=httpd.serve_forever)
    thread.daemon = True
    thread.start()
    time.sleep(0.2)
    yield "http://127.0.0.1:8999"
    httpd.shutdown()
    os.environ.pop("AI_MEMORY_VAULT_API_TOKEN", None)

def test_api_status_endpoint(api_server):
    url = f"{api_server}/api/v1/status"
    req = urllib.request.Request(url)
    with urllib.request.urlopen(req) as resp:
        assert resp.status == 200
        data = json.loads(resp.read().decode("utf-8"))
        assert data["status"] == "online"
        assert "indexed_notes" in data

def test_api_search_endpoint(api_server):
    url = f"{api_server}/api/v1/search?q=system"
    # Unauthenticated request must return 401
    unauth_req = urllib.request.Request(url)
    with pytest.raises(urllib.error.HTTPError) as exc_info:
        urllib.request.urlopen(unauth_req)
    assert exc_info.value.code == 401

    # Authenticated request with valid Bearer token must succeed (200)
    auth_req = urllib.request.Request(url, headers={"Authorization": "Bearer test-token-vault"})
    with urllib.request.urlopen(auth_req) as resp:
        assert resp.status == 200
        data = json.loads(resp.read().decode("utf-8"))
        assert "query" in data
        assert "results" in data
