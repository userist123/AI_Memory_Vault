from __future__ import annotations

import json
import os
import subprocess
import sys
import time
from pathlib import Path
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[3]
PORT = "8002"
BASE = f"http://127.0.0.1:{PORT}"


def main() -> int:
    env = dict(os.environ)
    env["JARVIS_TTS_PORT"] = PORT
    proc = subprocess.Popen([sys.executable, "voice_server.py"], cwd=ROOT / "projects" / "jarvis_web", env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    try:
        ready = False
        health = None
        for _ in range(50):
            try:
                with urlopen(f"{BASE}/health", timeout=2) as response:
                    health = json.loads(response.read().decode("utf-8"))
                    ready = response.status == 200
                    break
            except Exception:
                time.sleep(0.1)
        if not ready:
            print("FAIL: voice server did not start")
            return 1
        print(f"PASS: voice health ({health.get('engine')}, {health.get('model')})")
        if not health.get("piper"):
            print("SKIP: Piper executable is not installed in this environment")
            return 0
        payload = json.dumps({"text": "Bună. Sunt JARVIS și vorbesc română în mod natural."}).encode("utf-8")
        request = Request(f"{BASE}/tts", data=payload, headers={"Content-Type": "application/json"}, method="POST")
        with urlopen(request, timeout=30) as response:
            audio = response.read()
            assert response.status == 200
            assert response.headers.get("Content-Type", "").startswith("audio/wav")
        if len(audio) < 1000 or audio[:4] != b"RIFF":
            print("FAIL: invalid WAV output")
            return 1
        print(f"PASS: Romanian WAV generated ({len(audio)} bytes)")
        return 0
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=2)
        except subprocess.TimeoutExpired:
            proc.kill()


if __name__ == "__main__":
    raise SystemExit(main())
