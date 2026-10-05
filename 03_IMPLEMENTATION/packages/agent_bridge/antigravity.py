from __future__ import annotations

import json
import queue
import shutil
import subprocess
import threading
import time
from pathlib import Path
from typing import Callable


class AntigravityExecutionError(RuntimeError):
    pass


class AntigravitySession:
    """Persistent AGY headless session; prompts never enter the process argv."""

    def __init__(
        self,
        working_directory: str | Path,
        binary: str = "agy",
        model: str | None = None,
        effort: str | None = None,
        agent: str | None = None,
        process_factory: Callable[..., subprocess.Popen] = subprocess.Popen,
    ):
        self.working_directory = Path(working_directory).resolve()
        self.binary = binary
        self.model = model
        self.effort = effort
        self.agent = agent
        self.process_factory = process_factory
        self._process = None
        self._events: queue.Queue[dict | BaseException | None] = queue.Queue()
        self._reader: threading.Thread | None = None
        self._stderr_reader: threading.Thread | None = None
        self._lock = threading.Lock()

    def build_command(self) -> list[str]:
        command = [
            self.binary,
            "--input-format",
            "stream-json",
            "--output-format",
            "stream-json",
        ]
        if self.model:
            command.extend(["--model", self.model])
        if self.effort:
            command.extend(["--effort", self.effort])
        if self.agent:
            command.extend(["--agent", self.agent])
        return command

    def _pump_stdout(self, process) -> None:
        try:
            for line in process.stdout:
                line = line.strip()
                if not line:
                    continue
                self._events.put(json.loads(line))
        except BaseException as exc:
            self._events.put(exc)
        finally:
            self._events.put(None)

    def _pump_stderr(self, process) -> None:
        try:
            for _ in process.stderr:
                pass
        except Exception:
            pass

    def start(self) -> None:
        if self._process is not None and self._process.poll() is None:
            return
        if not self.working_directory.exists():
            raise AntigravityExecutionError(f"working directory unavailable: {self.working_directory}")
        if shutil.which(self.binary) is None:
            raise AntigravityExecutionError(f"{self.binary} unavailable")

        self._events = queue.Queue()
        self._process = self.process_factory(
            self.build_command(),
            cwd=str(self.working_directory),
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            bufsize=1,
        )
        self._reader = threading.Thread(target=self._pump_stdout, args=(self._process,), daemon=True)
        self._reader.start()
        self._stderr_reader = threading.Thread(target=self._pump_stderr, args=(self._process,), daemon=True)
        self._stderr_reader.start()

    def ask(self, prompt: str, timeout_seconds: float = 3600) -> dict:
        if not prompt:
            raise ValueError("prompt must not be empty")
        with self._lock:
            self.start()
            assert self._process is not None and self._process.stdin is not None
            message = {"event": "user", "message": {"content": prompt}}
            self._process.stdin.write(json.dumps(message, ensure_ascii=False) + "\n")
            self._process.stdin.flush()

            deadline = time.monotonic() + timeout_seconds
            while True:
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    self.close(force=True)
                    raise AntigravityExecutionError("antigravity session timeout")
                try:
                    event = self._events.get(timeout=remaining)
                except queue.Empty:
                    self.close(force=True)
                    raise AntigravityExecutionError("antigravity session timeout")
                if event is None:
                    code = self._process.poll()
                    self.close(force=True)
                    raise AntigravityExecutionError(f"antigravity session exited: {code}")
                if isinstance(event, BaseException):
                    self.close(force=True)
                    raise AntigravityExecutionError(str(event))
                if event.get("event") == "result":
                    return event.get("result", {})

    def close(self, force: bool = False) -> None:
        process = self._process
        self._process = None
        if process is None:
            return
        try:
            if not force and process.stdin:
                process.stdin.close()
            elif process.stdin:
                process.stdin.close()
        except OSError:
            pass
        if force and process.poll() is None:
            process.kill()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)

    def __enter__(self) -> "AntigravitySession":
        self.start()
        return self

    def __exit__(self, *_args) -> None:
        self.close()
