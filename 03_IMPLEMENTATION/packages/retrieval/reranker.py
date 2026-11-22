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
import threading
import time
import urllib.error
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


def _error_field(exc: "urllib.error.HTTPError") -> str:
    try:
        body = json.loads((exc.read() or b"")[:4096].decode("utf-8"))
    except Exception:  # noqa: BLE001
        return ""
    return str(body.get("error") or "") if isinstance(body, dict) else ""


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):  # noqa: D401
        raise urllib.error.HTTPError(req.full_url, code, "redirects are refused", headers, fp)


#: No proxy (system, WinINet or *_PROXY env) and no redirects: a request built
#: for a loopback URL can only ever reach that loopback socket. The default
#: urllib opener would honour HTTP_PROXY and send note text to the proxy host.
_LOOPBACK_OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}), _NoRedirect())


class HttpReranker:
    """Client for the local reranker server. Loopback URLs only."""

    #: After a transport failure the client stops calling the server for this many
    #: seconds and fails at once (the controller keeps the production order). On
    #: Windows a refused loopback connection takes ~2 s, so without this every
    #: search would pay 2 s while the reranker server is not running.
    RETRY_AFTER_SECONDS = 60.0
    #: The on-demand launcher (30_SCRIPTS/retrieval/reranker_launcher.py) answers
    #: 503 {"error": "warming"} while the model process starts. That search keeps
    #: the production order; the next one after this pause tries again.
    WARMING_RETRY_SECONDS = 5.0

    def __init__(self, url: str, timeout: float = 5.0, k: int = DEFAULT_RERANK_K, clock=time.monotonic):
        parsed = urllib.parse.urlparse(url)
        if parsed.scheme != "http" or not _is_loopback(url):
            raise ValueError("reranker URL must be http:// on a loopback host (note text must not leave the machine)")
        self.url = url.rstrip("/")
        self.timeout = float(timeout)
        self.k = int(k)
        self.model = "unknown"
        self._clock = clock
        self._down_until = 0.0
        self._lock = threading.Lock()

    def score(self, query: str, documents: Sequence[str]) -> List[float]:
        with self._lock:
            if self._clock() < self._down_until:
                raise RerankerError("reranker unavailable (recent connection failure; retry later)")
        try:
            return self._score(query, documents)
        except RerankerError as exc:
            pause = 0.0
            if str(exc).startswith("reranker unreachable"):
                pause = self.RETRY_AFTER_SECONDS
            elif str(exc).startswith("reranker warming"):
                pause = self.WARMING_RETRY_SECONDS
            if pause:
                with self._lock:
                    self._down_until = self._clock() + pause
            raise

    def _score(self, query: str, documents: Sequence[str]) -> List[float]:
        body = json.dumps({"query": query, "documents": list(documents)}).encode("utf-8")
        req = urllib.request.Request(f"{self.url}/rerank", data=body,
                                     headers={"Content-Type": "application/json"}, method="POST")
        try:
            with _LOOPBACK_OPENER.open(req, timeout=self.timeout) as resp:
                payload = json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            if exc.code == 503 and _error_field(exc) == "warming":
                raise RerankerError("reranker warming (model process starting)") from exc
            raise RerankerError(f"reranker unreachable: HTTPError {exc.code}") from exc
        except Exception as exc:  # noqa: BLE001 - any transport failure is "unavailable"
            raise RerankerError(f"reranker unreachable: {type(exc).__name__}") from exc
        if not isinstance(payload, dict):
            raise RerankerError("malformed reranker response")
        self.model = str(payload.get("model") or "unknown")
        return validate_scores(payload.get("scores"), len(documents))
