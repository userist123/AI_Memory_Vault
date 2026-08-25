from __future__ import annotations

import json
import os
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer

from supervisor import JarvisSupervisor


class FakeHandler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        return

    def _send(self, payload):
        body = json.dumps(payload).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path.startswith("/search"):
            self._send({"results": [{"id": "memory-1"}, {"id": "memory-2"}]})
        else:
            self._send({})

    def do_POST(self):
        if self.path == "/route":
            self._send({"selected": [{"name": "Local AI Engineer", "id": "local_ai_engineer"}]})
        elif self.path == "/chat":
            self._send({"reply": "Salut! Cu ce te pot ajuta?", "model": "test-model"})
        else:
            self._send({})


def main() -> int:
    server = HTTPServer(("127.0.0.1", 0), FakeHandler)
    port = server.server_port
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        os.environ["JARVIS_MEMORY_API"] = f"http://127.0.0.1:{port}"
        # Reload module constant after setting environment.
        import supervisor
        supervisor.MEMORY_API = os.environ["JARVIS_MEMORY_API"]
        state = JarvisSupervisor().run("Salut, JARVIS")
        assert state.reply.startswith("Salut")
        assert state.memory_hits == 2
        assert state.selected_agent["id"] == "local_ai_engineer"
        assert [event.name for event in state.events] == [
            "TASK_CREATED",
            "MEMORY_RETRIEVED",
            "AGENT_SELECTED",
            "SYNTHESIS_COMPLETED",
        ]
        print("PASS: JARVIS V2 supervisor lifecycle")
        return 0
    finally:
        server.shutdown()


if __name__ == "__main__":
    raise SystemExit(main())
