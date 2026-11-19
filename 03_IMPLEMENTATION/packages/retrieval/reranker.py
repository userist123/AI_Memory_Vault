"""Cross-encoder reranking of the fused candidate list -- OFF by default.

Measured in ``07_EVALUATION/reranker_experiment/REPORT.md`` (preregistered):
reordering the top 50 fused candidates with a local cross-encoder raised the
served hit@5 on benchmark v3 from 24 to 71 of 130 (47 wins, 0 losses).

Production never imports a model here. The model runs in a separate local
process (``30_SCRIPTS/retrieval/reranker_server.py``, its own environment with
torch), reached over loopback HTTP only -- note text never leaves the machine.
``MemoryController`` receives a reranker object (``reranker=None`` is the
default, i.e. OFF); ``controller_factory`` builds an ``HttpReranker`` only when
``AI_MEMORY_VAULT_RERANKER_URL`` is set.

Contract: ``score(query, documents) -> list[float]`` with one score per
document, or an exception. The controller treats any exception, timeout or
malformed answer as "reranker unavailable" and keeps the production order
(fail closed to the measured baseline, never to an unmeasured order).
"""
from __future__ import annotations

import ipaddress
import json
import math
import urllib.parse
import urllib.request
from typing import Any, Dict, List, Sequence

#: Candidates reordered per query (the preregistered confirmatory K).
DEFAULT_RERANK_K = 50
#: Characters of note content sent with the title (the measured input).
RERANK_TEXT_CHARS = 1200
#: Synthetic score base; real fused RRF scores are < 1, so a reranked note
#: always sorts above every candidate the reranker did not see.
RERANK_SCORE_BASE = 1000.0


class RerankerError(RuntimeError):
    """The reranker could not produce one finite score per document."""


def note_title(note: Dict[str, Any]) -> str:
    title = note.get("title")
    if isinstance(title, str) and title.strip():
        return title.strip()
    content = note.get("content")
    if isinstance(content, str):
        for line in content.splitlines():
            if line.startswith("# "):
                return line[2:].strip()
    return str(note.get("category") or note.get("id") or "untitled")


def rerank_text(note: Dict[str, Any]) -> str:
    """Title + first RERANK_TEXT_CHARS of content: exactly what was measured."""
    content = note.get("content") or ""
    if not isinstance(content, str):
        content = str(content)
    return f"{note_title(note)}\n{content[:RERANK_TEXT_CHARS]}"


def validate_scores(scores: Any, expected: int) -> List[float]:
    if not isinstance(scores, list) or len(scores) != expected:
        raise RerankerError(f"expected {expected} scores, got {type(scores).__name__}"
                            f"{'' if not isinstance(scores, list) else f' of length {len(scores)}'}")
    out = []
    for s in scores:
        if isinstance(s, bool) or not isinstance(s, (int, float)) or not math.isfinite(float(s)):
            raise RerankerError("non-finite or non-numeric score")
        out.append(float(s))
    return out


def _is_loopback(url: str) -> bool:
    host = urllib.parse.urlparse(url).hostname or ""
    if host == "localhost":
        return True
    try:
        return ipaddress.ip_address(host).is_loopback
    except ValueError:
        return False


class HttpReranker:
    """Client for the local reranker server. Loopback URLs only."""

    def __init__(self, url: str, timeout: float = 5.0, k: int = DEFAULT_RERANK_K):
        parsed = urllib.parse.urlparse(url)
        if parsed.scheme != "http" or not _is_loopback(url):
            raise ValueError("reranker URL must be http:// on a loopback host (note text must not leave the machine)")
        self.url = url.rstrip("/")
        self.timeout = float(timeout)
        self.k = int(k)
        self.model = "unknown"

    def score(self, query: str, documents: Sequence[str]) -> List[float]:
        body = json.dumps({"query": query, "documents": list(documents)}).encode("utf-8")
        req = urllib.request.Request(f"{self.url}/rerank", data=body,
                                     headers={"Content-Type": "application/json"}, method="POST")
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                payload = json.loads(resp.read().decode("utf-8"))
        except Exception as exc:  # noqa: BLE001 - any transport failure is "unavailable"
            raise RerankerError(f"reranker unreachable: {type(exc).__name__}") from exc
        if not isinstance(payload, dict):
            raise RerankerError("malformed reranker response")
        self.model = str(payload.get("model") or "unknown")
        return validate_scores(payload.get("scores"), len(documents))
