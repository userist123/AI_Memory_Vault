from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json

HOST = "127.0.0.1"
PORT = int(os.getenv("JARVIS_TTS_PORT", "8002"))
MODEL = os.getenv("JARVIS_TTS_MODEL", "ro_RO-mihai-medium")
DATA_DIR = os.getenv("PIPER_DATA_DIR", os.path.join(os.path.dirname(__file__), "voice_models"))
PIPER_BIN = os.getenv("PIPER_BIN") or shutil.which("piper") or shutil.which("piper.exe")


def synthesize(text: str) -> bytes:
    clean = " ".join(str(text).strip().split())
    if not clean:
        raise ValueError("text is required")
    if len(clean) > 6000:
        clean = clean[:6000]
    if not PIPER_BIN:
        raise RuntimeError("Piper nu este instalat. Ruleaza setup_jarvis_voice.ps1 si reporneste JARVIS.")

    os.makedirs(DATA_DIR, exist_ok=True)
    with tempfile.NamedTemporaryFile(prefix="jarvis_tts_", suffix=".wav", delete=False) as temp:
        output_path = temp.name
    try:
        command = [
            PIPER_BIN,
            "--model", MODEL,
            "--data-dir", DATA_DIR,
            "--output_file", output_path,
            "--length_scale", os.getenv("JARVIS_TTS_LENGTH_SCALE", "0.96"),
            "--noise_scale", os.getenv("JARVIS_TTS_NOISE_SCALE", "0.55"),
            "--noise_w", os.getenv("JARVIS_TTS_NOISE_W", "0.80"),
        ]
        subprocess.run(
            command,
            input=clean,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=120,
            check=True,
        )
        with open(output_path, "rb") as audio:
            return audio.read()
    except subprocess.CalledProcessError as exc:
        detail = (exc.stderr or "Piper synthesis failed").strip()
        raise RuntimeError(detail) from exc
    finally:
        try:
            os.remove(output_path)
        except OSError:
            pass


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        return

    def _headers(self, status=200, content_type="application/json; charset=utf-8"):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET,POST,OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()

    def do_OPTIONS(self):
        self._headers(204)

    def do_GET(self):
        if self.path == "/health":
            self._headers(200)
            payload = {
                "status": "online" if PIPER_BIN else "offline",
                "engine": "Piper neural TTS",
                "model": MODEL,
                "romanian": MODEL.startswith("ro_RO"),
                "piper": bool(PIPER_BIN),
            }
            self.wfile.write(json.dumps(payload, ensure_ascii=False).encode("utf-8"))
            return
        self._headers(404)
        self.wfile.write(b'{"error":"Not found"}')

    def do_POST(self):
        if self.path != "/tts":
            self._headers(404)
            self.wfile.write(b'{"error":"Not found"}')
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            body = json.loads(self.rfile.read(length).decode("utf-8"))
            audio = synthesize(body.get("text", ""))
            self._headers(200, "audio/wav")
            self.send_header("Content-Length", str(len(audio)))
            # headers must be sent before body; Content-Length is intentionally sent again below
        except Exception as exc:
            self._headers(500)
            self.wfile.write(json.dumps({"error": str(exc)}, ensure_ascii=False).encode("utf-8"))
            return
        self.wfile.write(audio)


if __name__ == "__main__":
    print(f"[JARVIS TTS] http://{HOST}:{PORT} | model={MODEL} | piper={PIPER_BIN or 'NOT FOUND'}")
    ThreadingHTTPServer((HOST, PORT), Handler).serve_forever()
