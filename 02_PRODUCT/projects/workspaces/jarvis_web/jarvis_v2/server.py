from __future__ import annotations

import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from .supervisor import JarvisSupervisor

HOST = "127.0.0.1"
PORT = int(os.getenv("JARVIS_V2_PORT", "8003"))
SUPERVISOR = JarvisSupervisor()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        return

    def _json(self, status: int, payload: dict):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET,POST,OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET,POST,OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_GET(self):
        if self.path == "/health":
            self._json(200, {"status": "online", "service": "JARVIS V2 Supervisor", "port": PORT})
            return
        self._json(404, {"error": "Not found"})

    def do_POST(self):
        if self.path != "/v2/chat":
            self._json(404, {"error": "Not found"})
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            data = json.loads(self.rfile.read(length).decode("utf-8")) if length else {}
            message = str(data.get("message", "")).strip()
            history = data.get("history") or []
            model = str(data.get("model", "")).strip()
            if not message:
                self._json(400, {"error": "message is required"})
                return
            session = SUPERVISOR.run(message, history=history, model=model)
            self._json(200, session.public())
        except Exception as exc:
            self._json(500, {"error": str(exc)})


if __name__ == "__main__":
    print(f"[JARVIS V2] Supervisor listening on http://{HOST}:{PORT}")
    ThreadingHTTPServer((HOST, PORT), Handler).serve_forever()
