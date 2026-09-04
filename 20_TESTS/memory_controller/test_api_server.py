import pytest
import json
import urllib.request
import urllib.error
import threading
import time
from memory_controller.api_server import run_server, HTTPServer, BrowserMemoryAPIHandler

@pytest.fixture(scope="module")
def api_server():
    server_address = ("127.0.0.1", 8999)
    httpd = HTTPServer(server_address, BrowserMemoryAPIHandler)
    thread = threading.Thread(target=httpd.serve_forever)
    thread.daemon = True
    thread.start()
    time.sleep(0.2)
    yield "http://127.0.0.1:8999"
    httpd.shutdown()

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
    req = urllib.request.Request(url)
    with urllib.request.urlopen(req) as resp:
        assert resp.status == 200
        data = json.loads(resp.read().decode("utf-8"))
        assert "query" in data
        assert "results" in data
