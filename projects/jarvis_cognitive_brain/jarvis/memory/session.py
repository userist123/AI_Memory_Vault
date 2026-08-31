"""Compact working memory and append-only session recovery logs."""

from __future__ import annotations

import json
import os
import re
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable, Mapping


_SECRET_PATTERNS = (
    re.compile(r"(?i)(api[_-]?key|token|password|secret|authorization)\s*[:=]\s*([\"']?)([^\s,;\"'}]+)", re.MULTILINE),
    re.compile(r"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+"),
    re.compile(r"\bsk-[A-Za-z0-9_-]+\b"),
)


class SessionMemory:
    """Persist only resumable session state, never the complete vault."""

    def __init__(
        self,
        working_memory_path: str | Path,
        recap_dir: str | Path | None = None,
        max_bytes: int = 3072,
    ) -> None:
        self.working_memory_path = Path(working_memory_path)
        self.recap_dir = Path(recap_dir) if recap_dir else self.working_memory_path.parent / "recaps"
        self.max_bytes = max(512, int(max_bytes))
        self.working_memory_path.parent.mkdir(parents=True, exist_ok=True)
        self.recap_dir.mkdir(parents=True, exist_ok=True)

    @staticmethod
    def redact(value: Any) -> str:
        text = str(value or "")
        text = _SECRET_PATTERNS[0].sub(lambda match: f"{match.group(1)}={match.group(2)}[REDACTED]", text)
        for pattern in _SECRET_PATTERNS[1:]:
            text = pattern.sub("[REDACTED]", text)
        return text

    def _atomic_write(self, path: Path, content: str) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        fd, temporary = tempfile.mkstemp(prefix=".jarvis-", dir=path.parent, text=True)
        try:
            with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as handle:
                handle.write(content)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temporary, path)
        except Exception:
            try:
                os.unlink(temporary)
            except OSError:
                pass
            raise

    def read(self) -> str:
        if not self.working_memory_path.exists():
            return ""
        return self.working_memory_path.read_text(encoding="utf-8")[: self.max_bytes]

    def write(self, snapshot: Mapping[str, Any]) -> Path:
        lines = ["# JARVIS MEMORY", f"updated: {datetime.now(timezone.utc).isoformat()}"]
        for key, value in snapshot.items():
            clean_key = re.sub(r"[^a-zA-Z0-9_.-]", "_", str(key))
            if isinstance(value, (dict, list, tuple)):
                rendered = json.dumps(value, ensure_ascii=False, separators=(",", ":"), default=str)
            else:
                rendered = str(value or "")
            lines.append(f"- {clean_key}: {self.redact(rendered).replace(chr(10), ' ')}")
        content = "\n".join(lines).strip() + "\n"
        encoded = content.encode("utf-8")
        if len(encoded) > self.max_bytes:
            content = encoded[: self.max_bytes].decode("utf-8", errors="ignore").rstrip() + "\n"
        self._atomic_write(self.working_memory_path, content)
        return self.working_memory_path

    def checkpoint(self, entries: Iterable[Mapping[str, Any]] = (), **state: Any) -> Path:
        compact_entries = []
        for entry in entries:
            compact_entries.append({
                "id": entry.get("id", ""),
                "title": entry.get("title", ""),
                "content": self.redact(entry.get("content", ""))[:240],
            })
        state["working_memory"] = compact_entries[:5]
        return self.write(state)

    def append_recap(self, event: str, **details: Any) -> Path:
        now = datetime.now(timezone.utc)
        path = self.recap_dir / f"RECAP_{now.date().isoformat()}.md"
        payload = {key: self.redact(value) for key, value in details.items()}
        line = f"- `{now.isoformat()}` **{self.redact(event)}** {json.dumps(payload, ensure_ascii=False, separators=(',', ':'))}\n"
        if path.exists():
            with path.open("a", encoding="utf-8", newline="\n") as handle:
                handle.write(line)
        else:
            self._atomic_write(path, f"# JARVIS RECAP {now.date().isoformat()}\n\n{line}")
        return path

    def record_turn(
        self,
        request: str,
        response: str,
        intent: str = "",
        active_plan_id: str = "",
        working_memory: Iterable[Mapping[str, Any]] = (),
    ) -> Path:
        self.append_recap(
            "dialogue_turn",
            request=self.redact(request),
            response=self.redact(response),
            intent=intent,
            active_plan_id=active_plan_id,
        )
        return self.checkpoint(
            last_request=self.redact(request)[:500],
            last_response=self.redact(response)[:700],
            last_intent=intent,
            active_plan_id=active_plan_id,
            working_memory=working_memory,
        )

    def resume(self, recap_lines: int = 12) -> dict[str, str]:
        recap_files = sorted(self.recap_dir.glob("RECAP_*.md"))
        recap = ""
        if recap_files:
            recap = "\n".join(recap_files[-1].read_text(encoding="utf-8").splitlines()[-recap_lines:])
        return {"working_memory": self.read(), "latest_recap": self.redact(recap)}
