"""Local cross-encoder reranker server for MemoryController (loopback only).

Runs in its own environment (torch + sentence-transformers); the vault never
imports a model. MemoryController reaches it through
``retrieval.reranker.HttpReranker`` when ``AI_MEMORY_VAULT_RERANKER_URL`` is
set (OFF otherwise). Measured configuration: 07_EVALUATION/reranker_experiment/.

    <venv>/python 30_SCRIPTS/retrieval/reranker_server.py --port 8765 \
        --model BAAI/bge-reranker-v2-m3 --revision 953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e --fp16

API: POST /rerank {"query": str, "documents": [str, ...]} -> {"scores": [...], "model": str}
     GET  /health -> {"ok": true, "model": str}

Binds 127.0.0.1 only and answers only requests whose Host header is a loopback
name (a web page cannot drive it); refuses bodies over 4 MB and more than 200
documents.
One request is scored at a time (the GPU is shared with the local LLM).
"""
from __future__ import annotations

import argparse
import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

MAX_BODY = 4 * 1024 * 1024
MAX_DOCS = 200
MAX_LENGTH = 512


def build_handler(model, label: str, lock: threading.Lock):
    class Handler(BaseHTTPRequestHandler):
        def _send(self, code: int, payload: dict) -> None:
            body = json.dumps(payload).encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, fmt, *args):  # no query text in logs
            return

        def do_GET(self):
            if self.path == "/health":
                self._send(200, {"ok": True, "model": label})
            else:
                self._send(404, {"error": "not_found"})

        def _host_ok(self) -> bool:
            host = (self.headers.get("Host") or "").rsplit(":", 1)[0].strip("[]").lower()
            return host in ("127.0.0.1", "localhost", "::1")

        def do_POST(self):
            if not self._host_ok():
                self._send(403, {"error": "host"})
                return
            if self.path != "/rerank":
                self._send(404, {"error": "not_found"})
                return
            try:
                length = int(self.headers.get("Content-Length") or 0)
            except ValueError:
                length = -1
            if length <= 0 or length > MAX_BODY:
                self._send(413, {"error": "body_size"})
                return
            try:
                req = json.loads(self.rfile.read(length).decode("utf-8"))
                query, docs = req["query"], req["documents"]
                if not isinstance(query, str) or not isinstance(docs, list) or not all(isinstance(d, str) for d in docs):
                    raise ValueError("types")
            except Exception:  # noqa: BLE001
                self._send(400, {"error": "bad_request"})
                return
            if len(docs) > MAX_DOCS:
                self._send(413, {"error": "too_many_documents"})
                return
            if not docs:
                self._send(200, {"scores": [], "model": label})
                return
            try:
                with lock:
                    scores = model.predict([(query, d) for d in docs], batch_size=32, show_progress_bar=False)
            except Exception:  # noqa: BLE001 - the client fails closed on any non-200
                self._send(500, {"error": "inference_failed"})
                return
            self._send(200, {"scores": [float(s) for s in scores], "model": label})

    return Handler


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--model", default="BAAI/bge-reranker-v2-m3")
    ap.add_argument("--revision", default="953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e")
    ap.add_argument("--device", default="cuda")
    ap.add_argument("--fp16", action="store_true")
    args = ap.parse_args()

    import torch
    from sentence_transformers import CrossEncoder

    kwargs = {"model_kwargs": {"torch_dtype": torch.float16}} if args.fp16 else {}
    model = CrossEncoder(args.model, revision=args.revision, device=args.device, max_length=MAX_LENGTH, **kwargs)
    model.predict([("warm", "up")])
    label = f"{args.model}@{args.revision[:12]}{'+fp16' if args.fp16 else ''}"
    server = ThreadingHTTPServer(("127.0.0.1", args.port), build_handler(model, label, threading.Lock()))
    print(json.dumps({"listening": f"http://127.0.0.1:{args.port}", "model": label}), flush=True)
    server.serve_forever()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
