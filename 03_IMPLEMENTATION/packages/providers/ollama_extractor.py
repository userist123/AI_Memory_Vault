"""Optional local-LLM (Ollama) adapter for AtomicMemoryExtractor.

Fully opt-in and disabled unless explicitly constructed and used. Never calls
any cloud API — only a local Ollama endpoint the caller configures. Falls back
to returning no extra candidates on any connection or parsing error, so the
deterministic path in extraction.py always still works.
"""
from __future__ import annotations

import json
import urllib.error
import urllib.request
from typing import Iterable, List

_EXTRACTION_PROMPT = (
    "Extrage din textul urmator memorii atomice in format JSON list, fiecare element cu "
    "campurile type (fact|decision|preference|task|lesson|procedure), category si content. "
    "Raspunde STRICT cu JSON valid, fara text suplimentar.\n\nText:\n{text}"
)


class OllamaExtractionAdapter:
    """Callable adapter matching the AtomicMemoryExtractor(local_llm=...) contract.

    Usage:
        from cognitive_core.ollama_extractor import OllamaExtractionAdapter
        from cognitive_core.extraction import AtomicMemoryExtractor

        adapter = OllamaExtractionAdapter(model="llama3.1", host="http://localhost:11434")
        extractor = AtomicMemoryExtractor(local_llm=adapter)
    """

    def __init__(self, model: str = "llama3.1", host: str = "http://localhost:11434",
                 timeout_seconds: int = 15):
        self.model = model
        self.host = host.rstrip("/")
        self.timeout_seconds = timeout_seconds

    def __call__(self, text: str) -> Iterable[dict]:
        payload = {"model": self.model, "prompt": _EXTRACTION_PROMPT.format(text=text), "stream": False}
        request = urllib.request.Request(
            url=f"{self.host}/api/generate",
            data=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout_seconds) as response:
                raw = json.loads(response.read().decode("utf-8"))
        except (urllib.error.URLError, OSError, ValueError):
            return []
        return self._parse(raw.get("response", ""))

    @staticmethod
    def _parse(generated: str) -> List[dict]:
        try:
            start = generated.index("[")
            end = generated.rindex("]") + 1
            parsed = json.loads(generated[start:end])
        except (ValueError, json.JSONDecodeError):
            return []
        if not isinstance(parsed, list):
            return []
        return [item for item in parsed if isinstance(item, dict) and "content" in item]
