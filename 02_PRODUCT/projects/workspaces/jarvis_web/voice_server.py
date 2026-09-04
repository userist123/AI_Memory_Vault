from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

HOST = "127.0.0.1"
PORT = int(os.getenv("JARVIS_TTS_PORT", "8002"))
MODEL = os.getenv("JARVIS_TTS_MODEL", "ro_RO-mihai-medium")
DATA_DIR = os.getenv("PIPER_DATA_DIR", os.path.join(os.path.dirname(__file__), "voice_models"))
PIPER_BIN = os.getenv("PIPER_BIN") or shutil.which("piper") or shutil.which("piper.exe")


def model_name() -> str:
    return Path(MODEL).stem if MODEL.lower().endswith(".onnx") else MODEL


def model_path() -> Path:
    candidate = Path(MODEL)
    return candidate if candidate.suffix.lower() == ".onnx" else Path(DATA_DIR) / f"{MODEL}.onnx"


def model_config_path() -> Path:
    return Path(f"{model_path()}.json")


def piper_available() -> bool:
    if PIPER_BIN:
        return True
    try:
        import piper
        return True
    except ImportError:
        return False


def ensure_voice_model() -> None:
    os.makedirs(DATA_DIR, exist_ok=True)
    if model_path().exists() and model_config_path().exists():
        return
    if not piper_available():
        raise RuntimeError("Piper nu este instalat.")
    command = [
        sys.executable,
        "-m",
        "piper.download_voices",
        model_name(),
        "--download-dir",
        DATA_DIR,
    ]
    subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=300, check=True)


def speech_text(text: str) -> str:
    value = str(text or "")
    value = re.sub(r"```.*?```", " ", value, flags=re.S)
    value = re.sub(r"`([^`]*)`", r"\1", value)
    value = re.sub(r"https?://\S+", "", value)
    value = re.sub(r"^\s{0,3}#{1,6}\s*", "", value, flags=re.M)
    value = re.sub(r"^\s*[-*+]\s+", "", value, flags=re.M)
    value = re.sub(r"\[([^\]]+)\]\([^\)]+\)", r"\1", value)
    value = re.sub(r"[*_~]+", "", value)
    value = re.sub(r"\s+", " ", value).strip()
    return value[:6000]


def synthesize(text: str) -> bytes:
    clean = speech_text(text)
    if not clean:
        raise ValueError("text is required")
    ensure_voice_model()

    os.makedirs(DATA_DIR, exist_ok=True)
    with tempfile.NamedTemporaryFile(prefix="jarvis_tts_", suffix=".wav", delete=False) as temp:
        output_path = temp.name
    try:
        command = [
            *( [PIPER_BIN] if PIPER_BIN else [sys.executable, "-m", "piper"] ),
            "--model", str(model_path()),
            "--data-dir", DATA_DIR,
            "--output_file", output_path,
            "--length_scale", os.getenv("JARVIS_TTS_LENGTH_SCALE", "0.96"),
            "--noise_scale", os.getenv("JARVIS_TTS_NOISE_SCALE", "0.55"),
            "--noise_w", os.getenv("JARVIS_TTS_NOISE_W", "0.80"),
        ]
        subprocess.run(command, input=clean, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=120, check=True)
        with open(output_path, "rb") as audio:
            return audio.read()
    except subprocess.CalledProcessError as exc:
        raise RuntimeError((exc.stderr or "Piper synthesis failed").strip()) from exc
    finally:
        try:
            os.remove(output_path)
        except OSError:
            pass


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        return

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET,POST,OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_GET(self):
        if self.path != "/health":
            self.send_response(404)
            self.end_headers()
            return
        payload = {
            "status": "online" if piper_available() and model_path().exists() and model_config_path().exists() else "offline",
            "engine": "Piper neural TTS",
            "model": model_name(),
            "romanian": model_name().startswith("ro_RO"),
            "piper": piper_available(),
            "model_ready": model_path().exists() and model_config_path().exists(),
        }
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        if self.path != "/tts":
            self.send_response(404)
            self.end_headers()
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            body = json.loads(self.rfile.read(length).decode("utf-8"))
            audio = synthesize(body.get("text", ""))
        except Exception as exc:
            body = json.dumps({"error": str(exc)}, ensure_ascii=False).encode("utf-8")
            self.send_response(500)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()
            self.wfile.write(body)
            return
        self.send_response(200)
        self.send_header("Content-Type", "audio/wav")
        self.send_header("Content-Length", str(len(audio)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(audio)


if __name__ == "__main__":
    try:
        ensure_voice_model()
    except Exception as exc:
        print(f"[JARVIS TTS] voice model unavailable: {exc}")
    print(f"[JARVIS TTS] http://{HOST}:{PORT} | model={MODEL} | piper={PIPER_BIN or 'NOT FOUND'}")
    ThreadingHTTPServer((HOST, PORT), Handler).serve_forever()
