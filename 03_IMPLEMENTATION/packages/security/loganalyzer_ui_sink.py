"""Append-only JSONL sink for the LogAnalyzer.UI forensic consumer.

Only SecurityEvent.to_dict() output is written. The sink never writes raw
prompts, credentials, cookies, tool parameters or arbitrary tool output.
"""
from __future__ import annotations

import json
from pathlib import Path
from threading import RLock
from typing import Any

from .security_event import SecurityEvent


class LogAnalyzerUISink:
    """Write metadata-only security events as newline-delimited JSON."""

    def __init__(self, path: str | Path) -> None:
        self._path = Path(path)
        self._lock = RLock()

    @property
    def path(self) -> Path:
        return self._path

    def emit(self, event: SecurityEvent) -> None:
        payload = event.to_dict()
        # Serialize only the machine-readable SecurityEvent contract.
        line = json.dumps(payload, ensure_ascii=True, sort_keys=True, separators=(",", ":"))
        with self._lock:
            self._path.parent.mkdir(parents=True, exist_ok=True)
            with self._path.open("a", encoding="utf-8", newline="\n") as handle:
                handle.write(line)
                handle.write("\n")
                handle.flush()

    def __call__(self, event: SecurityEvent) -> None:
        self.emit(event)
