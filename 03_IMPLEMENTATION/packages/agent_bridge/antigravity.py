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
    """Headless AGY session; prompts never enter the process argv.

    The process is reused only inside ONE task. A conversation holds the previous prompts and
    answers, so by default (`isolate_tasks=True`) a prompt for a different task, or one carrying no
    task id, first discards the running process: a task never inherits another task's context.
    `reset()` does the same explicitly.
    """

    def __init__(
        self,
        working_directory: str | Path,
        binary: str = "agy",
        model: str | None = None,
        effort: str | None = None,
        agent: str | None = None,
        isolate_tasks: bool = True,
        process_factory: Callable[..., subprocess.Popen] = subprocess.Popen,
    ):
        self.working_directory = Path(working_directory).resolve()
        self.binary = binary
        self.model = model
        self.effort = effort
        self.agent = agent
        self.isolate_tasks = isolate_tasks
        self._context_task_id: str | None = None
        self._has_context = False
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

    @staticmethod
    def _pump_stdout(process, events: queue.Queue) -> None:
        # The queue is bound per process: a reader thread of a discarded process must never
        # write its end-of-stream marker into the queue of the process that replaced it.
        try:
            for line in process.stdout:
                line = line.strip()
                if not line:
                    continue
                events.put(json.loads(line))
        except BaseException as exc:
            events.put(exc)
        finally:
            events.put(None)

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
        self._reader = threading.Thread(target=self._pump_stdout, args=(self._process, self._events), daemon=True)
        self._reader.start()
        self._stderr_reader = threading.Thread(target=self._pump_stderr, args=(self._process,), daemon=True)
        self._stderr_reader.start()

    def reset(self) -> None:
        """Discard the conversation: the next prompt starts a fresh AGY process."""
        with self._lock:
            self.close(force=True)

    def ask(self, prompt: str, timeout_seconds: float = 3600, task_id: str | None = None) -> dict:
        if not prompt:
            raise ValueError("prompt must not be empty")
        with self._lock:
            if self.isolate_tasks and self._has_context and (task_id is None or task_id != self._context_task_id):
                self.close(force=True)
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
                    self._has_context = True
                    self._context_task_id = task_id
                    return event.get("result", {})

    def close(self, force: bool = False) -> None:
        process = self._process
        self._process = None
        self._has_context = False
        self._context_task_id = None
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
