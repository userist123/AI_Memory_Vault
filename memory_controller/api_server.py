"""
Lightweight Standalone REST API Gateway for Browser AI Agents
(ChatGPT Custom GPTs, Perplexity Spaces, Claude Web, Gemini Web, Browser Extensions).

Uses Python Standard Library http.server (Zero external dependencies).
Exposes REST endpoints for browser AI agents over http://127.0.0.1:8000.
"""

import sys
import os

# Auto-add project root to sys.path so server can run from any working directory
project_root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

import json
import urllib.parse
from http.server import HTTPServer, BaseHTTPRequestHandler
import datetime

from memory_controller.storage.file_engine import FileStorageEngine
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal

class APIJSONEncoder(json.JSONEncoder):
    def default(self, obj):
        if hasattr(obj, "value"):
            return obj.value
        if isinstance(obj, (datetime.date, datetime.datetime)):
            return obj.isoformat()
        return super().default(obj)

class BrowserMemoryAPIHandler(BaseHTTPRequestHandler):
    vault_root = r"C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY"
    storage = FileStorageEngine(vault_root)
    controller = MemoryController(storage)

    def _set_headers(self, status=200):
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization, Mcp-Version")
        self.end_headers()

    def do_OPTIONS(self):
        self._set_headers(200)

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        path = parsed.path
        query_params = urllib.parse.parse_qs(parsed.query)

        if path == "/api/v1/status" or path == "/":
            self._set_headers(200)
            res = {
                "status": "online",
                "service": "AI Memory Vault Browser Gateway",
                "vault_root": self.vault_root,
                "indexed_notes": len(self.storage.id_to_path)
            }
            self.wfile.write(json.dumps(res, ensure_ascii=False, cls=APIJSONEncoder).encode("utf-8"))

        elif path == "/api/v1/search":
            q = query_params.get("q", [""])[0]
            self._set_headers(200)
            notes = self.storage.query(intent=q)
            res = {
                "query": q,
                "total_results": len(notes),
                "results": notes[:10]  # Top 10 notes
            }
            self.wfile.write(json.dumps(res, ensure_ascii=False, cls=APIJSONEncoder).encode("utf-8"))

        elif path.startswith("/api/v1/note/"):
            note_id = path.replace("/api/v1/note/", "")
            note = self.storage.get(note_id)
            if note:
                self._set_headers(200)
                self.wfile.write(json.dumps(note, ensure_ascii=False, cls=APIJSONEncoder).encode("utf-8"))
            else:
                self._set_headers(404)
                self.wfile.write(json.dumps({"error": "Note not found", "id": note_id}).encode("utf-8"))

        else:
            self._set_headers(404)
            self.wfile.write(json.dumps({"error": "Endpoint not found"}).encode("utf-8"))

    def do_POST(self):
        parsed = urllib.parse.urlparse(self.path)
        path = parsed.path

        if path == "/api/v1/propose":
            content_length = int(self.headers.get("Content-Length", 0))
            body = self.rfile.read(content_length)
            try:
                data = json.loads(body.decode("utf-8"))
                result = self.controller.propose(Principal.AI_AGENT, data)
                self._set_headers(201)
                self.wfile.write(json.dumps({"status": "proposed", "result": result}, ensure_ascii=False, cls=APIJSONEncoder).encode("utf-8"))
            except Exception as e:
                self._set_headers(400)
                self.wfile.write(json.dumps({"error": str(e)}, ensure_ascii=False, cls=APIJSONEncoder).encode("utf-8"))
        else:
            self._set_headers(404)
            self.wfile.write(json.dumps({"error": "Endpoint not found"}).encode("utf-8"))

def run_server(port=8000):
    server_address = ("127.0.0.1", port)
    httpd = HTTPServer(server_address, BrowserMemoryAPIHandler)
    print(f"[BROWSER GATEWAY] Running REST API server at http://127.0.0.1:{port}...")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("[BROWSER GATEWAY] Stopping server.")
        httpd.server_close()

if __name__ == "__main__":
    port = 8000
    if len(sys.argv) > 1:
        try:
            port = int(sys.argv[1])
        except ValueError:
            pass
    run_server(port)
